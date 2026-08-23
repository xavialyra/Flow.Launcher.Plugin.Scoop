using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Windows.Media;
using Flow.Launcher.Plugin.Scoop.Entity;
using Flow.Launcher.Plugin.Scoop.Helper;

public static class ScoopInstance
{
    public static string? ScoopHomePath { get; private set; } = string.Empty;
    public static string? ScoopGlobalHomePath { get; private set; } = string.Empty;
    public static string? ScoopConfigFilePath { get; private set; } = string.Empty;
    public static ImageSource ScoopIcon { get; private set; }
    public static ImageSource HomeIcon { get; private set; }
    public static ImageSource InstallIcon { get; private set; }
    public static ImageSource TrashIcon { get; private set; }
    public static ImageSource UpdateIcon { get; private set; }
    public static ImageSource ResetIcon { get; private set; }

    /// <summary>
    /// Checks if scoop-search should run in verbose mode.
    /// </summary>
    public static bool IsVerbose()
    {
        return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SCOOP_SEARCH_VERBOSE"));
    }

    /// <summary>
    /// Gets the home directory of the current user.
    /// </summary>
    private static string? GetHomeDir()
    {
        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
        Debug.WriteLine($"env:USERPROFILE={userProfile ?? ""}");

        if (string.IsNullOrEmpty(userProfile))
        {
            throw new InvalidOperationException("Missing home directory.");
        }

        return userProfile;
    }

    /// <summary>
    /// Path to the scoop config file.
    /// </summary>
    private static string? GetScoopConfigFilePath(string? homeDir)
    {
        if (homeDir == null)
        {
            return null;
        }

        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Debug.WriteLine($"env:XDG_CONFIG_HOME={xdgConfigHome ?? ""}");

        return !string.IsNullOrEmpty(xdgConfigHome)
            ? Path.Combine(xdgConfigHome, "scoop", "config.json")
            : Path.Combine(homeDir, ".config", "scoop", "config.json");
    }

    /// <summary>
    /// Returns the path to the root of scoop. Logic follows Scoop's logic for resolving the home directory.
    /// </summary>
    private static string? GetScoopHome(Settings settings, string? globalPath)
    {
        var configuredPath = GetValidScoopDirectory(settings.ScoopHome);
        if (configuredPath != null
            && (globalPath == null || !PathsEqual(configuredPath, globalPath)))
        {
            return configuredPath;
        }

        var scoopPath = GetValidScoopDirectory(Environment.GetEnvironmentVariable("SCOOP"));
        if (scoopPath != null
            && (globalPath == null || !PathsEqual(scoopPath, globalPath)))
        {
            return scoopPath;
        }

        var homeDir = GetHomeDir();
        var parsedConfig = LoadScoopConfig(homeDir);
        var configPath = GetValidScoopDirectory(parsedConfig?.RootPath);
        if (configPath != null
            && (globalPath == null || !PathsEqual(configPath, globalPath)))
        {
            return configPath;
        }

        foreach (var commandRoot in GetScoopCommandRoots())
        {
            if (globalPath == null || !PathsEqual(commandRoot, globalPath))
            {
                return commandRoot;
            }
        }

        var defaultUserPath = homeDir == null
            ? null
            : GetValidScoopDirectory(Path.Combine(homeDir, "scoop"));
        return defaultUserPath != null
               && (globalPath == null || !PathsEqual(defaultUserPath, globalPath))
            ? defaultUserPath
            : null;
    }

