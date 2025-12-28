using System;
using System.Collections.Generic;
using GaldrJson;

namespace Scum_Bag.DataAccess.Data;

[GaldrJsonSerializable]
internal sealed class SaveGame
{
    public Guid Id { get; set; }
    public bool Enabled { get; set; }
    public string BackupLocation { get; set; }
    
    public string Name { get; set; }
    public string SaveLocation { get; set; }
    public int Frequency { get; set; }
    public int MaxBackups { get; set; }
    public string Game { get; set; }
    public string Icon { get; set; }
    public Dictionary<string, BackupMetadata> BackupMetadata { get; set; } = new Dictionary<string, BackupMetadata>();
}
