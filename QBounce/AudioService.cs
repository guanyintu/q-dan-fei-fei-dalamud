namespace QBounce;

public interface IAudioPlayback : IDisposable
{
    Exception? Error => null;
    float Volume { set; }
    void Play();
    void Stop();
}

public sealed class AudioService(Func<string, IAudioPlayback> open, Action<Exception> report) : IDisposable
{
    private IAudioPlayback? playback;
    private bool disposed;
    public string Status { get; private set; } = "已停止";

    public static string ResolvePath(string? selectedPath, string configDefault, string bundledDefault)
    {
        if (!string.IsNullOrWhiteSpace(selectedPath)) return selectedPath;
        return File.Exists(configDefault) || !File.Exists(bundledDefault) ? configDefault : bundledDefault;
    }

    public void Start(string path, float volume, bool muted)
    {
        Prepare(path, volume, muted);
        PlayPrepared();
    }

    public void Prepare(string path, float volume, bool muted)
    {
        Stop();
        if (disposed) return;
        try
        {
            if (!File.Exists(path)) { Status = "未找到音乐文件，Q 弹继续运行"; return; }
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) &&
                !extension.Equals(".wav", StringComparison.OrdinalIgnoreCase))
                throw new NotSupportedException("请选择 MP3 或 WAV 文件。");
            playback = open(path);
            SetVolume(volume, muted);
            Status = "已准备";
        }
        catch (Exception ex)
        {
            Stop();
            Status = "音乐不可用，Q 弹继续运行";
            report(ex);
        }
    }

    public void PlayPrepared()
    {
        if (playback == null || disposed) return;
        try { playback.Play(); Status = "循环播放"; }
        catch (Exception ex) { Stop(); Status = "音乐不可用，Q 弹继续运行"; report(ex); }
    }

    public void SetVolume(float volume, bool muted)
    {
        if (playback?.Error is { } error)
        {
            Stop();
            Status = "音频设备中断，Q 弹继续运行";
            report(error);
            return;
        }
        if (playback != null) playback.Volume = muted ? 0 : float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0;
    }

    public void Stop()
    {
        var old = playback;
        playback = null;
        if (old != null)
        {
            try { old.Stop(); }
            catch (Exception ex) { report(ex); }
            finally
            {
                try { old.Dispose(); }
                catch (Exception ex) { report(ex); }
            }
        }
        Status = "已停止";
    }

    public void Dispose() { if (disposed) return; Stop(); disposed = true; }
}
