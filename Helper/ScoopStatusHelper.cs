using System;
using System.Collections.Generic;
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
