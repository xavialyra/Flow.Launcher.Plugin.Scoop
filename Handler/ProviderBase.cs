using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Flow.Launcher.Plugin.Scoop.Handler;

public abstract class ProviderBase
{
    protected readonly PluginInitContext _context;

    protected ProviderBase(PluginInitContext context)
    {
        _context = context;
    }

    private static List<Result> ErrorResult => new()
    {
        new Result
        {
            Title = "Error",
            SubTitle = "Please check the log file for more details.",
            Icon = () => ScoopInstance.ScoopIcon,
            Action = _ => false
        }
    };

    private List<Result> ScoopDetectionFailedResult => new()
    {
        new Result
        {
            Title = "Scoop installation not detected",
            SubTitle = "You can specify the correct path in this plugin's settings.",
            Icon = () => ScoopInstance.ScoopIcon,
            Action = _ =>
            {
                // _context.API.OpenUrl("https://scoop.sh/");  scoopRootPath
                _context.API.OpenSettingDialog();
                return false;
            }
        }
    };

    protected abstract Task<List<Result>> GetResultAsync(string keyword);

    public Task<List<Result>> Handle(string keyword, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(ScoopInstance.ScoopHomePath))
        {
            return Task.FromResult(ScoopDetectionFailedResult);
        }

        return GetResultAsync(keyword)
            .ContinueWith(task =>
            {
                if (!task.IsFaulted) return task.Result;
                _context.API.LogException("Provider",
                    $"error", task.Exception);
                return ErrorResult;
            }, token);
    }
}