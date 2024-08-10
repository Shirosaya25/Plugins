using Dalamud.Configuration;
using System;

namespace AutoResetActPlugin;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public bool ShowDebugLogs { get; set; } = false;
    public bool IsSharedMacro { get; set; } = false;
    public int macroId { get; set; } = 0;

    // the below exist just to make saving less cumbersome
    public void Save()
    {
        DalamudApi.PluginInterface.SavePluginConfig(this);
    }
}
