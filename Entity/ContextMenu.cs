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
                Title = $"From bucket {resultContext.Match.Bucket}",
                SubTitle = $"Version: {resultContext.Match.Version}, click to check new version",
                Icon = selectedResult.Icon,
                AsyncAction = async _ =>
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try
                    {
                        var latestVersion = await new VersionChecker(resultContext.Match)
                            .GetLatestVersionAsync(cts.Token);

                        if (latestVersion == null)
                        {
                            _context.API.ShowMsgError("Error",
                                $"Failed to check new version for {resultContext.Match.Name}.");
                            return false;
                        }

                        if (VersionChecker.IsSameVersion(latestVersion, resultContext.Match.Version))
                        {
                            _context.API.ShowMsg("No update",
                                $"The version {resultContext.Match.Version} for {resultContext.Match.Name} is already the latest version.");
                            return false;
                        }

                        _context.API.ShowMsg("New version",
                            $"The latest version of {resultContext.Match.Name} is {latestVersion}.");
                        return false;
                    }
                    catch (Exception)
                    {
                        // ignore
                    }

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
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.UpdateAsync(resultContext.Match, _context);
                    return false;
                }
            },
            new()
            {
                Title = "Uninstall",
                SubTitle = "Uninstall the selected app",
                Icon = () => ScoopInstance.TrashIcon,
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.UninstallAsync(resultContext.Match, _context);
                    return false;
                }
            },
            new()
            {
                Title = "Reset",
                SubTitle = "Reset the selected app",
                Icon = () => ScoopInstance.ResetIcon,
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.ResetAsync(resultContext.Match, _context);
                    return false;
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
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.InstallAsync(resultContext.Match, _context);
                    return false;
                }
            }
        };

        return results;
    }
}