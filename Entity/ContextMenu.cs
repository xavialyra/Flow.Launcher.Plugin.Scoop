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
        if (selectedResult.ContextData is not ContextData resultContext)
        {
            return new List<Result>();
        }

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
                Title = resultContext.Match.InstallScope == ScoopInstallScope.Global
                    ? $"global app from bucket {resultContext.Match.Bucket}"
                    : $"App from bucket {resultContext.Match.Bucket}",
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
            }
        };

        if (resultContext.Match.InstallScope != ScoopInstallScope.Global
            || ScoopInstance.IsAdministrator())
        {
            results.Add(new Result
            {
                Title = "Update",
                SubTitle = "Update the selected app",
                Icon = () => ScoopInstance.UpdateIcon,
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.UpdateAsync(resultContext.Match, _context);
                    return false;
                }
            });
            results.Add(new Result
            {
                Title = "Uninstall",
                SubTitle = "Uninstall the selected app",
                Icon = () => ScoopInstance.TrashIcon,
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.UninstallAsync(resultContext.Match, _context);
                    return false;
                }
            });
        }

        if (resultContext.Match.InstallScope != ScoopInstallScope.Global)
        {
            results.Add(new Result
            {
                Title = "Reset",
                SubTitle = "Reset the selected app",
                Icon = () => ScoopInstance.ResetIcon,
                AsyncAction = async _ =>
                {
                    await ScoopPwshExecutor.ResetAsync(resultContext.Match, _context);
                    return false;
                }
            });
        }

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
            CreateInstallResult(resultContext.Match)
        };

        if (ScoopInstance.IsAdministrator())
        {
            results.Add(CreateGlobalInstallResult(resultContext.Match));
        }

        return results;
    }

    private Result CreateInstallResult(Match match)
    {
        var installationPath = ScoopInstance.GetInstallation(ScoopInstallScope.User)?.AppsPath
            ?? "Scoop apps directory";

        return new Result
        {
            Title = "Install",
            SubTitle = $"Install the selected app to {installationPath}",
            Icon = () => ScoopInstance.InstallIcon,
            AsyncAction = async _ =>
            {
                await ScoopPwshExecutor.InstallAsync(match, _context);
                return false;
            }
        };
    }

    private Result CreateGlobalInstallResult(Match match)
    {
        var installationPath = ScoopInstance.GetInstallation(ScoopInstallScope.Global)?.AppsPath
            ?? ScoopInstance.GetDefaultGlobalAppsPath();

        return new Result
        {
            Title = "Install (global)",
            SubTitle = $"Install the selected app to {installationPath}",
            Icon = () => ScoopInstance.InstallIcon,
            AsyncAction = async _ =>
            {
                await ScoopPwshExecutor.InstallGlobalAsync(match, _context);
                return false;
            }
        };
    }
}