using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public class ScoopPwshExecutor
{
    public static async Task ExecuteCommandAsync(string command)
    {
        string shellExecutable = "pwsh.exe";
        try
        {
            await ExecuteCommandWithShellAsync(shellExecutable, command);
        }
        catch (Exception ex) when (ex is Win32Exception || ex.Message.Contains("not found") ||
                                   ex.Message.Contains("not recognized"))
        {
            shellExecutable = "powershell.exe";
            try
            {
                await ExecuteCommandWithShellAsync(shellExecutable, command);
            }
            catch (Exception innerEx) when (innerEx is Win32Exception)
            {
                if ((innerEx as Win32Exception)?.NativeErrorCode == 1223)
                {
                    throw new Exception("The operation was cancelled by the user.", innerEx);
                }

                if (innerEx.Message.Contains("not found") || innerEx.Message.Contains("not recognized"))
                {
                    throw new Exception(
                        $"{shellExecutable} was also not found.  Both pwsh.exe and powershell.exe failed to start.",
                        innerEx);
                }

                throw;
            }
        }
    }

    private static async Task ExecuteCommandWithShellAsync(string shellExecutable, string command)
    {
        using var process = new Process();
        process.StartInfo.FileName = shellExecutable;
        process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy unrestricted -Command \"{command}\"";
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.Verb = "runas";

        string output = null;
        string error = null;

        process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                output += e.Data + Environment.NewLine;
            }
        };

        process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                error += e.Data + Environment.NewLine;
            }
        };

        process.Start();

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await Task.Run(() => process.WaitForExit());

        if (process.ExitCode != 0)
        {
            throw new Exception(
                $"{shellExecutable} script execution failed (Process). Exit code: {process.ExitCode}.  Error Output: {error}");
        }
    }

    public static async Task InstallAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg(
                title: $"Install {match.Name}",
                subTitle: $"bucket {match.Bucket} version {match.Version}"
            );
            await ExecuteCommandAsync($"scoop install {match.Bucket}/{match.Name}");
            // context.API.HideMainWindow();
            context.API.ShowMsg("Install finished");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Install failed", e.Message);
            throw;
        }
    }

    public static async Task UninstallAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg(
                title: $"uninstall {match.Name}",
                subTitle: $"bucket {match.Bucket} version {match.Version}"
            );
            await ExecuteCommandAsync($"scoop uninstall {match.Bucket}/{match.Name}");
            context.API.ShowMsg($"Uninstall finished: {match.Name}");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Uninstall failed", e.Message);
            throw;
        }
    }

    public static async Task UpdateAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg(
                title: $"update {match.Name}",
                subTitle: $"bucket: {match.Bucket}"
            );
            await ExecuteCommandAsync($"scoop update {match.Bucket}/{match.Name}");
            context.API.ShowMsg($"Update finished: {match.Name}");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Update failed", e.Message);
            throw;
        }
    }

    public static async Task ResetAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg(
                title: $"reset {match.Name}",
                subTitle: $"bucket: {match.Bucket} version {match.Version}"
            );
            await ExecuteCommandAsync($"scoop reset {match.Bucket}/{match.Name}");
            context.API.ShowMsg($"Reset finished: {match.Name}");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Reset failed", e.Message);
            throw;
        }
    }
}