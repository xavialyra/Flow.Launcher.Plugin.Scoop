using System.IO;

namespace Flow.Launcher.Plugin.Scoop.Entity;

public enum ScoopInstallScope
{
    Unknown,
    User,
    Global
}

public static class ScoopInstallScopeExtensions
{
    public static string DisplayLabel(this ScoopInstallScope scope) =>
        scope == ScoopInstallScope.Global ? "global" : string.Empty;

    public static string PowerShellArgument(this ScoopInstallScope scope) =>
        scope == ScoopInstallScope.Global ? " --global" : string.Empty;
}

public sealed record ScoopInstallation(string RootPath, ScoopInstallScope Scope)
{
    public string AppsPath => Path.Combine(RootPath, "apps");
}

public sealed record ScoopAppInstallation(string Name, string DirectoryPath, ScoopInstallation Installation);
