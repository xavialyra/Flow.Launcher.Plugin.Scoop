namespace Flow.Launcher.Plugin.Scoop.Entity;

public class Settings : BaseModel
{
    private string _scoopHome = "";
    private string _scoopGlobalHome = "";

    public string ScoopHome
    {
        get => _scoopHome;
        set
        {
            _scoopHome = value;
            OnPropertyChanged();
            ScoopInstance.LoadScoopHome(this);
        }
    }

    public string ScoopGlobalHome
    {
        get => _scoopGlobalHome;
        set
        {
            _scoopGlobalHome = value;
            OnPropertyChanged();
            ScoopInstance.LoadScoopHome(this);
        }
    }

}