using GaldrJson;

namespace Scum_Bag.DataAccess.Data;

[GaldrJsonSerializable]
internal sealed class Settings
{
    public string Theme { get; set; }
    public bool IsDark { get; set; }
    public string BackupsDirectory { get; set; }
    public string SteamExePath { get; set; }
    public bool SupportsScreenCast { get; set; }
    public ScreenCapturePortal ScreenCapturePortal { get; set; }
}