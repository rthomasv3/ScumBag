using System;
using System.IO;
using System.Threading;

namespace Scum_Bag.Models;

internal sealed class WatchLocation
{
    public FileSystemWatcher Watcher { get; set; }
    public Guid SaveGameId { get; set; }
    public string Location { get; set; }
    public string GameDirectory { get; set; }
    public Timer DebounceTimer { get; set; }
}