using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Flow.Launcher.Plugin;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public static class SearchHelper
{
    private const int MaxSearchConcurrency = 4;
    private const int PersistentCacheVersion = 1;
    private const string PersistentCacheFileName = "search-index.json";
    private static readonly TimeSpan IndexDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions PersistentCacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed record BucketDirectory(string Name, string Path);

    private sealed record ManifestFile(string BucketName, string Path, string PackageName);

    private sealed record ManifestEnumerationResult(
        IReadOnlyList<ManifestFile> Files,
        bool IsComplete);

    private sealed record CachedMatch(
        string ManifestPath,
        long Length,
        DateTime LastWriteTimeUtc,
        Match Value,
        string[] SearchTerms);

    private sealed class PersistedSearchIndex
    {
        public PersistedSearchIndex()
        {
        }

        public int SchemaVersion { get; set; }
        public string BucketBase { get; set; } = string.Empty;
        public List<PersistedEntry>? Entries { get; set; }
    }

    private sealed class PersistedEntry
    {
        public PersistedEntry()
        {
        }

        public string? ManifestPath { get; set; }
        public string? BucketName { get; set; }
        public string? PackageName { get; set; }
        public long Length { get; set; }
        public DateTime LastWriteTimeUtc { get; set; }
        public string[]? SearchTerms { get; set; }
        public PersistedMatch? Match { get; set; }
    }

    private sealed class PersistedMatch
    {
        public PersistedMatch()
        {
        }

        public string? Name { get; set; }
        public string? Version { get; set; }
        public string? FileName { get; set; }
        public string? Bucket { get; set; }
        public string? Path { get; set; }
        public string? Homepage { get; set; }
        public string? Description { get; set; }
    }

    private sealed class IndexBuildStatistics
    {
        public int CacheHits;
        public int PersistentCacheHits;
        public int FilesRead;
        public int ParsedManifests;
        public int InvalidManifests;
        public int ReadFailures;
        public int ParseFailures;
        public int FileStampFailures;
    }

    private sealed class SearchIndex
    {
        public SearchIndex(IReadOnlyList<CachedMatch> entries)
        {
            Entries = entries;
            BuiltAtUtc = DateTime.UtcNow;
        }

        public IReadOnlyList<CachedMatch> Entries { get; }
        public DateTime BuiltAtUtc { get; }
    }

    private static readonly ConcurrentDictionary<string, CachedMatch> ManifestCache = new(
        StringComparer.OrdinalIgnoreCase);
    private static readonly object IndexGate = new();
    private static readonly SemaphoreSlim PersistentCacheGate = new(1, 1);
    private static Task<SearchIndex>? _indexBuildTask;
    private static CancellationTokenSource? _indexBuildCancellation;
    private static SearchIndex? _searchIndex;
    private static string? _indexBucketBase;
    private static string? _persistentCachePath;
    private static long _buildGeneration;
    private static IPublicAPI? _api;

    public static void Initialize(PluginInitContext context)
    {
        _api = context.API;
        try
        {
            var cacheDirectory = context.CurrentPluginMetadata.PluginCacheDirectoryPath;
            _persistentCachePath = string.IsNullOrWhiteSpace(cacheDirectory)
                ? null
                : Path.Combine(cacheDirectory, PersistentCacheFileName);

            if (_persistentCachePath == null)
            {
                LogWarn("Flow Launcher did not provide a plugin cache directory; persistent search cache is disabled.");
            }
            else
            {
                LogDebug($"Persistent search cache path: {_persistentCachePath}.");
            }
        }
        catch (Exception exception)
        {
            _persistentCachePath = null;
            LogWarn($"Failed to initialize persistent search cache: {exception.Message}");
        }
    }

    public static async Task<List<Match>> GetResultAsync(
        string bucketBase,
        string keyword,
        string? bucketName = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = keyword.Trim();
        if (string.IsNullOrEmpty(query))
        {
            return new List<Match>();
        }

        var index = await GetSearchIndexAsync(bucketBase, cancellationToken);
        var matches = index.Entries
            .Where(item => string.IsNullOrWhiteSpace(bucketName)
                          || item.Value.Bucket.Equals(bucketName, StringComparison.OrdinalIgnoreCase))
            .Where(item => MatchesQuery(item, query))
            .Select(item => item.Value)
            .ToList();

        return matches;
    }

    public static void InvalidateCache()
    {
        CancellationTokenSource? buildCancellation;
        lock (IndexGate)
        {
            _buildGeneration++;
            buildCancellation = _indexBuildCancellation;
            _indexBuildCancellation = null;
            _indexBuildTask = null;
            _searchIndex = null;
            _indexBucketBase = null;
            ManifestCache.Clear();
        }

        buildCancellation?.Cancel();
        LogDebug("Invalidated in-memory manifest cache and search index.");
    }

    private static async Task<SearchIndex> GetSearchIndexAsync(
        string bucketBase,
        CancellationToken queryCancellation)
    {
        Task<SearchIndex> buildTask;
        lock (IndexGate)
        {
            if (_searchIndex != null
                && string.Equals(_indexBucketBase, bucketBase, StringComparison.OrdinalIgnoreCase)
                && DateTime.UtcNow - _searchIndex.BuiltAtUtc < IndexDuration)
            {
                return _searchIndex;
            }

            if (_indexBuildTask == null
                || _indexBuildTask.IsCompleted
                || _indexBuildTask.IsFaulted
                || _indexBuildTask.IsCanceled
                || !string.Equals(_indexBucketBase, bucketBase, StringComparison.OrdinalIgnoreCase))
            {
                var bucketRootChanged = !string.Equals(
                    _indexBucketBase,
                    bucketBase,
                    StringComparison.OrdinalIgnoreCase);
                var rebuildReason = _searchIndex == null
                    ? "no in-memory index"
                    : bucketRootChanged
                        ? "bucket root changed"
                        : "index expired or previous build was unavailable";
                LogDebug($"Starting search index build: reason={rebuildReason}.");
                if (bucketRootChanged)
                {
                    ManifestCache.Clear();
                }

                _indexBuildCancellation?.Cancel();
                _indexBuildCancellation = new CancellationTokenSource();
                _indexBucketBase = bucketBase;
                _buildGeneration++;
                _indexBuildTask = BuildSearchIndexAsync(
                    bucketBase,
                    _indexBuildCancellation.Token,
                    _buildGeneration);
            }

            buildTask = _indexBuildTask!;
        }

        try
        {
            var index = await buildTask.WaitAsync(queryCancellation);
            lock (IndexGate)
            {
                if (ReferenceEquals(_indexBuildTask, buildTask))
                {
                    _searchIndex = index;
                    return index;
                }
            }

            return await GetSearchIndexAsync(bucketBase, queryCancellation);
        }
        catch
        {
            lock (IndexGate)
            {
                if (ReferenceEquals(_indexBuildTask, buildTask) && buildTask.IsCompleted)
                {
                    _indexBuildTask = null;
                    _indexBuildCancellation = null;
                    _searchIndex = null;
                }
            }

            throw;
        }
    }

    private static async Task<SearchIndex> BuildSearchIndexAsync(
        string bucketBase,
        CancellationToken cancellationToken,
        long buildGeneration)
    {
        var stopwatch = Stopwatch.StartNew();
        var statistics = new IndexBuildStatistics();
        var bucketsDir = Path.Combine(bucketBase, "buckets");
        if (!Directory.Exists(bucketsDir))
        {
            throw new FileNotFoundException("Bucket Directory Not Found");
        }

        try
        {
            var bucketDirectories = GetBucketDirectories(bucketsDir);
            var manifestEnumeration = GetManifestFiles(bucketDirectories, cancellationToken);
            var manifestFiles = manifestEnumeration.Files;
            LogDebug(
                $"Enumerated Scoop manifests: buckets={bucketDirectories.Count}, " +
                $"manifestFiles={manifestFiles.Count}, " +
                $"complete={manifestEnumeration.IsComplete}.");

            IReadOnlyDictionary<string, PersistedEntry>? persistedEntries = null;
            if (ManifestCache.IsEmpty)
            {
                persistedEntries = await LoadPersistentCacheAsync(bucketBase, cancellationToken);
            }

            var entries = await LoadMatchesAsync(
                manifestFiles,
                persistedEntries,
                statistics,
                buildGeneration,
                cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            var persistentCacheWritten = false;
            var canPersist = manifestEnumeration.IsComplete
                             && statistics.ReadFailures == 0
                             && statistics.ParseFailures == 0
                             && statistics.InvalidManifests == 0
                             && statistics.FileStampFailures == 0;
            var persistentCacheFileExists = !string.IsNullOrWhiteSpace(_persistentCachePath)
                                             && File.Exists(_persistentCachePath);
            var persistentCacheNeedsWrite = persistedEntries != null
                ? persistedEntries.Count != manifestFiles.Count
                  || statistics.PersistentCacheHits != manifestFiles.Count
                : !persistentCacheFileExists
                  || statistics.CacheHits != manifestFiles.Count
                  || ManifestCache.Count != manifestFiles.Count;
            if (canPersist && persistentCacheNeedsWrite)
            {
                persistentCacheWritten = await SavePersistentCacheAsync(
                    bucketBase,
                    entries,
                    buildGeneration,
                    cancellationToken);
            }
            else if (!canPersist)
            {
                LogDebug(
                    $"Skipped persistent search cache write: complete={manifestEnumeration.IsComplete}, " +
                    $"readFailures={statistics.ReadFailures}, parseFailures={statistics.ParseFailures}, " +
                    $"invalid={statistics.InvalidManifests}, fileStampFailures={statistics.FileStampFailures}.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var index = new SearchIndex(entries);
            LogInfo(
                $"Built Scoop search index: buckets={bucketDirectories.Count}, " +
                $"manifestFiles={manifestFiles.Count}, entries={entries.Count}, " +
                $"cacheHits={statistics.CacheHits}, persistentHits={statistics.PersistentCacheHits}, " +
                $"filesRead={statistics.FilesRead}, parsed={statistics.ParsedManifests}, " +
                $"invalid={statistics.InvalidManifests}, readFailures={statistics.ReadFailures}, " +
                $"parseFailures={statistics.ParseFailures}, fileStampFailures={statistics.FileStampFailures}, " +
                $"persistentWrite={persistentCacheWritten}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F0}.");
            return index;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogDebug($"Cancelled Scoop search index build after {stopwatch.Elapsed.TotalMilliseconds:F0}ms.");
            throw;
        }
    }

    private static List<BucketDirectory> GetBucketDirectories(string bucketsDir)
    {
        return Directory.EnumerateDirectories(bucketsDir)
            .Select(path => new BucketDirectory(Path.GetFileName(path)!, path))
            .ToList();
    }

    private static ManifestEnumerationResult GetManifestFiles(
        IEnumerable<BucketDirectory> bucketDirectories,
        CancellationToken cancellationToken)
    {
        var manifestFiles = new List<ManifestFile>();
        var isComplete = true;

        foreach (var bucketDirectory in bucketDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var manifestDirectory = Path.Combine(bucketDirectory.Path, "bucket");
            if (!Directory.Exists(manifestDirectory))
            {
                isComplete = false;
                continue;
            }

            try
            {
                foreach (var filePath in Directory.EnumerateFiles(
                             manifestDirectory,
                             "*.json",
                             SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    manifestFiles.Add(new ManifestFile(
                        bucketDirectory.Name,
                        filePath,
                        Path.GetFileNameWithoutExtension(filePath)));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                isComplete = false;
                // A bucket can change while it is being indexed.
            }
        }

        return new ManifestEnumerationResult(manifestFiles, isComplete);
    }

    private static async Task<List<CachedMatch>> LoadMatchesAsync(
        IEnumerable<ManifestFile> manifestFiles,
        IReadOnlyDictionary<string, PersistedEntry>? persistedEntries,
        IndexBuildStatistics statistics,
        long buildGeneration,
        CancellationToken cancellationToken)
    {
        var results = new ConcurrentBag<CachedMatch>();
        var filesToRead = new List<ManifestFile>();

        foreach (var manifestFile in manifestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (persistedEntries != null
                && persistedEntries.TryGetValue(manifestFile.Path, out var persistedEntry)
                && TryRestorePersistedMatch(manifestFile, persistedEntry, out var persistedMatch))
            {
                if (TryStoreManifestCache(
                        manifestFile.Path,
                        persistedMatch!,
                        buildGeneration,
                        cancellationToken))
                {
                    results.Add(persistedMatch!);
                    Interlocked.Increment(ref statistics.PersistentCacheHits);
                }

                continue;
            }

            filesToRead.Add(manifestFile);
        }

        var parallelOptions = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Min(MaxSearchConcurrency, Math.Max(1, Environment.ProcessorCount))
        };

        await Parallel.ForEachAsync(filesToRead, parallelOptions, async (manifestFile, token) =>
        {
            var match = await LoadPackageAsync(
                manifestFile,
                statistics,
                buildGeneration,
                token);
            if (match != null)
            {
                results.Add(match);
            }
        });

        return results.ToList();
    }

    private static async Task<CachedMatch?> LoadPackageAsync(
        ManifestFile manifestFile,
        IndexBuildStatistics statistics,
        long buildGeneration,
        CancellationToken cancellationToken)
    {
        if (TryGetCachedMatch(manifestFile.Path, out var cachedMatch))
        {
            Interlocked.Increment(ref statistics.CacheHits);
            return cachedMatch;
        }

        Interlocked.Increment(ref statistics.FilesRead);
        string content;
        try
        {
            content = await File.ReadAllTextAsync(manifestFile.Path, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            Interlocked.Increment(ref statistics.ReadFailures);
            return null;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var document = JsonDocument.Parse(content);
            var manifest = document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement[0]
                : document.RootElement;

            if (manifest.ValueKind != JsonValueKind.Object)
            {
                Interlocked.Increment(ref statistics.InvalidManifests);
                return null;
            }

            var version = manifest.TryGetProperty("version", out var versionElement)
                ? versionElement.ToString()
                : "unknown";
            var manifestName = Path.GetFileName(manifestFile.Path);
            var packagesDirectory = Path.GetDirectoryName(manifestFile.Path);
            var match = new Match
            {
                Name = manifestFile.PackageName,
                Version = version,
                FileName = manifestName,
                Bucket = manifestFile.BucketName,
                Path = packagesDirectory,
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
            };

            var searchTerms = BuildSearchTerms(manifest, manifestFile.PackageName);
            Interlocked.Increment(ref statistics.ParsedManifests);
            if (TryGetFileStamp(manifestFile.Path, out var length, out var lastWriteTimeUtc))
            {
                var cached = new CachedMatch(
                    manifestFile.Path,
                    length,
                    lastWriteTimeUtc,
                    match,
                    searchTerms);
                return TryStoreManifestCache(
                           manifestFile.Path,
                           cached,
                           buildGeneration,
                           cancellationToken)
                    ? cached
                    : null;
            }

            Interlocked.Increment(ref statistics.FileStampFailures);
            return new CachedMatch(
                manifestFile.Path,
                0,
                default,
                match,
                searchTerms);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            Interlocked.Increment(ref statistics.ParseFailures);
            return null;
        }
    }

    private static async Task<IReadOnlyDictionary<string, PersistedEntry>?> LoadPersistentCacheAsync(
        string bucketBase,
        CancellationToken cancellationToken)
    {
        var cachePath = _persistentCachePath;
        if (string.IsNullOrWhiteSpace(cachePath) || !File.Exists(cachePath))
        {
            return null;
        }

        await PersistentCacheGate.WaitAsync(cancellationToken);
        try
        {
            await using var stream = new FileStream(
                cachePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var cache = await JsonSerializer.DeserializeAsync<PersistedSearchIndex>(
                stream,
                PersistentCacheJsonOptions,
                cancellationToken);

            if (cache == null
                || cache.SchemaVersion != PersistentCacheVersion
                || !PathsEqual(cache.BucketBase, bucketBase)
                || cache.Entries == null)
            {
                LogDebug("Ignoring persistent search cache because its identity or schema is invalid.");
                return null;
            }

            var entries = new Dictionary<string, PersistedEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in cache.Entries)
            {
                if (!IsValidPersistedEntry(entry)
                    || !entries.TryAdd(entry.ManifestPath!, entry))
                {
                    LogDebug("Ignoring persistent search cache because it contains an invalid or duplicate entry.");
                    return null;
                }
            }

            LogDebug($"Loaded persistent search cache: entries={entries.Count}.");
            return entries;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            LogDebug($"Ignoring unreadable persistent search cache: {exception.Message}");
            return null;
        }
        finally
        {
            PersistentCacheGate.Release();
        }
    }

    private static async Task<bool> SavePersistentCacheAsync(
        string bucketBase,
        IReadOnlyList<CachedMatch> entries,
        long buildGeneration,
        CancellationToken cancellationToken)
    {
        var cachePath = _persistentCachePath;
        if (string.IsNullOrWhiteSpace(cachePath)
            || !IsCurrentBuild(buildGeneration, cancellationToken))
        {
            return false;
        }

        var persistedCache = new PersistedSearchIndex
        {
            SchemaVersion = PersistentCacheVersion,
            BucketBase = bucketBase,
            Entries = entries.Select(ToPersistedEntry).ToList()
        };
        var temporaryPath = $"{cachePath}.tmp";
        var gateEntered = false;

        try
        {
            await PersistentCacheGate.WaitAsync(cancellationToken);
            gateEntered = true;
            if (!IsCurrentBuild(buildGeneration, cancellationToken))
            {
                return false;
            }

            var cacheDirectory = Path.GetDirectoryName(cachePath);
            if (string.IsNullOrWhiteSpace(cacheDirectory))
            {
                return false;
            }

            Directory.CreateDirectory(cacheDirectory);
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             64 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    persistedCache,
                    PersistentCacheJsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            lock (IndexGate)
            {
                if (!IsCurrentBuildLocked(buildGeneration, cancellationToken))
                {
                    return false;
                }

                if (File.Exists(cachePath))
                {
                    File.Replace(temporaryPath, cachePath, null);
                }
                else
                {
                    File.Move(temporaryPath, cachePath);
                }
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            LogWarn($"Failed to save persistent search cache: {exception.Message}");
            return false;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
            if (gateEntered)
            {
                PersistentCacheGate.Release();
            }
        }
    }

    private static bool TryRestorePersistedMatch(
        ManifestFile manifestFile,
        PersistedEntry persistedEntry,
        out CachedMatch? cachedMatch)
    {
        cachedMatch = null;
        if (!IsValidPersistedEntry(persistedEntry)
            || !PathsEqual(persistedEntry.ManifestPath!, manifestFile.Path)
            || !string.Equals(persistedEntry.BucketName, manifestFile.BucketName, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(persistedEntry.PackageName, manifestFile.PackageName, StringComparison.OrdinalIgnoreCase)
            || !TryGetFileStamp(manifestFile.Path, out var length, out var lastWriteTimeUtc)
            || length != persistedEntry.Length
            || lastWriteTimeUtc != NormalizeUtc(persistedEntry.LastWriteTimeUtc))
        {
            return false;
        }

        var match = persistedEntry.Match!;
        cachedMatch = new CachedMatch(
            manifestFile.Path,
            persistedEntry.Length,
            NormalizeUtc(persistedEntry.LastWriteTimeUtc),
            new Match
            {
                Name = match.Name!,
                Version = match.Version,
                FileName = match.FileName,
                Bucket = match.Bucket!,
                Path = match.Path,
                Homepage = match.Homepage,
                Description = match.Description
            },
            persistedEntry.SearchTerms!.ToArray());
        return true;
    }

    private static PersistedEntry ToPersistedEntry(CachedMatch cachedMatch)
    {
        var match = cachedMatch.Value;
        return new PersistedEntry
        {
            ManifestPath = cachedMatch.ManifestPath,
            BucketName = match.Bucket,
            PackageName = match.Name,
            Length = cachedMatch.Length,
            LastWriteTimeUtc = NormalizeUtc(cachedMatch.LastWriteTimeUtc),
            SearchTerms = cachedMatch.SearchTerms.ToArray(),
            Match = new PersistedMatch
            {
                Name = match.Name,
                Version = match.Version,
                FileName = match.FileName,
                Bucket = match.Bucket,
                Path = match.Path,
                Homepage = match.Homepage,
                Description = match.Description
            }
        };
    }

    private static bool IsValidPersistedEntry(PersistedEntry? entry)
    {
        var match = entry?.Match;
        return entry != null
               && !string.IsNullOrWhiteSpace(entry.ManifestPath)
               && !string.IsNullOrWhiteSpace(entry.BucketName)
               && !string.IsNullOrWhiteSpace(entry.PackageName)
               && entry.Length >= 0
               && entry.LastWriteTimeUtc != default
               && entry.SearchTerms is { Length: > 0 }
               && entry.SearchTerms.All(term => !string.IsNullOrWhiteSpace(term))
               && match != null
               && !string.IsNullOrWhiteSpace(match.Name)
               && !string.IsNullOrWhiteSpace(match.FileName)
               && !string.IsNullOrWhiteSpace(match.Bucket)
               && string.Equals(entry.PackageName, match.Name, StringComparison.OrdinalIgnoreCase)
               && string.Equals(entry.BucketName, match.Bucket, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryStoreManifestCache(
        string filePath,
        CachedMatch cachedMatch,
        long buildGeneration,
        CancellationToken cancellationToken)
    {
        lock (IndexGate)
        {
            if (!IsCurrentBuildLocked(buildGeneration, cancellationToken))
            {
                return false;
            }

            ManifestCache[filePath] = cachedMatch;
            return true;
        }
    }

    private static bool IsCurrentBuild(long buildGeneration, CancellationToken cancellationToken)
    {
        lock (IndexGate)
        {
            return IsCurrentBuildLocked(buildGeneration, cancellationToken);
        }
    }

    private static bool IsCurrentBuildLocked(long buildGeneration, CancellationToken cancellationToken)
    {
        return _buildGeneration == buildGeneration && !cancellationToken.IsCancellationRequested;
    }

    private static DateTime NormalizeUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
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

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (IOException)
        {
            // A stale temporary cache file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // A stale temporary cache file is harmless.
        }
    }

    private static string[] BuildSearchTerms(JsonElement manifest, string packageName)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddSearchTerm(terms, packageName);
        AddPropertyValues(manifest, "name", terms);
        AddPropertyValues(manifest, "bin", terms);
        AddPropertyValues(manifest, "shortcuts", terms);

        if (manifest.TryGetProperty("psmodule", out var psmodule))
        {
            AddPropertyValues(psmodule, "name", terms);
        }

        if (manifest.TryGetProperty("architecture", out var architecture)
            && architecture.ValueKind == JsonValueKind.Object)
        {
            foreach (var architectureEntry in architecture.EnumerateObject())
            {
                if (architectureEntry.Value.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                AddPropertyValues(architectureEntry.Value, "bin", terms);
                AddPropertyValues(architectureEntry.Value, "shortcuts", terms);
            }
        }

        return terms.ToArray();
    }

    private static void AddPropertyValues(
        JsonElement element,
        string propertyName,
        ISet<string> terms)
    {
        if (element.TryGetProperty(propertyName, out var property))
        {
            AddSearchValues(property, terms);
        }
    }

    private static void AddSearchValues(JsonElement element, ISet<string> terms)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                AddSearchTerm(terms, element.GetString());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddSearchValues(item, terms);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddSearchValues(property.Value, terms);
                }

                break;
        }
    }

    private static void AddSearchTerm(ISet<string> terms, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var term = value.Trim();
        terms.Add(term);

        var pathValue = term.Replace('\\', '/');
        var fileName = Path.GetFileNameWithoutExtension(pathValue);
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            terms.Add(fileName);
        }
    }

    private static bool MatchesQuery(CachedMatch match, string query)
    {
        return match.SearchTerms.Any(term => term.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetCachedMatch(string filePath, out CachedMatch? match)
    {
        match = null;
        if (!TryGetFileStamp(filePath, out var length, out var lastWriteTimeUtc))
        {
            return false;
        }

        if (!ManifestCache.TryGetValue(filePath, out var cachedMatch)
            || cachedMatch.Length != length
            || cachedMatch.LastWriteTimeUtc != lastWriteTimeUtc)
        {
            return false;
        }

        match = cachedMatch;
        return true;
    }

    private static bool TryGetFileStamp(
        string filePath,
        out long length,
        out DateTime lastWriteTimeUtc)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            if (!fileInfo.Exists)
            {
                length = 0;
                lastWriteTimeUtc = default;
                return false;
            }

            length = fileInfo.Length;
            lastWriteTimeUtc = fileInfo.LastWriteTimeUtc;
            return true;
        }
        catch (IOException)
        {
            length = 0;
            lastWriteTimeUtc = default;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            length = 0;
            lastWriteTimeUtc = default;
            return false;
        }
    }

    private static void LogDebug(string message)
    {
        _api?.LogDebug(nameof(SearchHelper), message);
    }

    private static void LogInfo(string message)
    {
        _api?.LogInfo(nameof(SearchHelper), message);
    }

    private static void LogWarn(string message)
    {
        _api?.LogWarn(nameof(SearchHelper), message);
    }
}
