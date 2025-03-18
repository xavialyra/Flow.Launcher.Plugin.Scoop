using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using Flow.Launcher.Plugin.Scoop.Entity;

namespace Flow.Launcher.Plugin.Scoop.Helper;

public class ScoopPwshExecutor
{

    public static void ExecuteCommand(string command)
    {

        var shellExecutable = "pwsh.exe";

        try
        {
            ExecuteCommandWithShell(shellExecutable, command);
        }
        catch (Exception ex) when (ex is Win32Exception || ex is Win32Exception ||
                                   ex.Message.Contains("not found") || ex.Message.Contains("not recognized"))
        {
            shellExecutable = "powershell.exe";
            try
            {
                ExecuteCommandWithShell(shellExecutable, command);
            }
            catch (Exception innerEx) when
                (innerEx is Win32Exception or Win32Exception)
            {
                if ((innerEx as Win32Exception)?.NativeErrorCode == 1223 ||
                    (innerEx as Win32Exception)?.NativeErrorCode == 1223)
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


    private static void ExecuteCommandWithShell(string shellExecutable, string command)
    {
        using var process = new Process();
        process.StartInfo.FileName = shellExecutable;
        process.StartInfo.Arguments = $"-NoProfile -ExecutionPolicy unrestricted -Command \"{command}\"";
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.UseShellExecute = true;
        process.StartInfo.RedirectStandardOutput = false;
        process.StartInfo.RedirectStandardError = false;
        process.StartInfo.Verb = "runas";

        process.Start();
        process.WaitForExit();

        try
        {
            process.Start();
            process.WaitForExit();
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new Exception("The operation was cancelled by the user.", ex);
        }

        if (process.ExitCode != 0)
        {
            throw new Exception($"{shellExecutable} script execution failed (Process). Exit code: {process.ExitCode}");
        }
    }

    public static void InstallAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg($"install {match.Name}");
            ExecuteCommand($"scoop install {match.Bucket}/{match.Name}");
            context.API.ShowMsg("Install finished");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Install failed", e.Message);
            throw;
        }
    }

    public static void UninstallAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg($"uninstall {match.Name}");
            ExecuteCommand($"scoop uninstall {match.Bucket}/{match.Name}");
            context.API.ShowMsg("Uninstall finished");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Uninstall failed", e.Message);
            throw;
        }
    }

    public static void UpdateAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg($"update {match.Name}");
            ExecuteCommand($"scoop update {match.Bucket}/{match.Name}");
            context.API.ShowMsg("Update finished");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Update failed", e.Message);
            throw;
        }
    }

    public static void ResetAsync(Match match, PluginInitContext context)
    {
        try
        {
            context.API.ShowMsg($"reset {match.Name}");
            ExecuteCommand($"scoop reset {match.Bucket}/{match.Name}");
            context.API.ShowMsg("Reset finished");
        }
        catch (Exception e)
        {
            context.API.ShowMsgError("Reset failed", e.Message);
            throw;
        }
    }
}