using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using GaldrJson;
using Scum_Bag.DataAccess.Data;
using Scum_Bag.DataAccess.Data.Steam;
using Scum_Bag.Models;
using Shutter.Abstractions;
using Shutter.Models;

namespace Scum_Bag.Services;

internal sealed class ScreenshotService
{
    #region Fields

    private readonly Config _config;
    private readonly GameService _gameService;
    private readonly LoggingService _loggingService;
    private readonly IShutterService _shutterService;
    private readonly IGaldrJsonSerializer _jsonSerializer;
    private readonly Dictionary<string, WatchLocation> _watchers;
    private bool _flameshotSetup;
    private readonly bool _isInFlatpak;
    private readonly string _flameshotCommand;
    private readonly string _flameshotArgs;

    #endregion

    #region Constructor

    public ScreenshotService(Config config, GameService gameService, LoggingService loggingService,
        IShutterService shutterService, IGaldrJsonSerializer jsonSerializer)
    {
        _config = config;
        _gameService = gameService;
        _loggingService = loggingService;
        _shutterService = shutterService;
        _jsonSerializer = jsonSerializer;
        _watchers = new();

        _isInFlatpak = File.Exists("/.flatpak-info");

        if (_isInFlatpak)
        {
            _flameshotCommand = "flatpak-spawn";
            _flameshotArgs = "--host flameshot";
        }
        else
        {
            _flameshotCommand = "flameshot";
            _flameshotArgs = "";
        }

        Initialize();
    }

    #endregion

    #region Public Methods

