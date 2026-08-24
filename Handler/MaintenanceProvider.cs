using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using Flow.Launcher.Plugin.Scoop.Entity;
using Flow.Launcher.Plugin.Scoop.Helper;

namespace Flow.Launcher.Plugin.Scoop.Handler;

public class MaintenanceProvider : ProviderBase
{
    public MaintenanceProvider(PluginInitContext context) : base(context)
    {
    }

    protected override Task<List<Result>> GetResultAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        return GetUpdateResultsAsync(keyword.Trim(), cancellationToken);
    }

    private async Task<List<Result>> GetUpdateResultsAsync(
        string target,
        CancellationToken cancellationToken)
    {
        if (IsAllTarget(target))
        {
            var wildcardInstalledApps = ScoopInstance.GetInstalledApps(cancellationToken: cancellationToken)
                .Where(item => !string.Equals(item.Name, "scoop", StringComparison.OrdinalIgnoreCase))
                .ToList();
            return GetAllUpdateResults(wildcardInstalledApps, ScoopInstance.IsAdministrator());
        }

        var filter = target;
        var report = await ScoopStatusHelper.GetResultAsync(
            ScoopInstance.GetPrimaryRootPath(),
            cancellationToken);
        var installedApps = ScoopInstance.GetInstalledApps(cancellationToken: cancellationToken)
            .Where(item => !string.Equals(item.Name, "scoop", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var canUpdateGlobal = ScoopInstance.IsAdministrator();
        var updates = report.Apps
            .Where(HasAvailableUpdate)
            .Where(item => canUpdateGlobal || item.InstallScope != ScoopInstallScope.Global)
            .Where(item => string.IsNullOrWhiteSpace(filter)
                          || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        var hiddenGlobalUpdates = !canUpdateGlobal
                                  && report.Apps.Any(item =>
                                      HasAvailableUpdate(item)
                                      && (item.InstallScope == ScoopInstallScope.Global
                                          || item.InstallScope == ScoopInstallScope.Unknown)
                                      && (string.IsNullOrWhiteSpace(filter)
                                          || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)));

        var results = new List<Result>();
        if (string.IsNullOrWhiteSpace(target))
        {
            results.Add(CreateCommandResult(
                title: "Update Scoop and buckets",
                subTitle: "scoop update",
                icon: () => ScoopInstance.UpdateIcon,
                execute: ScoopPwshExecutor.UpdateScoopAsync));
            results.AddRange(GetAllUpdateResults(installedApps, canUpdateGlobal));
            if (hiddenGlobalUpdates)
            {
                results.Add(CreateMessageResult(
                    "Global application updates require administrator privileges"));
            }
        }

        results.AddRange(updates.SelectMany(item => CreateUpdateResults(
            item,
            filter,
            canUpdateGlobal)));
        if (hiddenGlobalUpdates && !string.IsNullOrWhiteSpace(target))
        {
            results.Add(CreateMessageResult(
                "Global application updates require administrator privileges"));
        }

        if (results.Count > 0)
        {
            if (updates.Count == 0
                && string.IsNullOrWhiteSpace(target)
                && !hiddenGlobalUpdates)
            {
                results.Add(CreateMessageResult("No application updates available"));
            }

            return results;
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
            var targetName = GetAppName(target);
            var installedScopes = GetInstalledScopes(targetName, installedApps);
            var blockedGlobal = !canUpdateGlobal
                                && installedScopes.Contains(ScoopInstallScope.Global);
            var scopes = canUpdateGlobal
                ? installedScopes
                : installedScopes.Where(scope => scope != ScoopInstallScope.Global).ToList();
            if (scopes.Count > 0)
            {
                var scopedResults = scopes.Select(scope => CreateCommandResult(
                        title: BuildScopedTitle($"Update {targetName}", scope),
                        subTitle: $"scoop update {targetName}{scope.PowerShellArgument()}",
                        icon: () => ScoopInstance.UpdateIcon,
                        execute: context => ScoopPwshExecutor.UpdateAsync(targetName, scope, context)))
                    .ToList();
                if (blockedGlobal && !hiddenGlobalUpdates)
                {
                    scopedResults.Add(CreateMessageResult(
                        "Global application updates require administrator privileges"));
                }

                return scopedResults;
            }

            if (blockedGlobal)
            {
                return new List<Result>
                {
                    CreateMessageResult(
                        "Global application updates require administrator privileges")
                };
            }

            return new List<Result>
            {
                CreateCommandResult(
                    title: $"Update {target}",
                    subTitle: $"scoop update {target}",
                    icon: () => ScoopInstance.UpdateIcon,
                    execute: context => ScoopPwshExecutor.UpdateAsync(target, context))
            };
        }

        return new List<Result>
        {
            CreateMessageResult("No application updates available")
        };
    }

    private List<Result> GetAllUpdateResults(
        IReadOnlyList<ScoopAppInstallation> installedApps,
        bool canUpdateGlobal)
    {
        var results = new List<Result>();
        if (installedApps.Any(item => item.Installation.Scope == ScoopInstallScope.User))
        {
            results.Add(CreateCommandResult(
                title: "Update all user applications",
                subTitle: "scoop update --all",
                icon: () => ScoopInstance.UpdateIcon,
                execute: ScoopPwshExecutor.UpdateAllAsync));
        }

        if (canUpdateGlobal
            && installedApps.Any(item => item.Installation.Scope == ScoopInstallScope.Global))
        {
            results.Add(CreateCommandResult(
                title: "Update all global applications",
                subTitle: "scoop update --all --global",
                icon: () => ScoopInstance.UpdateIcon,
                execute: ScoopPwshExecutor.UpdateAllGlobalAsync));
        }

        return results;
    }

    private IEnumerable<Result> CreateUpdateResults(
        ScoopStatusEntry status,
        string filter,
        bool canUpdateGlobal)
    {
        if (status.InstallScope == ScoopInstallScope.Global && !canUpdateGlobal)
        {
            return Array.Empty<Result>();
        }

        if (status.InstallScope == ScoopInstallScope.Unknown)
        {
            return canUpdateGlobal
                ? new[]
                {
                    CreateUpdateResult(status, filter, ScoopInstallScope.User),
                    CreateUpdateResult(status, filter, ScoopInstallScope.Global)
                }
                : new[] { CreateUpdateResult(status, filter, ScoopInstallScope.User) };
        }

        return new[] { CreateUpdateResult(status, filter, status.InstallScope) };
    }

    private Result CreateUpdateResult(
        ScoopStatusEntry status,
        string filter,
        ScoopInstallScope installScope)
    {
        return new Result
        {
            Title = BuildScopedTitle(status.Name, installScope),
            SubTitle = BuildSubtitle(status, installScope),
            Icon = () => ScoopInstance.UpdateIcon,
            Score = string.IsNullOrWhiteSpace(filter)
                ? 0
                : _context.API.FuzzySearch(filter, status.Name).Score,
            AsyncAction = async _ =>
            {
                await ScoopPwshExecutor.UpdateAsync(status.Name, installScope, _context);
                return false;
            }
        };
    }

    private static List<ScoopInstallScope> GetInstalledScopes(
        string appName,
        IReadOnlyList<ScoopAppInstallation> installedApps)
    {
        return installedApps
            .Where(item => string.Equals(item.Name, appName, StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Installation.Scope)
            .Distinct()
            .ToList();
    }

    private static string GetAppName(string target)
    {
        var separator = target.LastIndexOfAny(new[] { '/', '\\' });
        return separator >= 0 ? target[(separator + 1)..] : target;
    }

    private static string BuildScopedTitle(string title, ScoopInstallScope installScope)
    {
        var scopeLabel = installScope.DisplayLabel();
        return string.IsNullOrEmpty(scopeLabel) ? title : $"{title} ({scopeLabel})";
    }

    private Result CreateMessageResult(string message)
    {
        return new Result
        {
            Title = message,
            SubTitle = "Scoop update check",
            Icon = () => ScoopInstance.ScoopIcon,
            Action = _ => false
        };
    }

    private Result CreateCommandResult(
        string title,
        string subTitle,
        Func<ImageSource> icon,
        Func<PluginInitContext, Task> execute)
    {
        return new Result
        {
            Title = title,
            SubTitle = subTitle,
            Icon = () => icon(),
            AsyncAction = async _ =>
            {
                await execute(_context);
                return false;
            }
        };
    }

    private static bool HasAvailableUpdate(ScoopStatusEntry status)
    {
        return !string.IsNullOrWhiteSpace(status.LatestVersion);
    }

    private static string BuildSubtitle(
        ScoopStatusEntry status,
        ScoopInstallScope installScope)
    {
        var scopeLabel = installScope.DisplayLabel();
        var version = string.IsNullOrEmpty(scopeLabel)
            ? $"version: {status.InstalledVersion} -> {status.LatestVersion}"
            : $"{scopeLabel}, version: {status.InstalledVersion} -> {status.LatestVersion}";
        var details = new[]
            {
                status.Info,
                string.IsNullOrWhiteSpace(status.MissingDependencies)
                    ? string.Empty
                    : $"missing dependencies: {status.MissingDependencies}"
            }
            .Where(item => !string.IsNullOrWhiteSpace(item));

        var detailText = string.Join(", ", details);
        return string.IsNullOrWhiteSpace(detailText) ? version : $"{version}, {detailText}";
    }

    private static bool IsAllTarget(string target)
    {
        return target == "*";
    }
}
