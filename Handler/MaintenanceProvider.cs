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
    private readonly HotKeyType _operation;

    public MaintenanceProvider(PluginInitContext context, HotKeyType operation) : base(context)
    {
        _operation = operation;
    }

    protected override async Task<List<Result>> GetResultAsync(
        string keyword,
        CancellationToken cancellationToken)
    {
        var target = keyword.Trim();
        return _operation switch
        {
            HotKeyType.Update => await GetUpdateResultsAsync(target, cancellationToken),
            HotKeyType.Cleanup => GetCleanupResults(target),
            _ => new List<Result>()
        };
    }

    private async Task<List<Result>> GetUpdateResultsAsync(
        string target,
        CancellationToken cancellationToken)
    {
        var showAllUpdates = IsAllTarget(target);
        var filter = showAllUpdates ? string.Empty : target;
        var report = await ScoopStatusHelper.GetResultAsync(
            ScoopInstance.ScoopHomePath!,
            cancellationToken);
        var updates = report.Apps
            .Where(HasAvailableUpdate)
            .Where(item => string.IsNullOrWhiteSpace(filter)
                          || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var results = new List<Result>();
        if (!showAllUpdates && string.IsNullOrWhiteSpace(target))
        {
            results.Add(CreateCommandResult(
                title: "Update Scoop and buckets",
                subTitle: "scoop update",
                icon: () => ScoopInstance.UpdateIcon,
                execute: ScoopPwshExecutor.UpdateScoopAsync));
        }

        results.AddRange(updates.Select(item => CreateUpdateResult(item, filter)));
        if (results.Count > 0)
        {
            if (updates.Count == 0 && string.IsNullOrWhiteSpace(target))
            {
                results.Add(CreateMessageResult("No application updates available"));
            }

            return results;
        }

        if (showAllUpdates)
        {
            return new List<Result>
            {
                CreateMessageResult("No application updates available")
            };
        }

        if (!string.IsNullOrWhiteSpace(target))
        {
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

    private List<Result> GetCleanupResults(string target)
    {
        if (string.IsNullOrWhiteSpace(target) || IsAllTarget(target))
        {
            return new List<Result>
            {
                CreateCommandResult(
                    title: "Cleanup old versions from all apps",
                    subTitle: "scoop cleanup --all",
                    icon: () => ScoopInstance.TrashIcon,
                    execute: ScoopPwshExecutor.CleanupAllAsync)
            };
        }

        return new List<Result>
        {
            CreateCommandResult(
                title: $"Cleanup old versions from {target}",
                subTitle: $"scoop cleanup {target}",
                icon: () => ScoopInstance.TrashIcon,
                execute: context => ScoopPwshExecutor.CleanupAsync(target, context))
        };
    }

    private Result CreateUpdateResult(ScoopStatusEntry status, string filter)
    {
        return new Result
        {
            Title = status.Name,
            SubTitle = BuildSubtitle(status),
            Icon = () => ScoopInstance.UpdateIcon,
            Score = string.IsNullOrWhiteSpace(filter)
                ? 0
                : _context.API.FuzzySearch(filter, status.Name).Score,
            AsyncAction = async _ =>
            {
                await ScoopPwshExecutor.UpdateAsync(status.Name, _context);
                return false;
            }
        };
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

    private static string BuildSubtitle(ScoopStatusEntry status)
    {
        var version = $"version: {status.InstalledVersion} -> {status.LatestVersion}";
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
        return target == "*"
               || target.Equals("--all", StringComparison.OrdinalIgnoreCase)
               || target.Equals("-a", StringComparison.OrdinalIgnoreCase);
    }
}
