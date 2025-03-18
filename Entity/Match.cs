using System.Text.Json.Nodes;
using System.Windows.Media;

namespace Flow.Launcher.Plugin.Scoop.Entity;

public class Match
{
    public string Name { get; set; }
    public string? Version { get; set; }
    public string? FileName { get; set; }
    public string Bucket { get; set; }
    public string? Path { get; set; }
    public JsonNode? Checkver { get; set; }
    public string? Homepage { get; set; }
    public ImageSource? Icon { get; set; }
    public string? Description { get; set; }
}