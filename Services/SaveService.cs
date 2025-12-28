using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GaldrJson;
using Scum_Bag.DataAccess.Data;
using Scum_Bag.Models;

namespace Scum_Bag.Services;

internal sealed class SaveService
{
    #region Fields

    private readonly Config _config;
    private readonly BackupService _backupService;
    private readonly ScreenshotService _screenshotService;
    private readonly LoggingService _loggingService;
    private readonly FileService _fileService;
    private readonly IGaldrJsonSerializer _jsonSerializer;

    #endregion

    #region Constructor

    public SaveService(Config config, BackupService backupService, ScreenshotService screenshotService,
        LoggingService loggingService, FileService fileService, IGaldrJsonSerializer jsonSerializer)
    {
        _config = config;
        _backupService = backupService;
        _screenshotService = screenshotService;
        _loggingService = loggingService;
        _fileService = fileService;
        _jsonSerializer = jsonSerializer;
    }

    #endregion

    #region Public Methods

    public SaveGameNodes GetSaves()
    {
        List<TreeNode> saves = new();

        try
        {
            if (File.Exists(_config.SavesPath))
            {
                List<SaveGame> saveGames = DeserializeSaveGames();
                IEnumerable<IGrouping<string, SaveGame>> saveGroups = saveGames.GroupBy(x => String.IsNullOrEmpty(x.Game) ? "Game" : x.Game);

                foreach(IGrouping<string, SaveGame> group in saveGroups.OrderBy(x => x.Key))
                {
                    List<TreeNode> children = new();

                    foreach(SaveGame saveGame in group.OrderBy(x => x.Name))
                    {
                        children.Add(new TreeNode()
                        {
                            key = saveGame.Id.ToString(),
                            label = saveGame.Name,
                        });
                    }

                    saves.Add(new TreeNode()
                    {
                        key = group.Key,
                        label = group.Key,
                        type = "game",
                        children = children
                    });
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(GetSaves)} - {e}");
        }

        return new SaveGameNodes()
        {
            Nodes = saves.AsReadOnly()
        };
    }

    public SaveGame GetSave(Guid id)
    {
        SaveGame saveGame = null;

        try
        {
            List<SaveGame> saveGames = DeserializeSaveGames();

            foreach (SaveGame save in saveGames)
            {
                if (save.Id == id)
                {
                    saveGame = save;

                    if (String.IsNullOrWhiteSpace(save.BackupLocation))
                    {
                        save.BackupLocation = Path.Combine(_config.BackupsDirectory, save.Id.ToString());
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(GetSave)} - {e}");
        }

        return saveGame;
    }

    public BackupsList GetBackups(Guid id)
    {
        List<Backup> backups = new();

        try
        {
            SaveGame saveGame = GetSave(id);

            if (saveGame != null)
            {
                string path = Path.Combine(_config.BackupsDirectory, id.ToString());

                DirectoryInfo parent = new(path);

                if (parent.Exists)
                {
                    DirectoryInfo[] dirs = parent.GetDirectories();

                    foreach (DirectoryInfo dir in dirs)
                    {
                        string tag = null;
                        bool isFavorite = false;

                        if (saveGame.BackupMetadata?.TryGetValue(dir.FullName, out BackupMetadata metadata) == true)
                        {
                            tag = metadata.Tag;
                            isFavorite = metadata.IsFavorite;
                        }

                        if (Int64.TryParse(dir.Name, out long time))
                        {
                            backups.Add(new Backup()
                            {
                                SaveId = saveGame.Id,
                                Time = ((DateTimeOffset)new DateTime(time)).ToUnixTimeMilliseconds(),
                                Directory = dir.FullName,
                                Tag = tag,
                                IsFavorite = isFavorite
                            });
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(GetBackups)} - {e}");
        }

        return new BackupsList()
        {
            Backups = backups.AsReadOnly()
        };
    }

    public string GetScreenshot(string directory)
    {
        string screenshot = String.Empty;

        try
        {
            if (!String.IsNullOrEmpty(directory))
            {
                string screenshotPath = Path.Combine(directory, _config.BackupScreenshotName);

                if (File.Exists(screenshotPath))
                {
                    screenshot = Convert.ToBase64String(File.ReadAllBytes(screenshotPath));
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(GetScreenshot)} - {e}");
        }

        return screenshot;
    }

    public Guid CreateSave(SaveGame saveGame)
    {
        try
        {
            if (!File.Exists(_config.SavesPath))
            {
                File.WriteAllText(_config.SavesPath, "[]");
            }
            
            saveGame.Id = Guid.NewGuid();
            saveGame.BackupLocation = Path.Combine(_config.BackupsDirectory, saveGame.Id.ToString());

            List<SaveGame> saveGames = DeserializeSaveGames();

            if (saveGame.Enabled)
            {
                DisableDuplicates(saveGame.Id, saveGame.SaveLocation, ref saveGames);
            }

            saveGames.Add(saveGame);
            string fileContent = SerializeSaveGames(saveGames);
            File.WriteAllText(_config.SavesPath, fileContent);

            _backupService.AddNewBackupTimer(saveGame);

            _screenshotService.StartWatching(saveGame.Id, saveGame.SaveLocation, saveGame.Game);
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(CreateSave)} - {e}");
        }

        return saveGame.Id;
    }

    public bool UpdateSave(SaveGame saveGame)
    {
        bool updated = false;

        try
        {
            List<SaveGame> saveGames = DeserializeSaveGames();

            if (saveGame.Enabled)
            {
                DisableDuplicates(saveGame.Id, saveGame.SaveLocation, ref saveGames);
            }

            for (int i = 0; i < saveGames.Count; ++i)
            {
                if (saveGames[i].Id == saveGame.Id)
                {
                    saveGames[i].Enabled = saveGame.Enabled;
                    saveGames[i].Name = saveGame.Name;
                    saveGames[i].SaveLocation = saveGame.SaveLocation;
                    saveGames[i].Game = saveGame.Game;
                    saveGames[i].Frequency = saveGame.Frequency;
                    saveGames[i].MaxBackups = saveGame.MaxBackups;
                    updated = true;
                    break;
                }
            }

            string fileContent = SerializeSaveGames(saveGames);
            File.WriteAllText(_config.SavesPath, fileContent);

            _backupService.UpdateSave(saveGame);

            _screenshotService.StartWatching(saveGame.Id, saveGame.SaveLocation, saveGame.Game);
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(UpdateSave)} - {e}");
        }

        return updated;
    }

    public bool DeleteSave(Guid id)
    {
        bool deleted = false;

        try
        {
            List<SaveGame> saveGames = DeserializeSaveGames();

            for (int i = 0; i < saveGames.Count; ++i)
            {
                if (saveGames[i].Id == id)
                {
                    saveGames.RemoveAt(i);
                    deleted = true;
                    break;
                }
            }

            string fileContent = SerializeSaveGames(saveGames);
            File.WriteAllText(_config.SavesPath, fileContent);

            _backupService.StopTimer(id);

            string path = Path.Combine(_config.BackupsDirectory, id.ToString());
            DirectoryInfo parent = new(path);

            if (parent.Exists)
            {
                parent.Delete(true);
            }

            _screenshotService.StopWatching(id);
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(DeleteSave)} - {e}");
        }

        return deleted;
    }

    public bool CreateManualBackup(Guid id)
    {
        return _backupService.CreateManualBackup(id);
    }

    public bool RestoreSave(Guid id, long time)
    {
        bool restored = false;

        try
        {
            SaveGame saveGame = GetSave(id);

            string path = Path.Combine(_config.BackupsDirectory, id.ToString());

            DirectoryInfo parent = new(path);

            if (parent.Exists)
            {
                DirectoryInfo[] dirs = parent.GetDirectories();

                foreach (DirectoryInfo dir in dirs)
                {
                    if (Int64.TryParse(dir.Name, out long dirTime))
                    {
                        if (((DateTimeOffset)new DateTime(dirTime)).ToUnixTimeMilliseconds() == time)
                        {
                            restored = RestoreFile(dir.FullName, saveGame.SaveLocation);

                            if (restored)
                            {
                                string screenShotPath = Path.Combine(dir.FullName, _config.BackupScreenshotName);
                                if (File.Exists(screenShotPath))
                                {
                                    string latestScreenshotPath = Path.Combine(saveGame.BackupLocation, _config.LatestScreenshotName);
                                    File.Copy(screenShotPath, latestScreenshotPath, true);
                                }
                            }

                            break;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(RestoreSave)} - {e}");
        }

        return restored;
    }

    public bool UpdateMetadata(Guid saveGameId, string directory, string tag, bool isFavorite)
    {
        bool updated = false;

        try
        {
            if (Directory.Exists(directory))
            {
                List<SaveGame> saveGames = DeserializeSaveGames();
                
                foreach (SaveGame saveGame in saveGames)
                {
                    if (saveGame.BackupMetadata == null)
                    {
                        saveGame.BackupMetadata = new Dictionary<string, BackupMetadata>();
                    }

                    if (saveGameId == saveGame.Id)
                    {
                        saveGame.BackupMetadata[directory] = new BackupMetadata()
                        {
                            Tag = tag,
                            IsFavorite = isFavorite
                        };
                        updated = true;
                        break;
                    }
                }

                if (updated)
                {
                    string fileContent = SerializeSaveGames(saveGames);
                    File.WriteAllText(_config.SavesPath, fileContent);
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(UpdateMetadata)} - {e}");
        }

        return updated;
    }

    public bool DeleteBackup(Guid saveGameId, string directory)
    {
        bool deleted = false;

        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
                DeleteMetadata(saveGameId, directory);
                deleted = true;
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(DeleteBackup)} - {e}");
        }

        return deleted;
    }

    public bool DeleteMetadata(Guid saveGameId, string directory)
    {
        bool deleted = false;

        try
        {
            List<SaveGame> saveGames = DeserializeSaveGames();
            
            foreach (SaveGame saveGame in saveGames)
            {
                if (saveGame.BackupMetadata == null)
                {
                    saveGame.BackupMetadata = new Dictionary<string, BackupMetadata>();
                }

                if (saveGameId == saveGame.Id)
                {
                    saveGame.BackupMetadata.Remove(directory);
                    deleted = true;
                    break;
                }
            }

            if (deleted)
            {
                string fileContent = SerializeSaveGames(saveGames);
                File.WriteAllText(_config.SavesPath, fileContent);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(DeleteMetadata)} - {e}");
        }

        return deleted;
    }

    public void UpdateSavesBackupLocation()
    {
        if (File.Exists(_config.SavesPath))
        {
            List<SaveGame> saveGames = DeserializeSaveGames();

            foreach (SaveGame saveGame in saveGames)
            {
                if (saveGame.BackupMetadata == null)
                {
                    saveGame.BackupMetadata = new Dictionary<string, BackupMetadata>();
                }

                string newLocation = Path.Combine(_config.BackupsDirectory, saveGame.Id.ToString());

                if (saveGame.BackupMetadata?.Count > 0)
                {
                    Dictionary<string, BackupMetadata> updatedMetadata = new();

                    foreach (KeyValuePair<string, BackupMetadata> metadata in saveGame.BackupMetadata)
                    {
                        string key = metadata.Key.Replace(saveGame.BackupLocation, newLocation);
                        updatedMetadata[key] = metadata.Value;
                    }

                    saveGame.BackupMetadata = updatedMetadata;
                }

                saveGame.BackupLocation = newLocation;
            }

            string fileContent = SerializeSaveGames(saveGames);
            File.WriteAllText(_config.SavesPath, fileContent);
        }
    }

    #endregion

    #region Private Methods

    private bool RestoreFile(string source, string destination)
    {
        bool restored = false;

        if (Directory.Exists(source))
        {
            if (Directory.Exists(destination))
            {
                _fileService.CopyDirectory(source, destination, true, true);
                restored = true;
            }
            else
            {
                string fileName = Path.GetFileName(destination);

                string[] files = Directory.GetFiles(source, "*.*", SearchOption.AllDirectories)
                    .Where(x => x.EndsWith(fileName))
                    .ToArray();

                if (files.Length == 1)
                {
                    File.Copy(files[0], destination, true);
                    restored = true;
                }
            }
        }

        return restored;
    }

    private void DisableDuplicates(Guid? currentId, string location, ref List<SaveGame> saveGames)
    {
        try
        {
            foreach (SaveGame saveGame in saveGames)
            {
                if (saveGame.Id != currentId.GetValueOrDefault() && saveGame.SaveLocation == location)
                {
                    saveGame.Enabled = false;
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(SaveService)}>{nameof(DisableDuplicates)} - {e}");
        }
    }

    private List<SaveGame> DeserializeSaveGames()
    {
        return _jsonSerializer.Deserialize<List<SaveGame>>(File.ReadAllText(_config.SavesPath));
    }

    private string SerializeSaveGames(List<SaveGame> saveGames)
    {
        return _jsonSerializer.Serialize(saveGames);
    }

    #endregion
}