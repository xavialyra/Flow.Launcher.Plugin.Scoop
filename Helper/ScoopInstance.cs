using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Media;
using Flow.Launcher.Plugin.Scoop.Entity;
using Flow.Launcher.Plugin.Scoop.Helper;

public static class ScoopInstance
{
    public static string? ScoopHomePath { get; private set; } = string.Empty;
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
    private static string? GetScoopHome(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ScoopHome))
        {
            return settings.ScoopHome;
        }

        string? scoopPath = null;
        string? potentialPath = null;

        scoopPath = Environment.GetEnvironmentVariable("SCOOP_GLOBAL");
        if (IsValidScoopDirectory(scoopPath)) return scoopPath;
        
        scoopPath = Environment.GetEnvironmentVariable("SCOOP");
        if (IsValidScoopDirectory(scoopPath)) return scoopPath;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            potentialPath = RunCommand("where", "scoop");
            if (!string.IsNullOrWhiteSpace(potentialPath))
            {
                var firstPath = potentialPath.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(firstPath))
                {
                    var dir = Path.GetDirectoryName(firstPath);
                    if (dir != null)
                    {
                        scoopPath = Path.GetFileName(dir).Equals("shims", StringComparison.OrdinalIgnoreCase) ? Path.GetDirectoryName(dir) : dir;
        
                        if (IsValidScoopDirectory(scoopPath)) return scoopPath;
                    }
                }
            }
        }

        var homeDir = GetHomeDir();
        if (homeDir != null)
        {
            var scoopConfigPath = GetScoopConfigFilePath(homeDir);
            if (File.Exists(scoopConfigPath))
            {
                try
                {
                    var configContent = File.ReadAllText(scoopConfigPath);
                    var parsed = JsonSerializer.Deserialize<ScoopConfig>(configContent, new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        PropertyNameCaseInsensitive = true
                    });
                    if (IsValidScoopDirectory(parsed?.RootPath))
                    {
                        return parsed.RootPath;
                    }
                    potentialPath = Path.GetDirectoryName(scoopConfigPath);
                    if (IsValidScoopDirectory(potentialPath))
                    {
                         return potentialPath;
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                     potentialPath = Path.GetDirectoryName(scoopConfigPath);
                    if (IsValidScoopDirectory(potentialPath))
                    {
                         return potentialPath;
                    }
                }
            }
        }

        potentialPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "scoop");
        if (IsValidScoopDirectory(potentialPath)) return potentialPath;


        if (homeDir == null) return null;
        potentialPath = Path.Combine(homeDir, "scoop");
        return IsValidScoopDirectory(potentialPath) ? potentialPath : null;
    }
    
    // Helper function to validate a potential Scoop directory
    private static bool IsValidScoopDirectory(string? path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
        {
            return false;
        }
        return Directory.Exists(Path.Combine(path, "apps"));
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
        ScoopHomePath = GetScoopHome(settings);
        ScoopConfigFilePath = GetScoopConfigFilePath(ScoopHomePath);
        ScoopIcon = LoadIcon("scoop-icon.png")!;
        HomeIcon = LoadIcon("home.png")!;
        InstallIcon = LoadIcon("install.png")!;
        TrashIcon = LoadIcon("trash.png")!;
        UpdateIcon = LoadIcon("update.png")!;
        ResetIcon = LoadIcon("reset.png")!;
    }

    public static void LoadScoopHome(Settings settings)
    {
        ScoopHomePath = GetScoopHome(settings);
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
        public string? RootPath { get; set; }
    }
}