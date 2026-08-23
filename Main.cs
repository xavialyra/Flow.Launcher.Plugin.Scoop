using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Controls;
using Flow.Launcher.Plugin.Scoop.Entity;
using Flow.Launcher.Plugin.Scoop.Handler;
using Flow.Launcher.Plugin.Scoop.Helper;
using Flow.Launcher.Plugin.Scoop.Views;
using ContextMenu = Flow.Launcher.Plugin.Scoop.Entity.ContextMenu;

namespace Flow.Launcher.Plugin.Scoop;

public class Scoop : IAsyncPlugin, IContextMenu, ISettingProvider
{
    private PluginInitContext _context;
    private ProviderManager _providerManager;
    private IContextMenu _contextMenu;
    private static Settings _settings;

    public Task InitAsync(PluginInitContext context)
    {
        _context = context;
        SearchHelper.Initialize(context);
        _providerManager = new ProviderManager(context);
        _contextMenu = new ContextMenu(context);
        _settings = _context.API.LoadSettingJsonStorage<Settings>();
        ScoopInstance.LoadInstance(_settings);
        var installations = ScoopInstance.GetInstallations();
        var detectedRoots = installations.Count == 0
            ? "none"
            : string.Join(", ", installations.Select(item =>
                $"{item.Scope}: root={item.RootPath}, apps={item.AppsPath}"));
        _context.API.LogInfo(
            nameof(Scoop),
            $"Detected Scoop installations: {detectedRoots}; administrator={ScoopInstance.IsAdministrator()}");
        return Task.CompletedTask;
    }

    public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(query.Search))
            return _providerManager.HotKeyResults();
        var provider = _providerManager.GetProvider(query.FirstSearch);
        if (provider == null)
        {
            return _providerManager.HotKeyResults()
                .Where(result => _context.API.FuzzySearch(query.Search, result.Title).Score > 0)
                .ToList();
        }

        return await provider.Handle(query.SecondSearch, token);
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        return _contextMenu.LoadContextMenus(selectedResult);
    }

    public Control CreateSettingPanel()
    {
        return new SettingsControl(_settings);
    }
}