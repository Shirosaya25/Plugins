using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Plugin;
using System;
using Dalamud.Interface.Windowing;
using AutoResetActPlugin.Windows;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace AutoResetActPlugin;

public unsafe class Plugin : IDalamudPlugin
{

    private const string CommandName = "/autoact";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("AutoResetActPlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private RaptureMacroModule.Macro* macro;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        DalamudApi.Initialize(pluginInterface);

        Configuration = DalamudApi.PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        ConfigWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(ConfigWindow);

        try
        {
            DalamudApi.DutyState.DutyRecommenced += OnDutyRecommenced;
        }
        catch (Exception ex)
        {
            DalamudApi.PluginLog.Error(ex, $"An error occurred loading AutoResetActPlugin.");
            DalamudApi.PluginLog.Error("Plugin will not be loaded.");

            DalamudApi.CommandManager.RemoveHandler(CommandName);

            throw;
        }

        DalamudApi.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand){});

        DalamudApi.PluginInterface.UiBuilder.Draw += DrawUI;
        DalamudApi.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();

        DalamudApi.CommandManager.RemoveHandler(CommandName);
    }

    private void OnDutyRecommenced(object sender, ushort e)
    {
        DebugLog("Reset!");

        uint isShared = (uint) (Configuration.IsSharedMacro ? 1 : 0);
        uint macroNumber = (uint)Configuration.macroId;

        macro = RaptureMacroModule.Instance()->GetMacro(isShared, macroNumber);
        RaptureShellModule.Instance() -> MacroLocked = false;
        RaptureShellModule.Instance()->ExecuteMacro(macro);
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleConfigUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();

    private void DebugLog(string str)
    {
        if (Configuration.ShowDebugLogs)
            DalamudApi.PluginLog.Information($"{str}");
    }
}