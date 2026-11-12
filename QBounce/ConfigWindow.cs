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
        var changed = false;
        ImGui.TextDisabled("作者：someboy · 本项目主要由 Codex 生成");
        ImGui.Checkbox("Q 弹总开关", ref plugin.Enabled);
        changed |= ImGui.SliderFloat("强度", ref c.Amplitude, 0, .6f, "%.2f");
        changed |= ImGui.SliderFloat("周期（秒）", ref c.Period, .2f, 1.5f, "%.2f");
        changed |= ImGui.Checkbox("随机相位（关闭为同步）", ref c.RandomPhase);
        ImGui.Separator();
        ImGui.TextUnformatted("角色范围");
        changed |= ImGui.Checkbox("自己", ref c.AffectSelf);
        ImGui.SameLine(); changed |= ImGui.Checkbox("其他玩家", ref c.AffectPlayers);
        changed |= ImGui.Checkbox("NPC", ref c.AffectNpcs);
        ImGui.SameLine(); changed |= ImGui.Checkbox("怪物 / BOSS", ref c.AffectMonsters);
        ImGui.Separator();
        if (ImGui.Checkbox("BGM", ref c.BgmEnabled)) { plugin.AudioChanged = true; changed = true; }
        changed |= ImGui.Checkbox("静音", ref c.Muted);
        changed |= ImGui.SliderFloat("音量", ref c.Volume, 0, 1, "%.2f");
        if (ImGui.InputText("音频路径", ref c.AudioPath, 2048)) changed = true;
        if (ImGui.Button("选择 MP3 / WAV"))
            files.OpenFileDialog("选择本地音乐", "音频{.mp3,.wav}", (ok, path) =>
            {
                if (!ok) return;
                c.AudioPath = path; plugin.AudioChanged = true; plugin.Save();
            });
        ImGui.SameLine();
        if (ImGui.Button("应用音乐 / 重置节奏")) plugin.ResetRequested = true;
        ImGui.TextWrapped("当前音乐：" + plugin.CurrentAudioPath);
        ImGui.TextWrapped("路径留空使用默认音乐，可随时选择其他 MP3 / WAV。");
        ImGui.TextWrapped("音乐出处：https://www.bilibili.com/video/BV1hXbN6VEKV/");
        ImGui.TextWrapped("音乐：" + plugin.MusicStatus);
        ImGui.Separator();
        changed |= ImGui.Checkbox("启用 F6 / F7 / F8 快捷键", ref c.HotkeysEnabled);
        ImGui.TextWrapped("F6 开关 · F7 强度档位 · F8 从头重置。默认禁用；启用后仍会触发游戏原有绑定。输入文字或后台时忽略。");
        ImGui.TextUnformatted($"已处理 {plugin.ProcessedCount} / 跟踪 {plugin.TrackedCount} · 更新入口 {plugin.HookCount}");
        ImGui.TextWrapped("状态：" + plugin.Status);
        ImGui.TextWrapped("最近错误：" + plugin.LastError);
        ImGui.TextWrapped("过场和 GPose 自动暂停。切图后自动重新发现对象。");
        if (changed) plugin.Save();
    }

    public override void PreDraw()
    {
        ImGui.SetNextWindowSize(new Vector2(460, 630), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(360, 440), new Vector2(900, 1000));
    }
    public void DrawFileDialog() => files.Draw();
    public void Dispose() => files.Reset();
}
