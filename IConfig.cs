using Scum_Bag.DataAccess.Data;

namespace Scum_Bag;

internal interface IConfig
{
    string DataDirectory { get; }
    string SavesPath { get; }
    string LatestScreenshotName { get; }
    string BackupScreenshotName { get; }
    string BackupsDirectory { get; }
    string SettingsPath { get; }
    string SteamExePath { get; }
    Settings GetSettings();
    void SaveSettings(Settings settings);
}