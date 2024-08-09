using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using HealingDetailsPlugin.Windows;
using System;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Dalamud.Game.Gui.FlyText;
using Dalamud.Game.Text.SeStringHandling;
using ImGuiNET;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using DObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace HealingDetailsPlugin;

public unsafe class Plugin : IDalamudPlugin
{

    private const int TargetInfoGaugeBgNodeIndex = 41;
    private const int TargetInfoGaugeNodeIndex = 43;

    private const int TargetInfoSplitGaugeBgNodeIndex = 2;
    private const int TargetInfoSplitGaugeNodeIndex = 4;

    private const int FocusTargetInfoGaugeBgNodeIndex = 13;
    private const int FocusTargetInfoGaugeNodeIndex = 15;

    public string Name => "Damage Info";

    private const string CommandName = "/healinfo";

    private readonly Configuration _configuration;
    //private readonly PluginUI _ui;

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("SamplePlugin");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private delegate void ReceiveActionEffectDelegate(uint sourceId, Character* sourceCharacter, IntPtr pos, EffectHeader* effectHeader, EffectEntry* effectArray, ulong* effectTail);
    private readonly Hook<ReceiveActionEffectDelegate> _receiveActionEffectHook;

    private delegate void AddScreenLogDelegate(
        Character* target,
        Character* source,
        FlyTextKind logKind,
        int option,
        int actionKind,
        int actionId,
        int val1,
        int val2,
        int val3,
        int val4);
    private readonly Hook<AddScreenLogDelegate> _addScreenLogHook;

    private ActionEffectStore _actionStore;
    private readonly Dictionary<ulong, string>? _petNicknamesDictionary;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        DalamudApi.Initialize(pluginInterface);
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        _actionStore = new ActionEffectStore();

