namespace ScumBag.Tests.Mock;

using Scum_Bag;
using Scum_Bag.DataAccess.Data;

internal class MockConfig : IConfig
{
    public string DataDirectory => "";
    public string SavesPath => "";
    public string LatestScreenshotName => "";
    public string BackupScreenshotName => "Scum_Bag_Screenshot.jpg";
    public string BackupsDirectory => "";
    public string SettingsPath => "";
    public string SteamExePath => "";
    public Scum_Bag.DataAccess.Data.Settings GetSettings() => new() { BackupsDirectory = "", SteamExePath = "" };
    public void SaveSettings(Scum_Bag.DataAccess.Data.Settings settings) { }
}