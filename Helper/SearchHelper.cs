using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public class SearchHelper
{
    public static async Task<List<Match>> GetResultAsync(string bucketBase, string keyword, string? bucketName = null)
    {
        if (string.IsNullOrEmpty(keyword))
        {
            return new List<Match>();
        }

        var bucketsDir = Path.Combine(bucketBase, "buckets");
        if (!Directory.Exists(bucketsDir))
        {
            throw new FileNotFoundException("Bucket Directory Not Found");
        }

        IEnumerable<string> bucketDirectories;
        if (string.IsNullOrEmpty(bucketName))
        {
            bucketDirectories = Directory.GetDirectories(bucketsDir);
        }
        else
        {
            var specificBucketPath = Path.Combine(bucketsDir, bucketName.ToLowerInvariant());
            if (Directory.Exists(specificBucketPath))
            {
                bucketDirectories = new[] { specificBucketPath };
            }
            else
            {
                return new List<Match>();
            }
        }


        var allMatches = new List<Match>();
        await Parallel.ForEachAsync(bucketDirectories, async (bucketDirectory, cancellationToken) =>
        {
            var bucketSearchResult = await SearchBucketAsync(bucketDirectory, keyword);
            lock (allMatches)
            {
                allMatches.AddRange(bucketSearchResult);
            }
        });
        return allMatches;
    }

    private static async Task<List<Match>> SearchBucketAsync(string baseDirectoryPath, string query)
    {
        if (!Directory.Exists(baseDirectoryPath))
        {
            return new List<Match>();
        }

        var directoryPath = Path.Combine(baseDirectoryPath, "bucket");
        if (!Directory.Exists(directoryPath))
        {
            return new List<Match>();
        }

        var allFiles = Directory.GetFiles(directoryPath, "*.json", SearchOption.TopDirectoryOnly);
        var results = new ConcurrentBag<Match>();

        await Parallel.ForEachAsync(allFiles,
            async (filePath, cancellationToken) =>
            {
                await MatchPackageAsync(filePath, query, results, cancellationToken);
            });

        return results.ToList();
    }

    private static async Task MatchPackageAsync(string filePath, string query, ConcurrentBag<Match> results,
        CancellationToken cancellationToken)
    {
        string content;
        try
        {
            content = await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch (Exception)
        {
            return;
        }

        JsonDocument? docResult = null;
        try
        {
            docResult = JsonDocument.Parse(content);
            var manifest = docResult.RootElement;

            if (!manifest.TryGetProperty("version", out var versionElement))
            {
                versionElement = default;
            }

            var version = versionElement.ValueKind != JsonValueKind.Undefined ? versionElement.ToString() : "unknown";
            var manifestName = Path.GetFileName(filePath);
            var stem = Path.GetFileNameWithoutExtension(manifestName);
            var lowerStem = stem.ToLowerInvariant();
            var lowerQuery = query.ToLowerInvariant();
            var isMatch = string.IsNullOrEmpty(query) || lowerStem.Contains(lowerQuery);
            var bucketName = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(filePath)));
            var packagesDir = Path.GetDirectoryName(filePath);

            if (isMatch)
            {
                var match = new Match
                {
                    Name = stem,
                    Version = version,
                    FileName = manifestName,
                    Bucket = bucketName!,
                    Path = packagesDir,
                    Checkver = manifest.TryGetProperty("checkver", out var checkverElement)
                        ? JsonNode.Parse(checkverElement.GetRawText())
                        : null,
                    Homepage = manifest.TryGetProperty("homepage", out var homePage)
                        ? homePage.GetString()
                        : null,
                    Description = manifest.TryGetProperty("description", out var description)
                        ? description.GetString()
                        : null
                };
                results.Add(match);
            }
        }
        catch (JsonException)
        {
        }
        finally
        {
            docResult?.Dispose();
        }
    }
}