        // you might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "A useful message to display in /xlhelp"
        });


        try
        {
            var receiveActionEffectFuncPtr = DalamudApi.SigScanner.ScanText("40 55 56 57 41 54 41 55 41 56 48 8D AC 24");
            _receiveActionEffectHook = DalamudApi.Hooks.HookFromAddress<ReceiveActionEffectDelegate>(receiveActionEffectFuncPtr, ReceiveActionEffect);

            var addScreenLogPtr = DalamudApi.SigScanner.ScanText("E8 ?? ?? ?? ?? BF ?? ?? ?? ?? EB 39");
            _addScreenLogHook = DalamudApi.Hooks.HookFromAddress<AddScreenLogDelegate>(addScreenLogPtr, AddScreenLogDetour);

            DalamudApi.FlyTextGui.FlyTextCreated += OnFlyTextCreated;
        }
        catch (Exception ex) {
            DalamudApi.PluginLog.Error(ex, $"An error occurred loading HealingDetailsPlugin.");
            DalamudApi.PluginLog.Error("Plugin will not be loaded.");

            _receiveActionEffectHook?.Disable();
            _receiveActionEffectHook?.Dispose();

            _addScreenLogHook?.Disable();
            _addScreenLogHook?.Dispose();

            DalamudApi.CommandManager.RemoveHandler(CommandName);


            throw;
        }

        _receiveActionEffectHook.Enable();
        _addScreenLogHook?.Enable();

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
    }

    public void Dispose()
    {
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        _receiveActionEffectHook?.Disable();
        _receiveActionEffectHook?.Dispose();

        _addScreenLogHook?.Disable();
        _addScreenLogHook?.Dispose();

        DalamudApi.CommandManager.RemoveHandler(CommandName);
    }

    private uint GetCharacterActorId()
    {
        return DalamudApi.ClientState.LocalPlayer?.EntityId ?? 0;
    }

    private List<uint> FindCharaPets()
    {
        var results = new List<uint>();
        var charaId = GetCharacterActorId();
        foreach (var obj in DalamudApi.ObjectTable)
        {
            if (obj is not IBattleNpc npc) continue;

            var actPtr = npc.Address;
            if (actPtr == IntPtr.Zero) continue;

            if (npc.OwnerId == charaId)
                results.Add(npc.EntityId);
        }

        return results;
    }
    private SeString GetActorName(uint id)
    {
        var dGameObject = DalamudApi.ObjectTable.SearchById(id);
        if (dGameObject == null) return SeString.Empty;
        if (_petNicknamesDictionary != null)
        {
            if (dGameObject.ObjectKind == DObjectKind.BattleNpc && _petNicknamesDictionary.TryGetValue(dGameObject.GameObjectId, out var name)) return name;
        }
        return dGameObject.Name;
    }

    private void ReceiveActionEffect(uint sourceId, Character* sourceCharacter, IntPtr pos, EffectHeader* effectHeader, EffectEntry* effectArray, ulong* effectTail)
    {
        try
        {
            _actionStore.Cleanup();

            DebugLog(LogType.Effect, $"--- source actor: {sourceCharacter->GameObject.EntityId}, action id {effectHeader->ActionId}, anim id {effectHeader->AnimationId} numTargets: {effectHeader->TargetCount} ---");

            // TODO: Reimplement opcode logging, if it's even useful. Original code follows
            // ushort op = *((ushort*) effectHeader.ToPointer() - 0x7);
            // DebugLog(Effect, $"--- source actor: {sourceId}, action id {id}, anim id {animId}, opcode: {op:X} numTargets: {targetCount} ---");

            var entryCount = effectHeader->TargetCount switch
            {
                0 => 0,
                1 => 8,
                <= 8 => 64,
                <= 16 => 128,
                <= 24 => 192,
                <= 32 => 256,
                _ => 0
            };

            for (int i = 0; i < entryCount; i++)
            {
                if (effectArray[i].type == ActionEffectType.Nothing) continue;

                var target = effectTail[i / 8];
                uint val = effectArray[i].value;
                if (effectArray[i].mult != 0)
                    val += ((uint)ushort.MaxValue + 1) * effectArray[i].mult;

                var dmgType = DamageType.None;
                if (effectArray[i].type == ActionEffectType.Heal) dmgType = DamageType.None;

                DebugLog(LogType.Effect, $"{effectArray[i]}, s: {sourceId} t: {target} dmgType {dmgType}");

                var newEffect = new ActionEffectInfo
                {
                    step = ActionStep.Effect,
                    actionId = effectHeader->ActionId,
                    type = effectArray[i].type,
                    // we fill in LogKind later 
                    sourceId = sourceId,
                    targetId = target,
                    value = val
                };

                DebugLog(LogType.Effect, $"New ActionEffectInfo: {newEffect}");

                _actionStore.AddEffect(newEffect);
            }
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "An error has occurred in Damage Info.");
        }

        _receiveActionEffectHook.Original(sourceId, sourceCharacter, pos, effectHeader, effectArray, effectTail);
    }

    private void AddScreenLogDetour(
        Character* target,
        Character* source,
        FlyTextKind logKind,
        int option,
        int actionKind,
        int actionId,
        int val1,
        int val2,
        int serverAttackType,
        int val4)
    {
        try
        {
            var targetId = target->GameObject.EntityId;
            var sourceId = source->GameObject.EntityId;
            //DebugLog(LogType.ScreenLog, $"{option} {actionKind} {actionId}");
            //DebugLog(LogType.ScreenLog, $"{val1} {val2} {serverAttackType} {val4}");
            var targetName = GetActorName(targetId);
            var sourceName = GetActorName(sourceId);
            //DebugLog(LogType.ScreenLog, $"src {sourceId} {sourceName}");
            //DebugLog(LogType.ScreenLog, $"tgt {targetId} {targetName}");
        

            _actionStore.UpdateEffect((uint)actionId, sourceId, targetId, (uint)val1, (uint)serverAttackType, logKind);
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "An error occurred in Healing Detail Info.");
        }

        _addScreenLogHook.Original(target, source, logKind, option, actionKind, actionId, val1, val2, serverAttackType, val4);
    }

    private void OnFlyTextCreated(
        ref FlyTextKind kind,
        ref int val1,
        ref int val2,
        ref SeString text1,
        ref SeString text2,
        ref uint color,
        ref uint icon,
        ref uint damageTypeIcon,
        ref float yOffset,
        ref bool handled)
    {
        try
        {
            var ftKind = kind;

            //if (_configuration.DebugLogEnabled)
            //{
            var str1 = text1?.TextValue.Replace("%", "%%");
            var str2 = text2?.TextValue.Replace("%", "%%");

            DebugLog(LogType.FlyText, $"New Flytext: kind: {ftKind} ({(int)kind}), val1: {val1}, val2: {val2}, color: {color:X}, icon: {icon}");
            DebugLog(LogType.FlyText, $"text1: {str1} | text2: {str2}");
            //}

            var charaId = GetCharacterActorId();
            var petIds = FindCharaPets();

            if (!_actionStore.TryGetEffect((uint)val1, ftKind, charaId, petIds, out var info))
            {
                DebugLog(LogType.FlyText, $"Failed to obtain info... {val1} {ftKind} {charaId}");
                return;
            }

            DebugLog(LogType.FlyText, $"Obtained info: {info}");


            var isHealingAction = info.type == ActionEffectType.Heal;
            var isPetAction = petIds.Contains(info.sourceId);
            var isCharaAction = info.sourceId == charaId;
            var isCharaTarget = info.targetId == charaId;
        }
        catch (Exception e)
        {
            DalamudApi.PluginLog.Error(e, "An error has occurred in Damage Info");
        }
    }

    private void DebugLog(LogType type, string str)
    {
        //if (_configuration.DebugLogEnabled)
        DalamudApi.PluginLog.Information($"[{type}] {str}");
    }

    private void OnCommand(string command, string args)
    {
        // in response to the slash command, just toggle the display status of our main ui
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}