using NAudio.Vorbis;
using NAudio.Wave;

namespace Hamyar;

/// <summary>Plays alarm sounds (mp3, wav, wma, aac, m4a, flac, ogg, aiff ...). Loops until Stop().</summary>
public static class AudioService
{
    static WaveOutEvent _out;
    static WaveStream _reader;
    static bool _stopped = true;
    static DateTime _startedAt;
    static int _quickRestarts;

    static WaveStream OpenReader(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext == ".ogg") return new VorbisWaveReader(path);
        if (ext == ".aif" || ext == ".aiff") return new AiffFileReader(path);
        return new MediaFoundationReader(path);   // mp3, wav, wma, aac, m4a, flac, mp2, mp4 ...
    }

    /// <summary>Default alarm sound: Windows' "Alarm01".</summary>
    public static string DefaultSound
    {
        get
        {
            var p = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            return File.Exists(p) ? p : "";
        }
    }

    public static void Play(string path, bool loop)
    {
        Stop();
        try
        {
            if (string.IsNullOrWhiteSpace(path)) path = DefaultSound;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                System.Media.SystemSounds.Exclamation.Play();
                return;
            }
            var reader = OpenReader(path);
            var output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += (s, e) =>
            {
                if (!loop || _stopped || _out != output) return;
                // never restart in a tight loop (empty / broken file would freeze the UI)
                if ((DateTime.Now - _startedAt).TotalMilliseconds < 400)
                {
                    if (++_quickRestarts > 3) return;
                }
                else _quickRestarts = 0;
                try
                {
                    reader.Position = 0;
                    _startedAt = DateTime.Now;
                    output.Play();
                }
                catch { }
            };
            _reader = reader;
            _out = output;
            _stopped = false;
            _quickRestarts = 0;
            _startedAt = DateTime.Now;
            output.Play();
        }
        catch (Exception ex)
        {
            Log.Write("Audio: " + ex.Message);
            System.Media.SystemSounds.Exclamation.Play();
        }
    }

    public static void Stop()
    {
        _stopped = true;
        try { _out?.Stop(); } catch { }
        try { _out?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        _out = null;
        _reader = null;
    }
}
