using System;
using System.Collections.Generic;
using System.Threading;
using Flow.Launcher.Plugin.Scoop.Helper;

namespace Flow.Launcher.Plugin.Scoop.Entity;

public class ContextMenu : IContextMenu
{
    private readonly PluginInitContext _context;

    public ContextMenu(PluginInitContext context)
    {
        _context = context;
    }

    public List<Result> LoadContextMenus(Result selectedResult)
    {
        var resultContext = (ContextData)selectedResult.ContextData;

        return resultContext.HotKeyType switch
        {
            HotKeyType.List => GetListContextMenuItems(resultContext, selectedResult),
            HotKeyType.Search => GetSearchContextMenuItems(resultContext, selectedResult),
            _ => new List<Result>()
        };
    }

    private List<Result> GetListContextMenuItems(ContextData resultContext, Result selectedResult)
    {
        var results = new List<Result>
        {
            new()
            {
                Title = $"From bucket: {resultContext.Match.Bucket}, version: {resultContext.Match.Version}",
                SubTitle = "Click to check new version",
                Icon = selectedResult.Icon,
                AsyncAction = async _ =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    var latestVersion = await new VersionChecker(resultContext.Match)
                        .GetLatestVersionAsync(cts.Token);
                    if (latestVersion == null)
                    {
                        _context.API.ShowMsgError("Error", "Failed to check new version.");
                        return false;
                    }

                    if (latestVersion == resultContext.Match.Version)
                    {
                        _context.API.ShowMsg("No update", "The selected app is already the latest version.");
                        return false;
                    }

                    _context.API.ShowMsg("New version", $"The latest version is {latestVersion}.");
                    return false;
                }
            },
            new()
            {
                Title = "Open Homepage",
                SubTitle = "Open the homepage of the selected app",
                Icon = () => ScoopInstance.HomeIcon,
                Action = _ =>
                {
                    _context.API.OpenUrl(resultContext.Match.Homepage);
                    return true;
                }
            },
            new()
            {
                Title = "Update",
                SubTitle = "Update the selected app",
                Icon = () => ScoopInstance.UpdateIcon,
                Action = _ =>
                {
                    ScoopPwshExecutor.UpdateAsync(resultContext.Match, _context);
                    return true;
                }
            },
            new()
            {
                Title = "Uninstall",
                SubTitle = "Uninstall the selected app",
                Icon = () => ScoopInstance.TrashIcon,
                Action = _ =>
                {
                    ScoopPwshExecutor.UninstallAsync(resultContext.Match, _context);
                    return true;
                }
            },
            new()
            {
                Title = "Reset",
                SubTitle = "Reset the selected app",
                Icon = () => ScoopInstance.ResetIcon,
                Action = _ =>
                {
                    ScoopPwshExecutor.ResetAsync(resultContext.Match, _context);
                    return true;
                }
            }
        };

        return results;
    }

    private List<Result> GetSearchContextMenuItems(ContextData resultContext, Result selectedResult)
    {
        var results = new List<Result>
        {
            new()
            {
                Title = "Intro",
                SubTitle = resultContext.Match.Description,
                Icon = selectedResult.Icon
            },
            new()
            {
                Title = "Open Homepage",
                SubTitle = "Open the homepage of the selected app",
                Icon = () => ScoopInstance.HomeIcon,
                Action = _ =>
                {
                    _context.API.OpenUrl(resultContext.Match.Homepage);
                    return true;
                }
            },
            new()
            {
                Title = "Install",
                SubTitle = "Install the selected app",
                Icon = () => ScoopInstance.InstallIcon,
                Action = _ =>
                {
                    ScoopPwshExecutor.InstallAsync(resultContext.Match, _context);
                    return true;
                }
            }
        };

        return results;
    }
}