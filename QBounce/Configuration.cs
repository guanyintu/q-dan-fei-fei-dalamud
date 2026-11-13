using Dalamud.Configuration;

namespace QBounce;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    // Enabled is deliberately session-only: loading/reloading never starts native writes or audio.
    public float Amplitude = .4f;
    public float Period = .45f;
    public bool RandomPhase;
    public bool AffectSelf = true;
    public bool AffectPlayers = true;
    public bool AffectNpcs = true;
    public bool AffectMonsters = true;
    public bool BgmEnabled = true;
    public bool Muted;
    public float Volume = .35f;
    public string AudioPath = "";
    public bool HotkeysEnabled;
    public string UiLanguage = "auto";
    public bool Rotate;
    public float RotationSpeed = 90;

    public void Normalize()
    {
        Amplitude = float.IsFinite(Amplitude) ? Math.Clamp(Amplitude, 0, .6f) : .4f;
        Period = float.IsFinite(Period) ? Math.Clamp(Period, .2f, 1.5f) : .45f;
        Volume = float.IsFinite(Volume) ? Math.Clamp(Volume, 0, 1) : .35f;
        AudioPath ??= "";
        UiLanguage = Localization.NormalizePreference(UiLanguage);
        RotationSpeed = float.IsFinite(RotationSpeed) ? Math.Clamp(RotationSpeed, -720, 720) : 90;
    }

    public BounceSettings Settings => new(Amplitude, Period, RandomPhase, AffectSelf, AffectPlayers, AffectNpcs, AffectMonsters, Rotate, RotationSpeed);
}
