using System;
using System.IO;
using Galdr.Native;
using Scum_Bag.DataAccess.Data;
using Shutter.Abstractions;

namespace Scum_Bag.Services;

internal sealed class SettingsService
{
    #region Fields

    private readonly IConfig _config;
    private readonly ILoggingService _loggingService;
    private readonly SaveService _saveService;
    private readonly EventService _eventService;
    private readonly FileService _fileService;
    private readonly GameService _gameService;
    private readonly IShutterService _shutterService;

    #endregion

    #region Constructor

    public SettingsService(IConfig config, ILoggingService loggingService, SaveService saveService, 
        EventService eventService, FileService fileService, GameService gameService,
        IShutterService shutterService)
    {
        _config = config;
        _loggingService = loggingService;
        _saveService = saveService;
        _eventService = eventService;
        _fileService = fileService;
        _gameService = gameService;
        _shutterService = shutterService;
    }

    #endregion

    #region Public Methods

    public Settings GetSettings()
    {
        Settings settings = null;

        try
        {
            settings = _config.GetSettings();
            settings.SupportsScreenCast = GetSupportsScreenCast();
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SettingsService)}>{nameof(GetSettings)} - {e}");
        }

        return settings;
    }

    public bool SaveSettings(Settings settings)
    {
        bool saved = false;

        if (!String.IsNullOrWhiteSpace(settings.BackupsDirectory) && Directory.Exists(settings.BackupsDirectory))
        {
            saved = true;
            bool notifyLocationChanged = false;

            try
            {
                if (_config.BackupsDirectory != settings.BackupsDirectory)
                {
                    saved = UpdateBackupsDirectory(_config.BackupsDirectory, settings.BackupsDirectory);
                    notifyLocationChanged = saved;
                }

                if (saved)
                {
                    _config.SaveSettings(settings);
                    _saveService.UpdateSavesBackupLocation();

                    if (_config.SteamExePath != settings.SteamExePath)
                    {
                        _gameService.InitializeSteamLibrary();
                    }

                    if (notifyLocationChanged)
                    {
                        _eventService.PublishEvent("backupLocationChanged", settings.BackupsDirectory);
                    }
                }
            }
            catch (Exception e)
            {
                _loggingService.LogError($"{nameof(SettingsService)}>{nameof(SaveSettings)} - {e}");
            }
        }

        return saved;
    }

    #endregion

    #region Private Methods
    
    private bool UpdateBackupsDirectory(string oldDirectory, string newDirectory)
    {
        bool backupsMoved = false;

        if (!String.IsNullOrEmpty(newDirectory))
        {
            if (!Directory.Exists(newDirectory))
            {
                Directory.CreateDirectory(newDirectory);
            }

            backupsMoved = true;
            DirectoryInfo oldDirectoryInfo = new DirectoryInfo(oldDirectory);
            DirectoryInfo[] oldDirectories = oldDirectoryInfo.GetDirectories();

            foreach (DirectoryInfo directory in oldDirectories)
            {
                string newDirectoryPath = Path.Combine(newDirectory, directory.Name);
                _fileService.CopyDirectory(directory.FullName, Path.Combine(newDirectory, directory.Name), true, true);

                if (VerifyContent(directory.FullName, newDirectoryPath))
                {
                    Directory.Delete(directory.FullName, true);
                    backupsMoved &= true;
                }
                else
                {
                    backupsMoved = false;
                }
            }
        }

        return backupsMoved;
    }

    private bool VerifyContent(string oldDirectory, string newDirectory)
    {
        return _fileService.GetHash(oldDirectory) == _fileService.GetHash(newDirectory);
    }

    private bool GetSupportsScreenCast()
    {
        return _shutterService.GetCapabilities().SupportsScreenCast;
    }

    #endregion
}
