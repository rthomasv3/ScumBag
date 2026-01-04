using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;
using Galdr.Native;
using GaldrJson;
using Scum_Bag.DataAccess.Data;

namespace Scum_Bag.Services;

internal sealed class BackupService
{
    #region Fields

    private readonly IConfig _config;
    private readonly EventService _eventService;
    private readonly ILoggingService _loggingService;
    private readonly FileService _fileService;
    private readonly ScreenshotService _screenshotService;
    private readonly Dictionary<Guid, Timer> _backupTimers = new();
    private readonly Dictionary<Guid, ElapsedEventHandler> _backupTimerEvents = new();
    private readonly IGaldrJsonSerializer _jsonSerializer;

    #endregion

    #region Constructor

    public BackupService(IConfig config, EventService eventService, ILoggingService loggingService, 
        FileService fileService, ScreenshotService screenshotService, 
        IGaldrJsonSerializer jsonSerializer)
    {
        _config = config;
        _eventService = eventService;
        _loggingService = loggingService;
        _fileService = fileService;
        _screenshotService = screenshotService;
        _jsonSerializer = jsonSerializer;

        Initialize();
    }

    #endregion

    #region Public Methods

    public void AddNewBackupTimer(SaveGame saveGame)
    {
        CreateInitialBackup(saveGame);
        AddTimer(saveGame);
        DisableDuplicates(saveGame.Id, saveGame.SaveLocation);
    }

    public void UpdateSave(SaveGame saveGame)
    {
        RemoveTimer(saveGame.Id);

        if (saveGame.Enabled)
        {
            AddTimer(saveGame);
        }

        DisableDuplicates(saveGame.Id, saveGame.SaveLocation);
    }

    public void StopTimer(Guid id)
    {
        RemoveTimer(id);
    }

    public bool CreateManualBackup(Guid id)
    {
        return CreateBackup(id);
    }

    #endregion

    #region Private Methods

    private void Initialize()
    {
        if (File.Exists(_config.SavesPath))
        {
            List<SaveGame> saveGames = GetSaveGames();

            foreach(SaveGame saveGame in saveGames)
            {
                if (saveGame.Enabled)
                {
                    AddTimer(saveGame);
                }
            }
        }
    }

    private void AddTimer(SaveGame saveGame)
    {
        Timer timer = new Timer(TimeSpan.FromMinutes(saveGame.Frequency));
        ElapsedEventHandler eventHandler = (s, e) => HandleTimer(saveGame.Id);
        lock (_backupTimerEvents)
        {
            _backupTimerEvents[saveGame.Id] = eventHandler;
        }
        timer.Elapsed += eventHandler;
        timer.AutoReset = true;
        timer.Enabled = true;

        lock (_backupTimers)
        {
            _backupTimers.Add(saveGame.Id, timer);
        }
    }

    private void RemoveTimer(Guid id)
    {
        lock(_backupTimers)
        {
            if (_backupTimers.ContainsKey(id))
            {
                lock (_backupTimerEvents)
                {
                    if (_backupTimerEvents.ContainsKey(id))
                    {
                        _backupTimers[id].Elapsed -= _backupTimerEvents[id];
                        _backupTimerEvents.Remove(id);
                    }
                }

                _backupTimers[id].Enabled = false;
                _backupTimers[id].Dispose();
                _backupTimers.Remove(id);
            }
        }
    }

    private void DisableDuplicates(Guid id, string location)
    {
        List<SaveGame> saveGames = GetSaveGames();

        foreach (SaveGame saveGame in saveGames)
        {
            if (saveGame.Id != id && saveGame.SaveLocation == location)
            {
                RemoveTimer(saveGame.Id);
            }
        }
    }

    private void CreateInitialBackup(SaveGame saveGame)
    {
        try
        {
            string parentDirectory = Path.Combine(_config.BackupsDirectory, saveGame.Id.ToString());

            if (!Directory.Exists(parentDirectory))
            {
                Directory.CreateDirectory(parentDirectory);
            }

            string backupPath = Path.Combine(parentDirectory, DateTime.Now.Ticks.ToString());
            
            if (Directory.Exists(saveGame.SaveLocation))
            {
                _fileService.CopyDirectory(saveGame.SaveLocation, backupPath);
            }
            else
            {
                Directory.CreateDirectory(backupPath);
                FileInfo fileInfo = new(saveGame.SaveLocation);
                string filePath = Path.Combine(backupPath, fileInfo.Name);
                File.Copy(saveGame.SaveLocation, filePath);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(BackupService)}>{nameof(CreateInitialBackup)} - {e}");
        }
    }

    private void HandleTimer(Guid id)
    {
        lock (_backupTimerEvents[id])
        {
            CreateBackup(id);
        }
    }