    public void StartWatching(Guid saveGameId, string location, string game)
    {
        try
        {
            if (!String.IsNullOrWhiteSpace(location) && !String.IsNullOrWhiteSpace(game))
            {
                RemoveExistingWatcher(saveGameId);

                IEnumerable<AppState> games = _gameService.GetInstalledApps();
                AppState app = games.FirstOrDefault(x => x.Name == game);

                if (app != null)
                {
                    string directory = Path.GetDirectoryName(location);

                    FileSystemWatcher watcher = new(directory)
                    {
                        NotifyFilter = NotifyFilters.LastWrite,
                        IncludeSubdirectories = true
                    };
                    watcher.Changed += OnChanged;
                    watcher.EnableRaisingEvents = true;

                    lock (_watchers)
                    {
                        _watchers.Add(directory, new WatchLocation()
                        {
                            Watcher = watcher,
                            SaveGameId = saveGameId,
                            Location = location,
                            GameDirectory = app.FullInstallDir
                        });
                    }
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(StartWatching)} - {e}");
        }
    }

    public void StopWatching(Guid saveGameId)
    {
        RemoveExistingWatcher(saveGameId);
    }

    public bool TryGetLatestScreenshot(string saveLocation, out byte[] screenshotData)
    {
        screenshotData = null;

        if (_watchers.TryGetValue(Path.GetDirectoryName(saveLocation), out WatchLocation watchLocation))
        {
            if (Monitor.TryEnter(watchLocation, TimeSpan.FromSeconds(5)))
            {
                try
                {
                    string saveDirectory = Path.Combine(_config.BackupsDirectory, watchLocation.SaveGameId.ToString());
                    string savePath = Path.Combine(saveDirectory, _config.LatestScreenshotName);

                    if (File.Exists(savePath))
                    {
                        screenshotData = File.ReadAllBytes(savePath);
                    }
                }
                finally
                {
                    Monitor.Exit(watchLocation);
                }
            }
        }

        return screenshotData != null;
    }

    #endregion

    #region Private Methods

    private void Initialize()
    {
        try
        {
            if (File.Exists(_config.SavesPath))
            {
                List<SaveGame> saveGames = _jsonSerializer.Deserialize<List<SaveGame>>(File.ReadAllText(_config.SavesPath));

                foreach(SaveGame saveGame in saveGames)
                {
                    if (saveGame.Enabled && !String.IsNullOrWhiteSpace(saveGame.Game))
                    {
                        StartWatching(saveGame.Id, saveGame.SaveLocation, saveGame.Game);
                    }
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(Initialize)} - {e}");
        }
    }

    private void RemoveExistingWatcher(Guid saveGameId)
    {
        try
        {
            lock (_watchers)
            {
                KeyValuePair<string, WatchLocation> watcher = _watchers.FirstOrDefault(x => x.Value.SaveGameId == saveGameId);

                if (watcher.Value != null)
                {
                    watcher.Value.Watcher.EnableRaisingEvents = false;
                    watcher.Value.Watcher.Changed -= OnChanged;
                    watcher.Value.Watcher.Dispose();
                    _watchers.Remove(watcher.Key);
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(RemoveExistingWatcher)} - {e}");
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs args)
    {
        try
        {
            if (!String.IsNullOrEmpty(args.FullPath) && !String.IsNullOrWhiteSpace(args.Name))
            {
                string directory = args.FullPath.Replace(args.Name, "").TrimEnd('/', '\\');

                if (_watchers.TryGetValue(directory, out WatchLocation watchLocation))
                {
                    if (Monitor.TryEnter(watchLocation, TimeSpan.FromSeconds(5)))
                    {
                        try
                        {
                            string saveDirectory = Path.Combine(_config.BackupsDirectory, watchLocation.SaveGameId.ToString());
                            TakeScreenshot(saveDirectory, watchLocation.GameDirectory);
                        }
                        finally
                        {
                            Monitor.Exit(watchLocation);
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(OnChanged)} - {e}");
        }
    }

    private void TakeScreenshot(string saveDirectory, string gameDirectory)
    {
        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                TakeWindowsScreenshot(saveDirectory, gameDirectory);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                TakeLinuxScreenshot(saveDirectory);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                TakeMacScreenshot(saveDirectory);
            }
            else
            {
                _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeScreenshot)} - Skipping screenshot for platform {RuntimeInformation.OSDescription}");
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(TakeScreenshot)} - {e}");
        }
    }

    private void TakeWindowsScreenshot(string saveDirectory, string gameDirectory)
    {
        if (Directory.Exists(gameDirectory))
        {
            Process gameProcess = GetGameProcess(gameDirectory);

            if (gameProcess != null)
            {
                try
                {
                    byte[] imageData = _shutterService.TakeScreenshot(new ScreenshotOptions()
                    {
                        Target = Shutter.Enums.CaptureTarget.Window,
                        WindowHandle = gameProcess.MainWindowHandle,
                        IncludeBorder = false,
                        IncludeShadow = false,
                        Format = Shutter.Enums.ImageFormat.Jpeg,
                        JpegQuality = 85,
                    });

                    if (imageData != null)
                    {
                        string savePath = Path.Combine(saveDirectory, _config.LatestScreenshotName);

                        if (File.Exists(savePath))
                        {
                            File.Delete(savePath);
                        }

                        File.WriteAllBytes(savePath, imageData);
                    }
                }
                catch (Exception e)
                {
                    _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeWindowsScreenshot)} - Window screenshot failed: {e.ToString()}");
                }
            }
            else
            {
                _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeWindowsScreenshot)} - Failed to find game process for directory {gameDirectory}");
            }
        }
    }

    private void TakeLinuxScreenshot(string saveDirectory)
    {
        try
        {
            byte[] imageData = _shutterService.TakeScreenshot(new ScreenshotOptions()
            {
                Target = Shutter.Enums.CaptureTarget.FullScreen,
                Format = Shutter.Enums.ImageFormat.Jpeg,
                JpegQuality = 85,
            });

            if (imageData != null)
            {
                string savePath = Path.Combine(saveDirectory, _config.LatestScreenshotName);

                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }

                File.WriteAllBytes(savePath, imageData);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeLinuxScreenshot)} - Screenshot failed: {e.ToString()}");
        }
    }

    private void TakeMacScreenshot(string saveDirectory)
    {
        try
        {
            byte[] imageData = _shutterService.TakeScreenshot(new ScreenshotOptions()
            {
                Target = Shutter.Enums.CaptureTarget.FullScreen,
                Format = Shutter.Enums.ImageFormat.Jpeg,
                JpegQuality = 85,
            });

            if (imageData != null)
            {
                string savePath = Path.Combine(saveDirectory, _config.LatestScreenshotName);

                if (File.Exists(savePath))
                {
                    File.Delete(savePath);
                }

                File.WriteAllBytes(savePath, imageData);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeMacScreenshot)} - Screenshot failed: {e.ToString()}");
        }
    }

    private void TakeLinuxScreenshotFlameshot(string saveDirectory)
    {
        if (IsFlameshotSetup())
        {
            string savePath = Path.Combine(saveDirectory, _config.LatestScreenshotName);

            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            string arguments = _isInFlatpak
                ? $"{_flameshotArgs} screen -r -p \"{savePath}\""
                : $"screen -r -p \"{savePath}\"";

            for (int attempt = 1; attempt <= 3; attempt++)
            {
                ProcessStartInfo flameshotStartInfo = new()
                {
                    FileName = _flameshotCommand,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                flameshotStartInfo.EnvironmentVariables["DISPLAY"] = Environment.GetEnvironmentVariable("DISPLAY") ?? ":0";

                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("XAUTHORITY")))
                {
                    flameshotStartInfo.EnvironmentVariables["XAUTHORITY"] = Environment.GetEnvironmentVariable("XAUTHORITY");
                }

                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                {
                    flameshotStartInfo.EnvironmentVariables["WAYLAND_DISPLAY"] = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
                }

                if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")))
                {
                    flameshotStartInfo.EnvironmentVariables["XDG_RUNTIME_DIR"] = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
                }

                int? exitCode = null;
                Process process = Process.Start(flameshotStartInfo);

                try
                {
                    if (process.WaitForExit(3000))
                    {
                        exitCode = process.ExitCode;
                    }
                    else
                    {
                        process.Kill();
                    }

                    Thread.Sleep(100);

                    if (!File.Exists(savePath))
                    {
                        if (attempt == 3)
                        {
                            _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeLinuxScreenshotFlameshot)} - {_flameshotCommand} {arguments}");

                            if (exitCode != null)
                            {
                                _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeLinuxScreenshotFlameshot)} - Screenshot may have failed ({exitCode})");
                            }
                            else
                            {
                                _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeLinuxScreenshotFlameshot)} - Flameshot process timed out: killed and screenshot not found");
                            }
                        }
                        else
                        {
                            Thread.Sleep(250);
                        }
                    }
                    else
                    {
                        break;
                    }
                }
                finally
                {
                    process.Close();
                }
            }
        }
        else
        {
            _loggingService.LogInfo($"{nameof(ScreenshotService)}>{nameof(TakeLinuxScreenshotFlameshot)} - flameshot not available");
        }
    }

    private HashSet<string> GetExecutables(string directory)
    {
        HashSet<string> fileSet = new();

        try
        {
            if (Directory.Exists(directory))
            {
                IEnumerable<string> files = Directory
                    .GetFiles(directory, "*.*", SearchOption.AllDirectories)
                    .Where (x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || String.IsNullOrWhiteSpace(Path.GetExtension(x)))
                    .Select(Path.GetFileNameWithoutExtension)
                    .Distinct();

                fileSet = new(files, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(GetExecutables)} - {e}");
        }

        return fileSet;
    }

    private Process GetGameProcess(string directory)
    {
        Process gameProcess = null;

        HashSet<string> executables = GetExecutables(directory);

        foreach (Process process in Process.GetProcesses())
        {
            if (executables.Contains(process.ProcessName) && 
                process.MainWindowHandle != IntPtr.Zero)
            {
                gameProcess = process;
                break;
            }
        }

        return gameProcess;
    }

    private bool IsFlameshotSetup()
    {
        if (!_flameshotSetup)
        {
            try
            {
                ProcessStartInfo flameshotStartInfo;

                if (_isInFlatpak)
                {
                    flameshotStartInfo = new()
                    {
                        FileName = "flatpak-spawn",
                        Arguments = "--host which flameshot",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    };
                }
                else
                {
                    flameshotStartInfo = new()
                    {
                        FileName = "which",
                        Arguments = "flameshot",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false
                    };
                }

                Process process = Process.Start(flameshotStartInfo);
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    SetupFlameshotConfig();

                    _flameshotSetup = true;
                    _loggingService.LogInfo($"{nameof(ScreenshotService)} - Flameshot detected and configured (Flatpak: {_isInFlatpak})");
                }
                else
                {
                    _loggingService.LogInfo($"{nameof(ScreenshotService)} - Flameshot not found (Flatpak: {_isInFlatpak})");
                }
            }
            catch (Exception e)
            {
                _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(IsFlameshotSetup)} - {e}");
            }
        }

        return _flameshotSetup;
    }

    private void SetupFlameshotConfig()
    {
        try
        {
            string flameshotConfigPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "flameshot/flameshot.ini"
            );

            bool isConfigDefault = false;
            if (File.Exists(flameshotConfigPath))
            {
                string currentConfig = File.ReadAllText(flameshotConfigPath).Trim();
                isConfigDefault = String.IsNullOrWhiteSpace(currentConfig) ||
                                  currentConfig == "[General]" ||
                                  currentConfig == "[General]\ncontrastOpacity=188";
            }
            else
            {
                isConfigDefault = true;
                string configDir = Path.GetDirectoryName(flameshotConfigPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }
            }

            if (isConfigDefault)
            {
                string flameshotConfig = "[General]\ncontrastOpacity=188\nshowDesktopNotification=false\n";
                File.WriteAllText(flameshotConfigPath, flameshotConfig);
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(ScreenshotService)}>{nameof(SetupFlameshotConfig)} - {e}");
        }
    }

    #endregion
}