using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public class ScoopPwshExecutor
{
    private sealed record CommandResult(string Output, string Error);

    public static async Task ExecuteCommandAsync(string command)
    {
        await ExecuteCommandWithOutputAsync(command);
    }

    public static Task<string> GetStatusJsonAsync()
    {
        const string command =
            "$records = @(scoop status 6>&1); " +
            "$apps = @($records | Where-Object { $_.PSObject.Properties.Name -contains 'Installed Version' }); " +
            "[pscustomobject]@{ Apps = $apps } | ConvertTo-Json -Depth 5 -Compress";

        return ExecuteCommandWithOutputAsync(command);
    }

    public static async Task<string> ExecuteCommandWithOutputAsync(string command)
    {
        const string PowerShellCore = "pwsh.exe";
        const string WindowsPowerShell = "powershell.exe";

        try
        {
            return (await ExecuteCommandWithShellAsync(PowerShellCore, command)).Output;
        }
        catch (Exception ex) when (IsShellUnavailable(ex))
        {
            try
            {
                return (await ExecuteCommandWithShellAsync(WindowsPowerShell, command)).Output;
            }
            catch (Exception innerEx) when (innerEx is Win32Exception)
            {
                if (innerEx is Win32Exception win32Exception && win32Exception.NativeErrorCode == 1223)
                {
                    throw new Exception("The operation was cancelled by the user.", innerEx);
                }

                if (innerEx.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || innerEx.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception(
                        $"{WindowsPowerShell} was also not found. Both pwsh.exe and powershell.exe failed to start.",
                        innerEx);
                }

                throw;
            }
        }
    }

    private static async Task<CommandResult> ExecuteCommandWithShellAsync(string shellExecutable, string command)
    {
        using var process = new Process();
        process.StartInfo.FileName = shellExecutable;
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("unrestricted");
        process.StartInfo.ArgumentList.Add("-Command");
        process.StartInfo.ArgumentList.Add(command);
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"{shellExecutable} script execution failed. Exit code: {process.ExitCode}. Error Output: {error.Trim()}");
        }

        return new CommandResult(output, error);
    }

    public static Task InstallAsync(Match match, PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: $"scoop install {QuotePowerShellArgument($"{match.Bucket}/{match.Name}")}",
            title: $"Install {match.Name}",
            subTitle: $"bucket {match.Bucket} version {match.Version}",
            successMessage: "Install finished",
            errorTitle: "Install failed",
            context);
    }

    public static Task UninstallAsync(Match match, PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: $"scoop uninstall {QuotePowerShellArgument($"{match.Bucket}/{match.Name}")}",
            title: $"Uninstall {match.Name}",
            subTitle: $"bucket {match.Bucket} version {match.Version}",
            successMessage: $"Uninstall finished: {match.Name}",
            errorTitle: "Uninstall failed",
            context);
    }

    public static Task UpdateAsync(Match match, PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: $"scoop update {QuotePowerShellArgument($"{match.Bucket}/{match.Name}")}",
            title: $"Update {match.Name}",
            subTitle: $"bucket: {match.Bucket}",
            successMessage: $"Update finished: {match.Name}",
            errorTitle: "Update failed",
            context);
    }

    public static Task UpdateAsync(string appName, PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: $"scoop update {QuotePowerShellArgument(appName)}",
            title: $"Update {appName}",
            subTitle: "Update the selected Scoop app",
            successMessage: $"Update finished: {appName}",
            errorTitle: "Update failed",
            context);
    }

    public static Task UpdateScoopAsync(PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: "scoop update",
            title: "Update Scoop and buckets",
            subTitle: "Synchronize Scoop and installed buckets",
            successMessage: "Scoop update finished",
            errorTitle: "Scoop update failed",
            context);
    }

    public static Task CleanupAsync(string appName, PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: $"scoop cleanup {QuotePowerShellArgument(appName)}",
            title: $"Cleanup {appName}",
            subTitle: "Remove old versions of the selected Scoop app",
            successMessage: $"Cleanup finished: {appName}",
            errorTitle: "Cleanup failed",
            context);
    }

    public static Task CleanupAllAsync(PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: "scoop cleanup --all",
            title: "Cleanup all installed apps",
            subTitle: "Remove old versions from every Scoop app",
            successMessage: "App cleanup finished",
            errorTitle: "Cleanup failed",
            context);
    }

    public static Task ResetAsync(Match match, PluginInitContext context)
    {
        return ExecuteOperationAsync(
            command: $"scoop reset {QuotePowerShellArgument($"{match.Bucket}/{match.Name}")}",
            title: $"Reset {match.Name}",
            subTitle: $"bucket: {match.Bucket} version {match.Version}",
            successMessage: $"Reset finished: {match.Name}",
            errorTitle: "Reset failed",
            context);
    }

    private static async Task ExecuteOperationAsync(
        string command,
        string title,
        string subTitle,
        string successMessage,
        string errorTitle,
        PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg(title, subTitle);
            await ExecuteCommandAsync(command);
            ScoopStatusHelper.InvalidateCache();
            context.API.ShowMsg(successMessage);
        }
        catch (Exception e)
        {
            context.API.ShowMsgError(errorTitle, e.Message);
            throw;
        }
    }

    private static string QuotePowerShellArgument(string argument)
    {
        return $"'{argument.Replace("'", "''")}'";
    }

    private static bool IsShellUnavailable(Exception exception)
    {
        return exception is Win32Exception
               || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
               || exception.Message.Contains("not recognized", StringComparison.OrdinalIgnoreCase);
    }
}
