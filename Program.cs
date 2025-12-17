using System;
using Galdr.Native;
using Scum_Bag.DataAccess.Data;
using Scum_Bag.Models;
using Scum_Bag.Services;
using SharpWebview.Content;
using Shutter;
using Shutter.Abstractions;

namespace Scum_Bag;

internal class Program
{
    [STAThread]
    static void Main()
    {
        Config config = new();
        using LoggingService loggingService = new(config);

        try
        {
            //if (!OperatingSystem.IsWindows())
            //{
            //    Galdr.Native.Generated.GaldrGeneratedInitializer.Initialize();
            //}

            GaldrBuilder builder = new GaldrBuilder()
                .SetTitle("Scum Bag - Save Manager")
                .SetSize(1100, 775)
                .SetMinSize(800, 600)
                .AddSingleton(config)
                .AddSingleton(loggingService)
                .AddSingleton<BackupService>()
                .AddSingleton<GameService>()
                .AddSingleton<SaveService>()
                .AddSingleton<ScreenshotService>()
                .AddSingleton<SettingsService>()
                .AddSingleton<FileService>()
                .AddSingleton<IShutterService, ShutterService>();
#if DEBUG
            //EmbeddedContent embeddedContent = new(embeddedNamespace: "Scum_Bag");
            //builder.SetContentProvider(embeddedContent);
            //builder.SetDebug(true);

            int port = 1314;
            UrlContent urlContent = new($"http://localhost:{port}");
            builder.SetContentProvider(urlContent);
            builder.SetDebug(true);
            builder.SetPort(port);
#else
            EmbeddedContent embeddedContent = new(embeddedNamespace: "Scum_Bag");
            builder.SetContentProvider(embeddedContent);
#endif

            AddSaveGameCommands(builder);
            AddGameCommands(builder);
            AddDialogCommands(builder);
            AddSettingsCommands(builder);

            using Galdr.Native.Galdr galdr = builder
                .Build()
                .Run();
        }
        catch (Exception e)
        {
            loggingService.LogError($"{nameof(Program)}>{nameof(Main)} - {e}");
        }
    }

    private static void AddSaveGameCommands(GaldrBuilder builder)
    {
        builder.AddFunction("getSaves", (SaveService saveService) =>
        {
            return saveService.GetSaves();
        });

        builder.AddFunction("getSave", (SaveService saveService, SaveGame saveGame) =>
        {
            return saveService.GetSave(saveGame.Id);
        });

        builder.AddFunction("getBackups", (SaveService saveService, SaveGame saveGame) =>
        {
            return saveService.GetBackups(saveGame.Id);
        });

        builder.AddFunction("getScreenshot", (SaveService saveService, string directory) =>
        {
            string data = saveService.GetScreenshot(directory);

            return new ScreenshotResult()
            {
                Data = data,
            };
        });

        builder.AddFunction("createSave", (SaveService saveService, SaveGame saveGame) =>
        {
            Guid id = saveService.CreateSave(saveGame);

            return new CreateSaveResult()
            {
                Id = id,
            };
        });

        builder.AddFunction("updateSave", (SaveService saveService, SaveGame saveGame) =>
        {
            bool success = saveService.UpdateSave(saveGame);

            return new CommandResult()
            {
                Success = success,
            };
        });

        builder.AddFunction("deleteSave", (SaveService saveService, SaveGame saveGame) =>
        {
            bool success = saveService.DeleteSave(saveGame.Id);

            return new CommandResult()
            {
                Success = success,
            };
        });

        builder.AddFunction("createManualBackup", (SaveService saveService, SaveGame saveGame) =>
        {
            bool success = saveService.CreateManualBackup(saveGame.Id);

            return new CommandResult()
            {
                Success = success,
            };
        });

        builder.AddFunction("restore", (SaveService saveService, Restore restore) =>
        {
            bool success = saveService.RestoreSave(restore.Id, restore.Time);

            return new CommandResult()
            {
                Success = success,
            };
        });

        builder.AddFunction("updateMetadata", (SaveService saveService, Backup backup) =>
        {
            bool success = saveService.UpdateMetadata(backup.SaveId, backup.Directory, backup.Tag, backup.IsFavorite);

            return new CommandResult()
            {
                Success = success,
            };
        });

        builder.AddFunction("deleteBackup", (SaveService saveService, Backup backup) =>
        {
            bool success = saveService.DeleteBackup(backup.SaveId, backup.Directory);

            return new CommandResult()
            {
                Success = success,
            };
        });
    }

    private static void AddGameCommands(GaldrBuilder builder)
    {
        builder.AddFunction("getGames", (GameService gameService) =>
        {
            return gameService.GetInstalledGames();
        });

        builder.AddFunction("launchGame", (GameService gameService, string gameName) =>
        {
            bool launched = gameService.LaunchGame(gameName);

            return new CommandResult()
            {
                Success = launched,
            };
        });
    }

    private static void AddDialogCommands(GaldrBuilder builder)
    {
        builder.AddFunction("openFileDialog", (DialogService dialogService, LoggingService logger) =>
        {
            string file = null;

            try
            {
                file = dialogService.OpenFileDialog();
            }
            catch (Exception e)
            {
                logger.LogError(e.ToString());
            }

            return new DialogResult()
            {
                File = file,
            };
        });

        builder.AddFunction("openDirectoryDialog", (DialogService dialogService, LoggingService logger) =>
        {
            string directory = null;

            try
            {
                directory = dialogService.OpenDirectoryDialog();
            }
            catch (Exception e)
            {
                logger.LogError(e.ToString());
            }

            return new DialogResult()
            {
                Directory = directory,
            };
        });
    }

    private static void AddSettingsCommands(GaldrBuilder builder)
    {
        builder.AddFunction("getSettings", (SettingsService settingsService) =>
        {
            return settingsService.GetSettings();
        });

        builder.AddFunction("saveSettings", (SettingsService settingsService, Settings settings) =>
        {
            bool saved = settingsService.SaveSettings(settings);

            return new SaveSettingsResult()
            {
                Saved = saved
            };
        });
    }
}
