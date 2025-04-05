using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Newtonsoft.Json.Linq;
using Match = Flow.Launcher.Plugin.Scoop.Entity.Match;

public class VersionChecker
{
    private readonly JsonNode? _config;

    private readonly string? _homePage;

    private readonly HttpClient _httpClient;

    public VersionChecker(Match match)
    {
        _config = match.Checkver ?? throw new ArgumentNullException(nameof(match.Checkver));
        _homePage = match.Homepage;

        var handler = new HttpClientHandler
            { AllowAutoRedirect = true, CookieContainer = new System.Net.CookieContainer() };
        _httpClient = new HttpClient(handler);

        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");

        var userAgent = _config is JsonObject configObject ? GetStringProperty(configObject, "userAgent") : null;

        if (string.IsNullOrEmpty(userAgent)) return;
        _httpClient.DefaultRequestHeaders.UserAgent.Clear();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
    }

    private static string? GetStringProperty(JsonObject jsonObject, string propertyName) =>
        jsonObject.TryGetPropertyValue(propertyName, out var property) && property != null
            ? property.GetValue<string>()
            : null;

    private static bool? GetBoolProperty(JsonObject jsonObject, string propertyName)
    {
        if (!jsonObject.TryGetPropertyValue(propertyName, out var property) || property == null) return null;
        if (property is JsonValue jv && jv.TryGetValue(out bool boolValue))
        {
            return boolValue;
        }

        return null;
    }

    public async Task<string?> GetLatestVersionAsync(CancellationToken cancellationToken)
    {
        return _config switch
        {
            JsonValue configValue when configValue.TryGetValue(out string? checkverType) => checkverType
                switch
                {
                    "github" => await GetLatestVersionFromGithubAsync(_homePage!, cancellationToken),
                    _ => await GetLatestVersionFromUrlAsync(_homePage!, cancellationToken, checkverType)
                },
            JsonObject configObject when configObject.TryGetPropertyValue("github", out var github) &&
                                         github != null => await GetLatestVersionFromGithubAsync(
                github.GetValue<string>(), cancellationToken),
            JsonObject configObject when
                configObject.TryGetPropertyValue("sourceforge", out var sourceforge) && sourceforge != null => await
                    GetLatestVersionFromSourceforgeAsync(sourceforge.GetValue<string>(),
                        GetStringProperty(configObject, "sourceforgepath"),
                        GetStringProperty(configObject, "regex") ?? GetStringProperty(configObject, "re"),
                        cancellationToken
                    ),
            JsonObject configObject when configObject.TryGetPropertyValue("url", out var url) && url != null =>
                await GetLatestVersionFromUrlAsync(url.GetValue<string>(), cancellationToken),
            _ => null
        };
    }

    private async Task<string?> GetLatestVersionFromUrlAsync(string url,
        CancellationToken cancellationToken,
        string? regexPattern = null,
        bool? isReverse = null)
    {
        string? jsonPath = null;
        string? xPath = null;
        string? replacePattern = null;
        bool? reverse = isReverse;

        if (_config is JsonObject configObject)
        {
            regexPattern ??= GetStringProperty(configObject, "regex") ?? GetStringProperty(configObject, "re");
            jsonPath = GetStringProperty(configObject, "jsonpath") ?? GetStringProperty(configObject, "jp");
            xPath = GetStringProperty(configObject, "xpath");
            replacePattern = GetStringProperty(configObject, "replace");
            reverse ??= GetBoolProperty(configObject, "reverse");
        }

        var content = await _httpClient.GetStringAsync(url, cancellationToken);
        if (!string.IsNullOrEmpty(jsonPath))
        {
            return ExtractVersionFromJson(content, jsonPath, regexPattern);
        }

        if (!string.IsNullOrEmpty(xPath))
        {
            return ExtractVersionFromXml(content, xPath, regexPattern);
        }

        return !string.IsNullOrEmpty(regexPattern)
            ? ExtractVersionFromRegex(content, regexPattern, replacePattern, reverse)
            : null;
    }

