using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public class ListHelper
{
    public static List<Match> GetResult(
        IReadOnlyList<ScoopInstallation> installations,
        string keyword,
        string? bucketName = null,
        int limit = -1,
        CancellationToken cancellationToken = default)
    {
        if (!installations.Any(installation => Directory.Exists(installation.AppsPath)))
        {
            throw new FileNotFoundException("No Scoop apps directory found.");
        }

        var installApps = new List<Match>();
        foreach (var app in ScoopInstance.GetInstalledApps(installations, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installation = app.Installation;
            var appDir = app.DirectoryPath;
            var appCurrPath = Path.Combine(appDir, "current");
            var manifestPath = Path.Combine(appCurrPath, "manifest.json");
            var installConfigPath = Path.Combine(appCurrPath, "install.json");

            try
            {
                using var docResult = JsonDocument.Parse(File.ReadAllText(manifestPath));
                var manifest = docResult.RootElement.ValueKind == JsonValueKind.Array
                    ? docResult.RootElement[0]
                    : docResult.RootElement;

                var version = manifest.TryGetProperty("version", out var versionElement)
                    ? versionElement.GetString()
                    : "unknown";

                using var installJsonDocument = JsonDocument.Parse(File.ReadAllText(installConfigPath));
                var currBucketName = installJsonDocument.RootElement.TryGetProperty("bucket", out var bucketElement)
                    ? bucketElement.GetString()
                    : "unknown";

                if (bucketName != null
                    && !string.Equals(currBucketName, bucketName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var bin = manifest.TryGetProperty("shortcuts", out var shortcutsElement)
                    ? GetFirstShortcutPath(shortcutsElement)
                    : manifest.TryGetProperty("bin", out var binElement)
                        ? GetFirstShortcutPath(binElement)
                        : null;

                var appName = app.Name;
                var appExePath = bin == null ? null : Path.Combine(appCurrPath, bin);
                var icon = appExePath == null ? null : IconHelper.GetIconAsImageSource(appExePath);

                if (string.IsNullOrEmpty(keyword)
                    || appName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                {
                    installApps.Add(new Match
                    {
                        Name = appName,
                        Version = version,
                        Bucket = currBucketName!,
                        FileName = bin,
                        Path = appCurrPath,
                        Icon = icon,
                        InstallScope = installation.Scope,
                        InstallationRootPath = installation.RootPath,
                        Checkver = manifest.TryGetProperty("checkver", out var checkverElement)
                            ? JsonNode.Parse(checkverElement.GetRawText())
                            : null,
                        Homepage = manifest.TryGetProperty("homepage", out var homePage)
                                   && homePage.ValueKind == JsonValueKind.String
                            ? homePage.GetString()
                            : null,
                        Description = manifest.TryGetProperty("description", out var description)
                            ? description.ValueKind switch
                            {
                                JsonValueKind.String => description.GetString(),
                                JsonValueKind.Array when description.GetArrayLength() > 0
                                                         && description[0].ValueKind == JsonValueKind.String
                                    => description[0].GetString(),
                                _ => null
                            }
                            : null
                    });
                }

                if (limit != -1 && installApps.Count >= limit)
                {
                    return installApps;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // An app can change while it is being listed.
            }
        }

        return installApps;
    }

    private static string? GetFirstShortcutPath(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() <= 0) return null;

        var firstShortcut = element[0];

        if (firstShortcut.ValueKind == JsonValueKind.Array && firstShortcut.GetArrayLength() > 0)
        {
            var executablePath = firstShortcut[0];

            if (executablePath.ValueKind == JsonValueKind.String)
            {
                return executablePath.GetString();
            }
        }
        else if (firstShortcut.ValueKind == JsonValueKind.String)
        {
            return firstShortcut.GetString();
        }

        return null;
    }
}