    private static string? GetScoopGlobalHome(Settings settings)
    {
        var configuredPath = GetValidScoopDirectory(settings.ScoopGlobalHome);
        if (configuredPath != null)
        {
            return configuredPath;
        }

        var globalPath = GetValidScoopDirectory(Environment.GetEnvironmentVariable("SCOOP_GLOBAL"));
        if (globalPath != null)
        {
            return globalPath;
        }

        var homeDir = Environment.GetEnvironmentVariable("USERPROFILE");
        var parsedConfig = LoadScoopConfig(homeDir);
        var configPath = GetValidScoopDirectory(parsedConfig?.GlobalPath);
        if (configPath != null)
        {
            return configPath;
        }

        var defaultGlobalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "scoop");
        return GetValidScoopDirectory(defaultGlobalPath);
    }

    private static ScoopConfig? LoadScoopConfig(string? homeDir)
    {
        var configPath = GetScoopConfigFilePath(homeDir);
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ScoopConfig>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions
                {
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    PropertyNameCaseInsensitive = true
                });
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? GetValidScoopDirectory(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return null;
        }

        return Directory.Exists(Path.Combine(path, "apps")) ? path : null;
    }

    private static IEnumerable<string> GetScoopCommandRoots()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield break;
        }

        var output = RunCommand("where", "scoop");
        if (string.IsNullOrWhiteSpace(output))
        {
            yield break;
        }

        foreach (var commandPath in output.Split(
                     new[] { '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var directory = Path.GetDirectoryName(commandPath.Trim());
            if (directory == null)
            {
                continue;
            }

            var root = Path.GetFileName(directory).Equals("shims", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(directory)
                : directory;
            var validRoot = GetValidScoopDirectory(root);
            if (validRoot != null)
            {
                yield return validRoot;
            }
        }
    }

    // Helper function to run a command and get its standard output
    private static string? RunCommand(string fileName, string arguments)
    {
        try
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(processStartInfo);
            if (process == null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the Scoop instance, including home path, config file path, and icon.
    /// </summary>
    public static void LoadInstance(Settings settings)
    {
        ScoopGlobalHomePath = GetScoopGlobalHome(settings);
        ScoopHomePath = GetScoopHome(settings, ScoopGlobalHomePath);
        ScoopConfigFilePath = GetScoopConfigFilePath(Environment.GetEnvironmentVariable("USERPROFILE"));
        ScoopIcon = LoadIcon("scoop-icon.png")!;
        HomeIcon = LoadIcon("home.png")!;
        InstallIcon = LoadIcon("install.png")!;
        TrashIcon = LoadIcon("trash.png")!;
        UpdateIcon = LoadIcon("update.png")!;
        ResetIcon = LoadIcon("reset.png")!;
    }

    public static void LoadScoopHome(Settings settings)
    {
        ScoopGlobalHomePath = GetScoopGlobalHome(settings);
        ScoopHomePath = GetScoopHome(settings, ScoopGlobalHomePath);
        ScoopStatusHelper.InvalidateCache();
        SearchHelper.InvalidateCache();
    }

    public static IReadOnlyList<ScoopInstallation> GetInstallations()
    {
        var installations = new List<ScoopInstallation>();
        AddInstallation(installations, ScoopHomePath, ScoopInstallScope.User);
        AddInstallation(installations, ScoopGlobalHomePath, ScoopInstallScope.Global);
        return installations;
    }

    public static bool HasInstallation => GetInstallations().Count > 0;

    public static bool IsAdministrator()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity)
                .IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static ScoopInstallation? GetInstallation(ScoopInstallScope scope)
    {
        return GetInstallations().FirstOrDefault(item => item.Scope == scope);
    }

    public static string GetPrimaryRootPath() =>
        GetInstallations().FirstOrDefault()?.RootPath
        ?? throw new InvalidOperationException("Scoop installation not found.");

    public static IEnumerable<ScoopAppInstallation> GetInstalledApps(
        IReadOnlyList<ScoopInstallation>? installations = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var installation in installations ?? GetInstallations())
        {
            cancellationToken.ThrowIfCancellationRequested();

            string[] appDirectories;
            try
            {
                appDirectories = Directory.GetDirectories(installation.AppsPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var directoryPath in appDirectories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var appName = Path.GetFileName(directoryPath);
                if (!string.IsNullOrEmpty(appName))
                {
                    yield return new ScoopAppInstallation(appName, directoryPath, installation);
                }
            }
        }
    }

    public static string GetDefaultGlobalAppsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "scoop",
        "apps");

    public static string GetAppsPath(ScoopInstallScope scope) =>
        GetInstallation(scope)?.AppsPath
        ?? throw new InvalidOperationException(
            scope == ScoopInstallScope.Global
                ? "global Scoop installation not found."
                : "Scoop installation not found.");

    private static void AddInstallation(
        ICollection<ScoopInstallation> installations,
        string? rootPath,
        ScoopInstallScope scope)
    {
        var validRoot = GetValidScoopDirectory(rootPath);
        if (validRoot == null
            || installations.Any(item => PathsEqual(item.RootPath, validRoot)))
        {
            return;
        }

        installations.Add(new ScoopInstallation(validRoot, scope));
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Loads the Scoop icon from the specified path.
    /// </summary>
    private static ImageSource? LoadIcon(string fileName)
    {
        try
        {
            var iconPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "Images",
                fileName
            );

            return IconHelper.GetIconAsPath(iconPath);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private class ScoopConfig
    {
        [JsonPropertyName("root_path")]
        public string? RootPath { get; set; }

        [JsonPropertyName("global_path")]
        public string? GlobalPath { get; set; }
    }
}