using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows.Media;
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
    private static string? GetScoopHome()
    {
        var scoopEnv = Environment.GetEnvironmentVariable("SCOOP_GLOBAL")
                       ?? Environment.GetEnvironmentVariable("SCOOP");

        if (!string.IsNullOrEmpty(scoopEnv))
        {
            return scoopEnv;
        }

        var homeDir = GetHomeDir();
        if (homeDir == null)
        {
            return null;
        }

        var scoopConfigPath = GetScoopConfigFilePath(homeDir);

        if (!File.Exists(scoopConfigPath))
        {
            return Path.Combine(homeDir, "scoop");
        }

        try
        {
            var configContent = File.ReadAllText(scoopConfigPath);

            var parsed = JsonSerializer.Deserialize<ScoopConfig>(configContent, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                PropertyNameCaseInsensitive = true
            });

            return parsed?.RootPath ?? Path.Combine(homeDir, "scoop");
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return Path.Combine(homeDir, "scoop");
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Loads the Scoop instance, including home path, config file path, and icon.
    /// </summary>
    public static void LoadInstance()
    {
        ScoopHomePath = GetScoopHome();
        ScoopConfigFilePath = GetScoopConfigFilePath(ScoopHomePath);
        ScoopIcon = LoadIcon("scoop-icon.png")!;
        HomeIcon = LoadIcon("home.png")!;
        InstallIcon = LoadIcon("install.png")!;
        TrashIcon = LoadIcon("trash.png")!;
        UpdateIcon = LoadIcon("update.png")!;
        ResetIcon = LoadIcon("reset.png")!;
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