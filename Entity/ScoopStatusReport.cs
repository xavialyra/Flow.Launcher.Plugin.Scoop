using System.Collections.Generic;

namespace Flow.Launcher.Plugin.Scoop.Entity;

public sealed class ScoopStatusReport
{
    public List<ScoopStatusEntry> Apps { get; init; } = new();
}
