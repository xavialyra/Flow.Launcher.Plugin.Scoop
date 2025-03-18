namespace Flow.Launcher.Plugin.Scoop.Entity;

public class ContextData
{
    public Match Match { set; get; }
    public HotKeyType HotKeyType { set; get; }

    public ContextData(Match match, HotKeyType hotKeyType)
    {
        Match = match;
        HotKeyType = hotKeyType;
    }

    public static ContextData OfList(Match match)
    {
        return new ContextData(match, HotKeyType.List);
    }

    public static ContextData OfSearch(Match match)
    {
        return new ContextData(match, HotKeyType.Search);
    }
}