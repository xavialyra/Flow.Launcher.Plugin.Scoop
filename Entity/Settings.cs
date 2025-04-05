namespace Flow.Launcher.Plugin.Scoop.Entity;

public class Settings : BaseModel
{
    private string _scoopHome = "";
    
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
    
}