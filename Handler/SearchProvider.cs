using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Scoop.Entity;
using Flow.Launcher.Plugin.Scoop.Helper;

namespace Flow.Launcher.Plugin.Scoop.Handler;

public class SearchProvider : ProviderBase
{
    public SearchProvider(PluginInitContext context) : base(context)
    {
    }

    protected override async Task<List<Result>> GetResultAsync(string keyword)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<Result>
            {
                new()
                {
                    Title = "Please input keyword",
                    Icon = () => ScoopInstance.ScoopIcon,
                }
            };
        }

        var parts = keyword.Split('/');
        var searchKeyWord = parts.Length > 1 ? parts[1] : keyword;
        var bucketName = parts.Length > 1 ? parts[0] : null;

        var matches = await SearchHelper.GetResultAsync(
            bucketBase: ScoopInstance.ScoopHomePath!,
            keyword: searchKeyWord,
            bucketName: bucketName
        );

        return matches
            .Take(50)
            .Select(item => new Result
            {
                Title = item.Name,
                SubTitle = $"bucket: {item.Bucket}, version: {item.Version}",
                Icon = () => item.Icon ?? ScoopInstance.ScoopIcon,
                Score = _context.API.FuzzySearch(searchKeyWord, item.Name).Score,
                Action = action =>
                {
                    if (action.SpecialKeyState.CtrlPressed || string.IsNullOrWhiteSpace(item.FileName))
                    {
                        _context?.API.OpenDirectory(item.Path, item.FileName);
                        return true;
                    }

                    _context.API.OpenUrl(item.Homepage);
                    return false;
                },
                ContextData = ContextData.OfSearch(item)
            })
            .ToList();
    }
}