namespace Flow.Launcher.Plugin.Scoop.Entity;

public sealed class ScoopStatusEntry
{
    public string Name { get; init; } = string.Empty;
    public string InstalledVersion { get; init; } = string.Empty;
    public string LatestVersion { get; init; } = string.Empty;
    public string MissingDependencies { get; init; } = string.Empty;
    public string Info { get; init; } = string.Empty;
    public ScoopInstallScope InstallScope { get; set; } = ScoopInstallScope.Unknown;
}
