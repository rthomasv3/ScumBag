using System;
using System.IO;
using System.Runtime.InteropServices;
using GaldrJson;
using Microsoft.Win32;
using Scum_Bag.DataAccess.Data;

namespace Scum_Bag;

internal sealed class Config : IConfig
{
    #region Fields

    private readonly IGaldrJsonSerializer _jsonSerializer;
    private readonly string _dataDir;
    private readonly string _savesPath;
    private readonly string _latestScreenshotName;
    private readonly string _backupScreenshotName;
    private readonly string _settingsPath;
    private string _backupsDirectory;
    private string _steamExePath;

    #endregion

    #region Constructor

    public Config(IGaldrJsonSerializer jsonSerializer)
    {
        _jsonSerializer = jsonSerializer;
        
        _dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Scum Bag");
        _backupsDirectory = _dataDir;
        _savesPath = Path.Combine(_dataDir, "saves.json");
        _backupScreenshotName = "Scum_Bag_Screenshot.jpg";
        _latestScreenshotName = "latest_screenshot.jpg";
        _settingsPath = Path.Combine(_dataDir, "settings.json");

        if (!Directory.Exists(_dataDir))
        {
            Directory.CreateDirectory(_dataDir);
        }

        Initialize();
    }

    #endregion

    #region Properties
    
    public string DataDirectory
    {
        get { return _dataDir; }
    }

    public string SavesPath
    {
        get { return _savesPath; }
    }

    public string LatestScreenshotName
    {
        get { return _latestScreenshotName; }
    }

    public string BackupScreenshotName
    {
        get { return _backupScreenshotName; }
    }

    public string BackupsDirectory
    {
        get { return _backupsDirectory; }
    }

    public string SettingsPath
    {
        get { return _settingsPath; }
    }

    public string SteamExePath
    {
        get { return _steamExePath; }
    }

    #endregion

    #region Public Methods

    public Settings GetSettings()
    {
        Settings settings = new()
        {
            Theme = "Indigo",
            IsDark = true,
            BackupsDirectory = _backupsDirectory,
            SteamExePath = _steamExePath,
        };

        if (File.Exists(_settingsPath))
        {
            settings = _jsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));
        }

        return settings;
    }

    public void SaveSettings(Settings settings)
    {
        _backupsDirectory = settings.BackupsDirectory;
        _steamExePath = settings.SteamExePath;
        string settingsFileContent = _jsonSerializer.Serialize(settings);
        File.WriteAllText(_settingsPath, settingsFileContent);
    }

    #endregion

    #region Private Methods

    private void Initialize()
    {
        if (File.Exists(_settingsPath))
        {
            Settings settings = _jsonSerializer.Deserialize<Settings>(File.ReadAllText(_settingsPath));
            _backupsDirectory = settings.BackupsDirectory;
            _steamExePath = settings.SteamExePath;
        }

        if (String.IsNullOrWhiteSpace(_steamExePath) || !File.Exists(_steamExePath))
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _steamExePath = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")?.GetValue("SteamExe")?.ToString();
            }
            else
            {
                _steamExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam/steam/steam.sh");

                if (!File.Exists(_steamExePath))
                {
                    _steamExePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "snap/steam/common/.local/share/Steam/steam.sh");
                }
            }
        }
    }

    #endregion
}