using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;

namespace QBounce;

internal sealed class ConfigWindow(Plugin plugin) : Window("q弹狒狒###QBounceConfig"), IDisposable
{
    private readonly FileDialogManager files = new();
    public override void Draw()
    {
        var c = plugin.Config;
        var language = plugin.Language;
        string T(TextId key) => Localization.Get(key, language);
        var changed = false;
        ImGui.TextDisabled(T(TextId.Credits));
        var selection = Array.IndexOf(Localization.Preferences, c.UiLanguage);
        if (ImGui.Combo(T(TextId.Language) + "###UiLanguage", ref selection, Localization.LanguageOptions))
        {
            c.UiLanguage = Localization.Preferences[selection]; changed = true;
        }
        ImGui.Checkbox(T(TextId.Enable), ref plugin.Enabled);
        changed |= ImGui.SliderFloat(T(TextId.Amplitude), ref c.Amplitude, 0, .6f, "%.2f");
        changed |= ImGui.SliderFloat(T(TextId.Period), ref c.Period, .2f, 1.5f, "%.2f");
        changed |= ImGui.Checkbox(T(TextId.RandomPhase), ref c.RandomPhase);
        ImGui.Separator();
        ImGui.TextUnformatted(T(TextId.Scope));
        changed |= ImGui.Checkbox(T(TextId.Self), ref c.AffectSelf);
        ImGui.SameLine(); changed |= ImGui.Checkbox(T(TextId.Players), ref c.AffectPlayers);
        changed |= ImGui.Checkbox(T(TextId.Npcs), ref c.AffectNpcs);
        ImGui.SameLine(); changed |= ImGui.Checkbox(T(TextId.Monsters), ref c.AffectMonsters);
        ImGui.Separator();
        if (ImGui.Checkbox(T(TextId.Bgm), ref c.BgmEnabled)) { plugin.AudioChanged = true; changed = true; }
        changed |= ImGui.Checkbox(T(TextId.Mute), ref c.Muted);
        changed |= ImGui.SliderFloat(T(TextId.Volume), ref c.Volume, 0, 1, "%.2f");
        if (ImGui.InputText(T(TextId.AudioPath), ref c.AudioPath, 2048)) changed = true;
        if (ImGui.Button(T(TextId.BrowseMusic)))
            files.OpenFileDialog(T(TextId.SelectMusic), T(TextId.AudioFilter), (ok, path) =>
            {
                if (!ok) return;
                c.AudioPath = path; plugin.AudioChanged = true; plugin.Save();
            });
        ImGui.SameLine();
        if (ImGui.Button(T(TextId.ResetMusic))) plugin.ResetRequested = true;
        ImGui.TextWrapped(T(TextId.CurrentMusic) + plugin.CurrentAudioPath);
        ImGui.TextWrapped(T(TextId.MusicHint));
        ImGui.TextWrapped(T(TextId.MusicSource) + "https://www.bilibili.com/video/BV1VGYR6bECg/");
        ImGui.TextWrapped(T(TextId.MusicStatus) + plugin.MusicStatus);
        ImGui.Separator();
        changed |= ImGui.Checkbox(T(TextId.Hotkeys), ref c.HotkeysEnabled);
        ImGui.TextWrapped(T(TextId.HotkeyHint));
        ImGui.TextWrapped(Localization.Format(TextId.Counts, language, plugin.ProcessedCount, plugin.TrackedCount, plugin.HookCount));
        ImGui.TextWrapped(T(TextId.Status) + plugin.Status);
        ImGui.TextWrapped(T(TextId.LastError) + plugin.LastError);
        ImGui.TextWrapped(T(TextId.SceneHint));
        if (changed) plugin.Save();
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(520, 700), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(360, 440), new Vector2(900, 1000));
    }
    public void DrawFileDialog() => files.Draw();
    public void Dispose() => files.Reset();
}
