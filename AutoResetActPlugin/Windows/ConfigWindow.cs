using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace AutoResetActPlugin.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;

    // We give this window a constant ID using ###
    // This allows for labels being dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("AutoACT Settings")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(400, 140);
        SizeCondition = ImGuiCond.Always;

        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
    }

    public override void Draw()
    {
        var DebugLogConfigValue = Configuration.ShowDebugLogs;
        if (ImGui.Checkbox("Show Debug Logs", ref DebugLogConfigValue))
        {
            Configuration.ShowDebugLogs = DebugLogConfigValue;
            Configuration.Save();
        }
        var MacroNumberConfigValue = Configuration.macroId;
        if (ImGui.InputInt("Macro Number", ref MacroNumberConfigValue))
        {
            Configuration.macroId = MacroNumberConfigValue;
            Configuration.Save();
        }
        var IsSharedMacroConfigValue = Configuration.IsSharedMacro;
        if (ImGui.Checkbox("Is Shared Macro", ref IsSharedMacroConfigValue))
        {
            Configuration.IsSharedMacro = IsSharedMacroConfigValue;
            Configuration.Save();
        }
    }
}
