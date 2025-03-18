using System;
using System.Collections.Generic;
using System.Linq;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Handler;

public class ProviderManager
{
    private readonly PluginInitContext _context;

    public ProviderManager(PluginInitContext context)
    {
        _context = context;
    }

    private static readonly Dictionary<HotKeyType, (string Description, Func<PluginInitContext, ProviderBase> Factory)>
        Providers = new()
        {
            { HotKeyType.List, ("List installed apps", context => new ListProvider(context)) },
            { HotKeyType.Search, ("Search apps from added bucket", context => new SearchProvider(context)) },
        };

    public ProviderBase? GetProvider(string operationTypeStr)
    {
        operationTypeStr = operationTypeStr.ToLower();
        if (string.IsNullOrWhiteSpace(operationTypeStr)) return null;
        if (Enum.TryParse(operationTypeStr, true, out HotKeyType operationType))
            return Providers.TryGetValue(operationType, out var tuple) ? tuple.Factory(_context) : null;
        return null;
    }

    public List<Result> HotKeyResults()
    {
        return Providers.Select(item =>
        {
            return new Result
            {
                Title = item.Key.ToString().ToLower(),
                SubTitle = item.Value.Description,
                AutoCompleteText = item.Key.ToString().ToLower(),
                Icon = () => ScoopInstance.ScoopIcon,
                Action = _ =>
                {
                    _context.API.ChangeQuery(
                        $"{_context.CurrentPluginMetadata.ActionKeyword} {item.Key.ToString().ToLower()} ",
                        true);
                    return false;
                }
            };
        }).ToList();
    }
}