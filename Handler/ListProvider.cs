using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Scoop.Entity;
using Flow.Launcher.Plugin.Scoop.Helper;

namespace Flow.Launcher.Plugin.Scoop.Handler;

public class ListProvider : ProviderBase
{
    public ListProvider(PluginInitContext context) : base(context)
    {
    }

    protected override async Task<List<Result>> GetResultAsync(string keyword)
    {
        var matches = await Task.Run(() => ListHelper.GetResult(ScoopInstance.ScoopHomePath!, keyword));

        return matches
            .Select(item => new Result
            {
                Title = item.Name,
                SubTitle = item.Description,
                Icon = () => item.Icon ?? ScoopInstance.ScoopIcon,
                Score = _context.API.FuzzySearch(keyword, item.Name).Score,
                Action = action =>
                {
                    if (action.SpecialKeyState.CtrlPressed || string.IsNullOrWhiteSpace(item.FileName))
                    {
                        _context.API.OpenDirectory(item.Path, item.FileName);
                        return true;
                    }

                    if (!string.IsNullOrWhiteSpace(item.Path))
                        _context.API.OpenUrl(Path.Combine(item.Path, item.FileName));
                    return true;
                },
                ContextData = ContextData.OfList(item)
            })
            .ToList();
    }
}