    private bool CreateBackup(Guid id)
    {
        bool backedUp = false;

        try
        {
            SaveGame saveGame = GetSaveGame(id);

            if (saveGame != null)
            {
                string parentDirectory = Path.Combine(_config.BackupsDirectory, saveGame.Id.ToString());

                if (!Directory.Exists(parentDirectory))
                {
                    Directory.CreateDirectory(parentDirectory);
                }
                
                DirectoryInfo parentDir = new(parentDirectory);
                DirectoryInfo newestDir = null;
                long newestDirTime = 0;
                DirectoryInfo[] dirs = parentDir.GetDirectories();

                // find the latest backup
                foreach (DirectoryInfo dir in dirs)
                {
                    if (Int64.TryParse(dir.Name, out long dirTime))
                    {
                        if (newestDir == null || dirTime > newestDirTime)
                        {
                            newestDir = dir;
                            newestDirTime = dirTime;
                        }
                    }
                }

                string lastBackupPath = newestDir.FullName;
                string lastBackupLocation = lastBackupPath;

                if (File.Exists(saveGame.SaveLocation))
                {
                    FileInfo fileInfo = new(saveGame.SaveLocation);
                    lastBackupLocation = Path.Combine(lastBackupPath, fileInfo.Name);
                }

                if (Directory.Exists(lastBackupPath))
                {
                    if (_fileService.HasChanges(lastBackupLocation, saveGame.SaveLocation))
                    {
                        string backupPath = Path.Combine(parentDirectory, DateTime.Now.Ticks.ToString());

                        if (Directory.Exists(saveGame.SaveLocation))
                        {
                            _fileService.CopyDirectory(saveGame.SaveLocation, backupPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(backupPath);
                            FileInfo fileInfo = new(saveGame.SaveLocation);
                            string filePath = Path.Combine(backupPath, fileInfo.Name);
                            File.Copy(saveGame.SaveLocation, filePath);
                        }

                        if (_screenshotService.TryGetLatestScreenshot(saveGame.SaveLocation, out byte[] screenshotData))
                        {
                            string newScreenshotPath = Path.Combine(backupPath, _config.BackupScreenshotName);
                            File.WriteAllBytes(newScreenshotPath, screenshotData);
                        }

                        // actual count is dirs length + 1 because a new backup was just made,
                        // and don't count any favorites toward the max backups
                        int totalBackups = dirs.Length + 1 - saveGame.BackupMetadata?.Where(x => x.Value.IsFavorite)?.Count() ?? 0;

                        while (totalBackups > saveGame.MaxBackups)
                        {
                            DeleteOldestBackup(saveGame);
                            totalBackups--;
                        }

                        backedUp = true;
                        _eventService.PublishEvent("saveUpdated", $"{{ id: \"{id}\" }}");
                    }
                }
            }
        }
        catch(Exception e)
        {
            _loggingService.LogError($"{nameof(BackupService)}>{nameof(CreateBackup)} - {e}");
        }

        return backedUp;
    }

    private void DeleteOldestBackup(SaveGame saveGame)
    {
        try
        {
            long oldestDirTime = 0;
            DirectoryInfo oldestDir = null;
            DirectoryInfo parentDirectory = new(Path.Combine(_config.BackupsDirectory, saveGame.Id.ToString()));

            foreach (DirectoryInfo dir in parentDirectory.GetDirectories())
            {
                bool isFavorite = false;

                if (saveGame.BackupMetadata?.TryGetValue(dir.FullName, out BackupMetadata backupMetadata) == true)
                {
                    isFavorite = backupMetadata.IsFavorite;
                }

                if (!isFavorite)
                {
                    if (Int64.TryParse(dir.Name, out long dirTime))
                    {
                        if (oldestDir == null || dirTime < oldestDirTime)
                        {
                            oldestDir = dir;
                            oldestDirTime = dirTime;
                        }
                    }
                }
            }

            oldestDir?.Delete(true);
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(BackupService)}>{nameof(DeleteOldestBackup)} - {e}");
        }
    }

    private List<SaveGame> GetSaveGames()
    {
        List<SaveGame> saveGames = new();

        try
        {
            saveGames = _jsonSerializer.Deserialize<List<SaveGame>>(File.ReadAllText(_config.SavesPath));
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(BackupService)}>{nameof(GetSaveGames)} - {e}");
        }

        return saveGames;
    }

    private SaveGame GetSaveGame(Guid id)
    {
        SaveGame save = null;

        try
        {
            List<SaveGame> saveGames = GetSaveGames();

            foreach (SaveGame saveGame in saveGames)
            {
                if (saveGame.Id == id)
                {
                    save = saveGame;
                    break;
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(BackupService)}>{nameof(GetSaveGame)} - {e}");
        }

        return save;
    }

    #endregion
}