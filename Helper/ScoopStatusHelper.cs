using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public static class ScoopStatusHelper
{
    private static readonly object CacheLock = new();
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private static ScoopStatusReport? _cache;
    private static DateTime _cacheTimeUtc;
    private static string? _cacheScoopHome;

    public static async Task<ScoopStatusReport> GetResultAsync(
        string scoopHome,
        CancellationToken cancellationToken = default)
    {
        lock (CacheLock)
        {
            if (IsCacheValid(scoopHome))
            {
                return _cache!;
            }
        }

        await RefreshLock.WaitAsync(cancellationToken);
        try
        {
            lock (CacheLock)
            {
                if (IsCacheValid(scoopHome))
                {
                    return _cache!;
                }
            }

            var statusJson = await ScoopPwshExecutor.GetStatusJsonAsync(cancellationToken);
            var result = Parse(statusJson);
            AssignInstallScopes(result);

            lock (CacheLock)
            {
                _cache = result;
                _cacheTimeUtc = DateTime.UtcNow;
                _cacheScoopHome = scoopHome;
            }

            return result;
        }
        finally
        {
            RefreshLock.Release();
        }
    }

    public static ScoopStatusReport Parse(string statusJson)
    {
        if (string.IsNullOrWhiteSpace(statusJson))
        {
            return new ScoopStatusReport();
        }

        using var document = JsonDocument.Parse(statusJson);
        var root = document.RootElement;
        var report = new ScoopStatusReport();

        if (root.ValueKind != JsonValueKind.Object)
        {
            return report;
        }

        if (root.TryGetProperty("Apps", out var apps))
        {
            if (apps.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in apps.EnumerateArray())
                {
                    AddEntry(element, report.Apps);
                }
            }
            else
            {
                AddEntry(apps, report.Apps);
            }
        }

        return report;
    }

    public static void InvalidateCache()
    {
        lock (CacheLock)
        {
            _cache = null;
            _cacheTimeUtc = default;
            _cacheScoopHome = null;
        }
    }

    private static void AssignInstallScopes(ScoopStatusReport report)
    {
        var userInstallation = ScoopInstance.GetInstallation(ScoopInstallScope.User);
        var globalInstallation = ScoopInstance.GetInstallation(ScoopInstallScope.Global);
        if (globalInstallation == null)
        {
            foreach (var entry in report.Apps)
            {
                entry.InstallScope = ScoopInstallScope.User;
            }

            return;
        }

        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var counts = report.Apps
            .GroupBy(item => GetAppName(item.Name), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);

        foreach (var entry in report.Apps)
        {
            var appName = GetAppName(entry.Name);
            var occurrence = occurrences.TryGetValue(appName, out var currentOccurrence)
                ? currentOccurrence
                : 0;
            occurrences[appName] = occurrence + 1;

            var userVersion = userInstallation == null
                ? null
                : GetInstalledVersion(userInstallation, appName);
            var globalVersion = GetInstalledVersion(globalInstallation, appName);
            var userInstalled = userVersion != null || IsInstalled(userInstallation, appName);
            var globalInstalled = globalVersion != null || IsInstalled(globalInstallation, appName);

            if (globalInstalled && !userInstalled)
            {
                entry.InstallScope = ScoopInstallScope.Global;
            }
            else if (userInstalled && !globalInstalled)
            {
                entry.InstallScope = ScoopInstallScope.User;
            }
            else if (userInstalled && globalInstalled
                     && string.Equals(globalVersion, entry.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(userVersion, entry.InstalledVersion, StringComparison.OrdinalIgnoreCase))
            {
                entry.InstallScope = ScoopInstallScope.Global;
            }
            else if (userInstalled && globalInstalled
                     && string.Equals(userVersion, entry.InstalledVersion, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(globalVersion, entry.InstalledVersion, StringComparison.OrdinalIgnoreCase))
            {
                entry.InstallScope = ScoopInstallScope.User;
            }
            else if (userInstalled && globalInstalled && counts[appName] > 1)
            {
                // Scoop status emits global entries before local entries.
                entry.InstallScope = occurrence == 0
                    ? ScoopInstallScope.Global
                    : ScoopInstallScope.User;
            }
            else
            {
                // A single status row cannot identify the scope when both copies have the same version.
                entry.InstallScope = ScoopInstallScope.Unknown;
            }
        }
    }

    private static bool IsInstalled(ScoopInstallation? installation, string appName)
    {
        return installation != null
               && Directory.Exists(Path.Combine(installation.AppsPath, appName));
    }

    private static string? GetInstalledVersion(ScoopInstallation installation, string appName)
    {
        var manifestPath = Path.Combine(
            installation.AppsPath,
            appName,
            "current",
            "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement[0]
                : document.RootElement;
            return manifest.TryGetProperty("version", out var version)
                ? version.ToString()
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string GetAppName(string name)
    {
        var separator = name.LastIndexOfAny(new[] { '/', '\\' });
        return separator >= 0 ? name[(separator + 1)..] : name;
    }

    private static bool IsCacheValid(string scoopHome)
    {
        return _cache != null
               && string.Equals(_cacheScoopHome, scoopHome, StringComparison.OrdinalIgnoreCase)
               && DateTime.UtcNow - _cacheTimeUtc < CacheDuration;
    }

    private static void AddEntry(JsonElement element, ICollection<ScoopStatusEntry> entries)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var name = GetValue(element, "Name");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        entries.Add(new ScoopStatusEntry
        {
            Name = name,
            InstalledVersion = GetValue(element, "Installed Version"),
            LatestVersion = GetValue(element, "Latest Version"),
            MissingDependencies = GetValue(element, "Missing Dependencies"),
            Info = GetValue(element, "Info")
        });
    }

    private static string GetValue(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
               && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : string.Empty;
    }
}
