using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Timers;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;
using Scum_Bag.DataAccess.Data.Steam;
using Scum_Bag.Models;

namespace Scum_Bag.Services;

internal sealed class GameService : IDisposable
{
    #region Native

    public enum WindowShowStyle : uint
    {
        Hide = 0,
        ShowNormal = 1,
        ShowMinimized = 2,
        ShowMaximized = 3,
        Maximize = 3,
        ShowNormalNoActivate = 4,
        Show = 5,
        Minimize = 6,
        ShowMinNoActivate = 7,
        ShowNoActivate = 8,
        Restore = 9,
        ShowDefault = 10,
        ForceMinimized = 11
    }

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, WindowShowStyle nCmdShow);

    #endregion

    #region Fields

    private static readonly HashSet<string> _blackList = ["Steamworks Common Redistributables"];

    private readonly ILoggingService _loggingService;
    private readonly Timer _steamLibraryTimer;
    private readonly IConfig _config;
    private string _libraryPath;
    private List<AppState> _steamApps;

    #endregion

    #region Constructor

    public GameService(ILoggingService loggingService, IConfig config)
    {
        _loggingService = loggingService;
        _config = config;

        UpdateSteamLibraryPath();

        _steamApps = new();

        _steamLibraryTimer = new Timer(TimeSpan.FromMinutes(1));
        _steamLibraryTimer.Elapsed += SteamLibraryTimer_Elapsed;
        _steamLibraryTimer.AutoReset = true;
        _steamLibraryTimer.Enabled = true;

        UpdateSteamLibrary(true);
    }

    #endregion

    #region Public Methods

    public GamesList GetInstalledGames()
    {
        return new GamesList()
        {
            Games = _steamApps.Select(x => x.Name)
        };
    }

    public IEnumerable<AppState> GetInstalledApps()
    {
        return _steamApps.AsReadOnly();
    }

    public void Dispose()
    {
        _steamLibraryTimer.Enabled = false;
        _steamLibraryTimer.Elapsed -= SteamLibraryTimer_Elapsed;
        _steamLibraryTimer.Dispose();
    }

    public bool LaunchGame(string gameName)
    {
        bool launched = false;

        if (!String.IsNullOrWhiteSpace(gameName))
        {
            AppState appState = _steamApps.FirstOrDefault(x => x.Name == gameName);

            if (appState != null)
            {
                try
                {
                    Process gameProcess = null;

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        Process.Start("xdg-open", $"steam://launch/{appState.AppId}");
                    }
                    else
                    {
                        Process.Start(_config.SteamExePath, $"steam://launch/{appState.AppId}");
                    }

                    HashSet<string> possibleGameExecutables = new();

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        possibleGameExecutables = new(Directory
                            .GetFiles(appState.FullInstallDir, "*.*", SearchOption.AllDirectories)
                            .Where (x => x.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                            .Select(Path.GetFileNameWithoutExtension));
                    }
                    else
                    {
                        // No reliable way to check if file is executable on Linux using C#...
                        possibleGameExecutables = new(Directory
                            .GetFiles(appState.FullInstallDir, "*.*", SearchOption.AllDirectories)
                            .Select(Path.GetFileName));
                    }

                    int retryCount = 0;
                    while (gameProcess == null && retryCount++ < 10)
                    {
                        System.Threading.Thread.Sleep(1500);

                        gameProcess = Process.GetProcesses()
                            .Where(x => possibleGameExecutables.Contains(x.ProcessName))
                            .OrderByDescending(x => x.NonpagedSystemMemorySize64)
                            .FirstOrDefault();
                    }
                    
                    if (gameProcess != null)
                    {
                        launched = true;
                        BringWindowToForeground(gameProcess.MainWindowHandle);
                    }
                    else
                    {
                        _loggingService.LogInfo($"{nameof(GameService)}>{nameof(LaunchGame)} - Game Not Found: {gameName}");
                    }
                }
                catch (Exception e)
                {
                    _loggingService.LogError($"{nameof(GameService)}>{nameof(LaunchGame)} - {e}");
                }
            }
        }

        return launched;
    }

    public void InitializeSteamLibrary()
    {
        UpdateSteamLibraryPath();
        UpdateSteamLibrary();
    }

    #endregion

    #region Private Methods

    private void UpdateSteamLibraryPath()
    {
        _libraryPath = Path.Combine(Path.Combine(Path.GetDirectoryName(_config.SteamExePath), "steamapps"), "libraryfolders.vdf");
    }

    private void SteamLibraryTimer_Elapsed(object sender, ElapsedEventArgs e)
    {
        UpdateSteamLibrary();
    }

    private void UpdateSteamLibrary(bool logInfo = false)
    {
        try
        {
            lock (_steamApps)
            {
                _steamApps.Clear();

                if (logInfo)
                {
                    _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam Path: {_config.SteamExePath}");
                    _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam Libraries Path: {_libraryPath}");
                }

                if (File.Exists(_libraryPath))
                {
                    string libraryContent = File.ReadAllText(_libraryPath);
                    VProperty libraryVdf = VdfConvert.Deserialize(libraryContent);

                    Library library = new()
                    {
                        LibraryFolders = []
                    };

                    VToken rootNode = libraryVdf.Value;

                    if (rootNode != null)
                    {
                        VToken contentStatsId = rootNode["contentstatsid"];

                        if (contentStatsId != null)
                        {
                            library.LibraryFolders.ContentStatsId = contentStatsId.ToString();
                        }

                        foreach (VProperty child in rootNode.Children<VProperty>())
                        {
                            if (int.TryParse(child.Key, out int folderIndex))
                            {
                                VToken folderData = child.Value;
                                LibraryFolder libraryFolder = new()
                                {
                                    Path = folderData["path"]?.ToString(),
                                    Label = folderData["label"]?.ToString()
                                };

                                if (!string.IsNullOrEmpty(libraryFolder.Path))
                                {
                                    libraryFolder.Path = libraryFolder.Path.Trim('"');
                                    library.LibraryFolders[folderIndex] = libraryFolder;
                                }
                            }
                        }
                    }

                    foreach (LibraryFolder libraryFolder in library.LibraryFolders.Values)
                    {
                        string appsPath = Path.Combine(libraryFolder.Path, "steamapps");

                        if (Directory.Exists(appsPath))
                        {
                            if (logInfo)
                            {
                                _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam Library Found: {appsPath}");
                            }

                            try
                            {
                                foreach (string acfFile in Directory.EnumerateFiles(appsPath, "*.acf"))
                                {
                                    try
                                    {
                                        if (File.Exists(acfFile))
                                        {
                                            string acfText = File.ReadAllText(acfFile);

                                            if (acfText.StartsWith("\"AppState\""))
                                            {
                                                if (logInfo)
                                                {
                                                    _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam App Entry Found: {acfFile}");
                                                }

                                                VProperty appVdf = VdfConvert.Deserialize(acfText);
                                                VToken appStateNode = appVdf.Value;

                                                if (appStateNode != null)
                                                {
                                                    AppState appState = new()
                                                    {
                                                        AppId = appStateNode["appid"]?.ToString(),
                                                        Name = appStateNode["name"]?.ToString(),
                                                        InstallDir = appStateNode["installdir"]?.ToString(),
                                                        LauncherPath = appStateNode["LauncherPath"]?.ToString(),
                                                        LibraryAppDir = Path.Combine(appsPath, "common")
                                                    };

                                                    App app = new() { AppState = appState };

                                                    if (!string.IsNullOrEmpty(app.AppState.Name) &&
                                                        !_blackList.Contains(app.AppState.Name) &&
                                                        !app.AppState.Name.StartsWith("Proton ") &&
                                                        !app.AppState.Name.StartsWith("Steam Linux Runtime "))
                                                    {
                                                        app.AppState.Name = ConvertToAscii(app.AppState.Name);
                                                        _steamApps.Add(app.AppState);

                                                        if (logInfo)
                                                        {
                                                            _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam App Found: {app.AppState.Name}");
                                                        }
                                                    }
                                                    else if (logInfo)
                                                    {
                                                        _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam App Skipped: {app.AppState.Name}");
                                                    }
                                                }
                                            }
                                            else if (logInfo)
                                            {
                                                _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam App Invalid: {acfFile}");
                                            }
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        _loggingService.LogError($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Error processing {acfFile}: {e}");
                                    }
                                }
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // Log that this library needs manual permission
                                _loggingService.LogInfo($"Steam library at {libraryFolder.Path} requires permission. " +
                                    $"Run: flatpak override --user --filesystem={libraryFolder.Path}:ro io.github.rthomasv3.ScumBag");
                            }
                        }
                        else if (logInfo)
                        {
                            _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam Library Invalid: {appsPath}");
                        }
                    }
                }
                else
                {
                    _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Steam Libraries Not Found");
                }

                if (logInfo)
                {
                    _loggingService.LogInfo($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - Done");
                }
            }
        }
        catch (Exception e)
        {
            _loggingService.LogError($"{nameof(GameService)}>{nameof(UpdateSteamLibrary)} - {e}");
        }
    }

    private static string ConvertToAscii(string text)
    {
        string cleanedText = text
            .Replace('’','\'')
            .Replace('–', '-')
            .Replace('“', '"')
            .Replace('”', '"')
            .Replace("…", "...")
            .Replace("—", "--")
            .Replace("™", "")
            .Replace("®", "");
        byte[] textData = Encoding.Convert(Encoding.Default, Encoding.ASCII, Encoding.Default.GetBytes(cleanedText));
        return Encoding.ASCII.GetString(textData);
    }

    private void BringWindowToForeground(nint windowHandle)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            if (windowHandle != GetForegroundWindow())
            {
                if (IsIconic(windowHandle))
                {
                    // Minimized so send restore
                    ShowWindow(windowHandle, WindowShowStyle.Restore);
                }
                else
                {
                    // Already Maximized or Restored so just bring to front
                    SetForegroundWindow(windowHandle);
                }
            }
        }
    }

    #endregion
}