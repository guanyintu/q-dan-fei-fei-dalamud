using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace QBounce;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IClientState Client { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IKeyState Keys { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    internal Configuration Config { get; }
    internal bool Enabled;
    internal bool ResetRequested;
    internal bool AudioChanged;
    internal bool InputCaptured;
    private TextId status = TextId.Disabled;
    private string? errorMessage, restoreMessage;
    private bool isAudioError;
    private string? cachedPreference, cachedHostLanguage;
    private DisplayLanguage cachedLanguage;
    internal DisplayLanguage Language
    {
        get
        {
            var preference = Config.UiLanguage; var host = PluginInterface.UiLanguage;
            if (preference != cachedPreference || host != cachedHostLanguage)
            {
                cachedLanguage = Localization.Resolve(preference, host);
                cachedPreference = preference; cachedHostLanguage = host;
            }
            return cachedLanguage;
        }
    }
    internal string Status => Localization.Get(status, Language);
    internal string LastError => errorMessage == null ? Localization.Get(TextId.None, Language) :
        (isAudioError ? Localization.Get(TextId.AudioPrefix, Language) : "") + errorMessage +
        (restoreMessage == null ? "" : Localization.Get(TextId.RestoreFailed, Language) + restoreMessage);
    internal int ProcessedCount => actors.ProcessedCount;
    internal int TrackedCount => actors.TrackedCount;
    internal int HookCount => hooks.Count;
    internal string MusicStatus => audio.GetStatus(Language);
    internal string DefaultAudioPath { get; }
    private readonly string bundledAudioPath;
    internal string CurrentAudioPath => AudioService.ResolvePath(Config.AudioPath, DefaultAudioPath, bundledAudioPath);
    private readonly WindowSystem windows = new("q弹狒狒");
    private readonly ConfigWindow window;
    private readonly ActorManager actors;
    private readonly TransformService transforms;
    private readonly ActorUpdateHooks hooks;
    private readonly AudioService audio;
    private readonly Action<ActorSnapshot> ensureHook;
    private readonly CommandInfo commandInfo;
    private DisplayLanguage? commandLanguage;
    private long epoch;
    private long lastDiagnostic;
    private bool running;
    private bool disposed;
    private readonly bool[] previousKeys = new bool[3];
    private static readonly VirtualKey[] Hotkeys = [VirtualKey.F6, VirtualKey.F7, VirtualKey.F8];

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Normalize();
        DefaultAudioPath = Path.Combine(PluginInterface.GetPluginConfigDirectory(), "bgm.mp3");
        bundledAudioPath = Path.Combine(PluginInterface.AssemblyLocation.Directory!.FullName, "Assets", "bgm.mp3");
        transforms = new TransformService(Objects, Framework);
        actors = new ActorManager(transforms);
        hooks = new ActorUpdateHooks(Framework, Interop, actors, PauseOnError);
        ensureHook = hooks.Ensure;
        audio = new AudioService(path => new NAudioPlayback(path), ReportAudioError);
        window = new ConfigWindow(this);
        windows.AddWindow(window);
        commandInfo = new CommandInfo(OnCommand);
        RefreshLanguage();
        Commands.AddHandler("/qbounce", commandInfo);
        Framework.Update += OnUpdate;
        Client.TerritoryChanged += OnTerritoryChanged;
        Client.Logout += OnLogout;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi += ToggleWindow;
    }

    internal void Save() { Config.Normalize(); RefreshLanguage(); PluginInterface.SavePluginConfig(Config); }
    private void RefreshLanguage()
    {
        var language = Language;
        if (commandLanguage == language) return;
        commandInfo.HelpMessage = Localization.Get(TextId.CommandHelp, language);
        commandLanguage = language;
    }
    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) Enabled = false;
        else window.Toggle();
    }
    private void ToggleWindow() => window.Toggle();
    private void Draw()
    {
        RefreshLanguage();
        windows.Draw();
        window.DrawFileDialog();
        InputCaptured = Dalamud.Bindings.ImGui.ImGui.GetIO().WantCaptureKeyboard;
    }

    private bool SceneAvailable() => Client.IsLoggedIn && !Client.IsGPosing &&
        !Conditions[ConditionFlag.BetweenAreas] && !Conditions[ConditionFlag.BetweenAreas51] &&
        !Conditions[ConditionFlag.OccupiedInCutSceneEvent] && !Conditions[ConditionFlag.WatchingCutscene] &&
        !Conditions[ConditionFlag.WatchingCutscene78];

    private void OnUpdate(IFramework _)
    {
        if (disposed) return;
        try
        {
            UpdateKeys();
            var available = SceneAvailable();
            WriteDiagnostics(available);
            if (!Enabled || !available)
            {
                if (running) StopEffect();
                status = Enabled ? TextId.ScenePaused : TextId.Disabled;
                return;
            }
            if (!running || ResetRequested)
            {
                hooks.Active = false;
                actors.RestoreAndClear();
                audio.Stop();
                // Decoder/device preparation happens before the shared visual/music start epoch.
                PrepareMusic();
                epoch = Stopwatch.GetTimestamp();
                audio.PlayPrepared();
                running = true;
                ResetRequested = AudioChanged = false;
            }
            else if (AudioChanged) { PrepareMusic(); audio.PlayPrepared(); AudioChanged = false; }
            audio.SetVolume(Config.Volume, Config.Muted);
            var elapsed = Stopwatch.GetElapsedTime(epoch).TotalSeconds;
            actors.BeginFrame(elapsed, Config.Settings, ensureHook);
            hooks.Active = true;
            status = TextId.Running;
        }
        catch (Exception ex) { PauseOnError(ex); }
    }

    private void WriteDiagnostics(bool sceneAvailable)
    {
        var now = Stopwatch.GetTimestamp();
        if (lastDiagnostic != 0 && Stopwatch.GetElapsedTime(lastDiagnostic, now).TotalSeconds < 2) return;
        lastDiagnostic = now;
        try
        {
            var state = new { Version = typeof(Plugin).Assembly.GetName().Version?.ToString(), Enabled, SceneAvailable = sceneAvailable,
                Status, LastError, Processed = actors.ProcessedCount, Tracked = actors.TrackedCount, Hooks = hooks.Count, Local = transforms.InspectLocal() };
            File.WriteAllText(Path.Combine(PluginInterface.GetPluginConfigDirectory(), "diagnostics.json"), JsonSerializer.Serialize(state));
        }
        catch (Exception ex) { Log.Debug(ex, "Runtime status snapshot unavailable"); }
    }

    private void PrepareMusic()
    {
        if (Config.BgmEnabled) audio.Prepare(CurrentAudioPath,
            Config.Volume, Config.Muted);
        else audio.Stop();
    }

    private void StopEffect()
    {
        hooks.Active = false;
        running = false;
        audio.Stop();
        actors.RestoreAndClear();
    }
    private void OnTerritoryChanged(uint _) { try { StopEffect(); } catch (Exception ex) { PauseOnError(ex); } }
    private void OnLogout(int type, int code) { try { StopEffect(); } catch (Exception ex) { PauseOnError(ex); } }

    private void ReportAudioError(Exception ex)
    {
        errorMessage = ex.Message; isAudioError = true; restoreMessage = null;
        Log.Warning(ex, "BGM unavailable");
    }
    private void PauseOnError(Exception ex)
    {
        Enabled = false;
        hooks.Active = false;
        running = false;
        errorMessage = ex.Message; isAudioError = false; restoreMessage = null;
        status = TextId.ErrorStopped;
        Log.Error(ex, "QBounce paused");
        audio.Stop();
        try { actors.RestoreAndClear(); }
        catch (Exception restore) { restoreMessage = restore.Message; Log.Error(restore, "Restore failed"); }
    }

    private unsafe void UpdateKeys()
    {
        if (!Config.HotkeysEnabled || !Client.IsLoggedIn) { Array.Clear(previousKeys); return; }
        var stage = AtkStage.Instance();
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var processId);
        var allowed = Config.HotkeysEnabled && Client.IsLoggedIn && processId == Environment.ProcessId &&
            !InputCaptured && !GameGui.GameUiHidden && stage != null && stage->AtkInputManager != null &&
            !stage->AtkInputManager->IsTextInputActive && !Keys[VirtualKey.CONTROL] &&
            !Keys[VirtualKey.MENU] && !Keys[VirtualKey.SHIFT];
        for (var i = 0; i < Hotkeys.Length; i++)
        {
            var pressed = Keys[Hotkeys[i]];
            if (allowed && pressed && !previousKeys[i])
            {
                if (i == 0) Enabled = !Enabled;
                else if (i == 1)
                {
                    Config.Amplitude = Config.Amplitude < .19f ? .2f : Config.Amplitude < .39f ? .4f : Config.Amplitude < .59f ? .6f : 0;
                    Save();
                }
                else ResetRequested = true;
            }
            previousKeys[i] = pressed;
        }
        // Never consume the game's key-state buffer. Hotkeys are opt-in to avoid existing bindings.
    }

    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    public void Dispose()
    {
        disposed = true;
        Framework.Update -= OnUpdate;
        Client.TerritoryChanged -= OnTerritoryChanged;
        Client.Logout -= OnLogout;
        Commands.RemoveHandler("/qbounce");
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleWindow;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleWindow;
        hooks.Active = false;
        try { Framework.RunOnFrameworkThread(StopEffect).GetAwaiter().GetResult(); }
        catch (Exception ex) { Log.Error(ex, "Unload restoration failed; zone/relog to recreate models"); }
        finally { hooks.Dispose(); audio.Dispose(); windows.RemoveAllWindows(); window.Dispose(); }
    }
}