    private async Task<string?> GetLatestVersionFromGithubAsync(string githubRepo, CancellationToken cancellationToken)
    {
        var apiUrl = githubRepo.StartsWith("https://api.")
            ? githubRepo
            : $"https://api.github.com/repos/{githubRepo.Replace("https://github.com/", "")}/releases";

        await using var responseStream = await _httpClient.GetStreamAsync(apiUrl, cancellationToken);
        using var document = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement latestRelease = default;
        foreach (var release in root.EnumerateArray())
        {
            if (release.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            {
                continue;
            }

            if (release.TryGetProperty("published_at", out var publishedAt))
            {
                if (latestRelease.ValueKind == JsonValueKind.Undefined ||
                    DateTime.Parse(publishedAt.GetString()!) >
                    DateTime.Parse(latestRelease.GetProperty("published_at").GetString()!))
                {
                    latestRelease = release;
                }
            }
        }

        if (latestRelease.ValueKind == JsonValueKind.Undefined)
        {
            foreach (var release in root.EnumerateArray())
            {
                if (!release.TryGetProperty("published_at", out var publishedAt)) continue;
                if (latestRelease.ValueKind == JsonValueKind.Undefined ||
                    DateTime.Parse(publishedAt.GetString()!) >
                    DateTime.Parse(latestRelease.GetProperty("published_at").GetString()!))
                {
                    latestRelease = release;
                }
            }
        }

        return latestRelease.ValueKind != JsonValueKind.Undefined &&
               latestRelease.TryGetProperty("tag_name", out var tagName)
            ? tagName.GetString()
            : null;
    }

    private async Task<string?> GetLatestVersionFromSourceforgeAsync(string sourceforgeProject, string? sourceforgePath,
        string? regexPattern, CancellationToken cancellationToken)
    {
        var rssUrl = $"https://sourceforge.net/projects/{sourceforgeProject}/rss";
        if (!string.IsNullOrEmpty(sourceforgePath))
        {
            rssUrl += $"?path=/{sourceforgePath.TrimStart('/')}";
        }

        var rssContent = await _httpClient.GetStringAsync(rssUrl, cancellationToken);
        XmlDocument doc = new();
        doc.LoadXml(rssContent);
        var latestItem = doc.SelectSingleNode("//item[1]/link");
        if (latestItem == null) return null;
        var link = latestItem.InnerText;
        return !string.IsNullOrEmpty(regexPattern)
            ? Regex.Match(link, regexPattern) is { Success: true } match ? match.Groups[1].Value : null
            : link;
    }

    private string? ExtractVersionFromJson(string json, string jsonPath, string? regexPattern)
    {
        try
        {
            var jObject = JObject.Parse(json);

            var jsonMatch = jObject.SelectTokens(jsonPath).FirstOrDefault()?.ToString();

            if (string.IsNullOrEmpty(jsonMatch))
            {
                return null;
            }

            return string.IsNullOrEmpty(regexPattern)
                ? jsonMatch
                : ExtractVersionFromRegex(jsonMatch, regexPattern, null, false);
        }
        catch (Newtonsoft.Json.JsonReaderException)
        {
            var jArray = JArray.Parse(json);

            var jsonMatch = jArray.SelectTokens(jsonPath).FirstOrDefault()?.ToString();

            if (string.IsNullOrEmpty(jsonMatch))
            {
                return null;
            }

            return string.IsNullOrEmpty(regexPattern)
                ? jsonMatch
                : ExtractVersionFromRegex(jsonMatch, regexPattern, null, false);
        }
    }

    private string? ExtractVersionFromXml(string xml, string xPath, string? regexPattern)
    {
        XmlDocument doc = new();
        doc.LoadXml(xml);
        XmlNamespaceManager nsmgr = new(doc.NameTable);
        foreach (XmlNode node in doc.SelectNodes("//namespace::*")!)
        {
            if (node.LocalName == "xml") continue;
            var prefix = string.IsNullOrEmpty(node.LocalName) || node.LocalName == "xmlns"
                ? "ns"
                : node.LocalName;
            if (nsmgr.HasNamespace(prefix)) continue;
            if (node.Value != null) nsmgr.AddNamespace(prefix, node.Value);
        }

        var versionNode = doc.SelectSingleNode(xPath, nsmgr);
        return versionNode != null
            ? !string.IsNullOrEmpty(regexPattern)
                ? ExtractVersionFromRegex(versionNode.InnerText, regexPattern, null, false)
                : versionNode.InnerText
            : null;
    }

    private string? ExtractVersionFromRegex(string input, string regexPattern, string? replacePattern,
        bool? reverse)
    {
        var matches = Regex.Matches(input, regexPattern);
        if (matches.Count <= 0) return null;
        var match = reverse ?? false ? matches.Last() : matches.First();
        return !string.IsNullOrEmpty(replacePattern)
            ? Regex.Replace(match.Value, regexPattern, replacePattern)
            : match.Groups[1].Value;
    }

    public static bool IsSameVersion(string? version1, string? version2)
    {
        if (string.IsNullOrEmpty(version1) || string.IsNullOrEmpty(version2)) return false;

        try
        {
            if (version1.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                version1 = version1[1..];
            }

            if (version2.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            {
                version2 = version2[1..];
            }

            var v1 = new Version(version1);
            var v2 = new Version(version2);

            return v1.Equals(v2);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}