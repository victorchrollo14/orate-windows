using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Orate.Services;

/// <summary>
/// Records the microphone to 16 kHz mono PCM and, on stop, encodes it to FLAC using the
/// Media Foundation FLAC encoder built into Windows 10/11 (no extra dependency). The Windows
/// analog of macOS AudioRecorder. Raises <see cref="OnLevel"/> with a normalized 0…1 level
/// for the overlay waveform (on the capture thread — marshal to UI in the handler).
/// </summary>
public sealed class AudioRecorder
{
    // FLAC media subtype GUID (format tag 0xF1AC). Matches what the Cloudflare Worker expects.
    private static readonly Guid MFAudioFormat_FLAC = new("0000F1AC-0000-0010-8000-00AA00389B71");

    // NAudio WaveFormat(rate, bits, channels): 16 kHz, 16-bit, mono. Positional to avoid
    // depending on the parameter names.
    private static readonly WaveFormat RecordingFormat = new(16000, 16, 1);

    private WaveInEvent? _waveIn;
    private MemoryStream? _pcmBuffer;

    /// <summary>Called with a normalized audio level (0…1) at roughly 30 fps.</summary>
    public Action<double>? OnLevel;

    public bool IsRecording => _waveIn != null;

    public void StartRecording()
    {
        if (_waveIn != null) return;

        _pcmBuffer = new MemoryStream();
        _waveIn = new WaveInEvent
        {
            WaveFormat = RecordingFormat,
            BufferMilliseconds = 33,   // ~30 fps metering, like the macOS meter timer
            NumberOfBuffers = 3,
        };
        _waveIn.DataAvailable += OnDataAvailable;

        try
        {
            _waveIn.StartRecording();
        }
        catch (Exception ex)
        {
            Logger.Log("AudioRecorder: failed to start recording (mic permission or no device?)", ex);
            Cleanup();
        }
    }

    /// <summary>Stops capture and returns FLAC-encoded audio, or null if nothing was captured.</summary>
    public byte[]? StopRecording()
    {
        if (_waveIn == null) return null;

        _waveIn.DataAvailable -= OnDataAvailable;
        try { _waveIn.StopRecording(); } catch { /* ignore */ }
        _waveIn.Dispose();
        _waveIn = null;

        var pcm = _pcmBuffer?.ToArray();
        _pcmBuffer?.Dispose();
        _pcmBuffer = null;

        if (pcm == null || pcm.Length == 0)
        {
            Logger.Log("AudioRecorder: no PCM captured.");
            return null;
        }

        try
        {
            var flac = EncodeToFlac(pcm, RecordingFormat);
            Logger.Log($"AudioRecorder: captured {pcm.Length} PCM bytes -> {flac.Length} FLAC bytes.");
            return flac;
        }
        catch (Exception ex)
        {
            Logger.Log("AudioRecorder: FLAC encode failed", ex);
            return null;
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        _pcmBuffer?.Write(e.Buffer, 0, e.BytesRecorded);

        // Compute RMS over the 16-bit samples, convert to dB, normalize -40…0 dB -> 0…1
        // (mirrors macOS averagePower normalization).
        double sumSquares = 0;
        int sampleCount = e.BytesRecorded / 2;
        for (int i = 0; i + 1 < e.BytesRecorded; i += 2)
        {
            short sample = (short)(e.Buffer[i] | (e.Buffer[i + 1] << 8));
            double s = sample / 32768.0;
            sumSquares += s * s;
        }

        if (sampleCount > 0 && OnLevel != null)
        {
            double rms = Math.Sqrt(sumSquares / sampleCount);
            double db = rms > 0 ? 20.0 * Math.Log10(rms) : -160.0;
            double clamped = Math.Max(-40.0, Math.Min(db, 0.0));
            double normalized = (clamped + 40.0) / 40.0;
            OnLevel(normalized);
        }
    }

    private void Cleanup()
    {
        _waveIn?.Dispose();
        _waveIn = null;
        _pcmBuffer?.Dispose();
        _pcmBuffer = null;
    }

    // ---- FLAC encoding via Media Foundation ----

    private static byte[] EncodeToFlac(byte[] pcm, WaveFormat format)
    {
        MediaFoundationApi.Startup();

        var mediaType = SelectFlacMediaType(format)
            ?? throw new InvalidOperationException("No Media Foundation FLAC encoder available on this system.");

        var tmp = Path.Combine(Path.GetTempPath(), $"orate_{Guid.NewGuid():N}.flac");
        try
        {
            using (var input = new RawSourceWaveStream(new MemoryStream(pcm), format))
            using (var encoder = new MediaFoundationEncoder(mediaType))
            {
                encoder.Encode(tmp, input);
            }
            return File.ReadAllBytes(tmp);
        }
        finally
        {
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }

    private static MediaType? SelectFlacMediaType(WaveFormat format)
    {
        var candidates = MediaFoundationEncoder.GetOutputMediaTypes(MFAudioFormat_FLAC);
        if (candidates.Length == 0) return null;

        // Prefer an output type that matches our sample rate and channel count.
        MediaType? channelMatch = null;
        foreach (var mt in candidates)
        {
            if (TryGet(mt, out int sr, out int ch))
            {
                if (sr == format.SampleRate && ch == format.Channels) return mt;
                if (ch == format.Channels) channelMatch ??= mt;
            }
        }
        return channelMatch ?? candidates[0];
    }

    private static bool TryGet(MediaType mt, out int sampleRate, out int channels)
    {
        try
        {
            sampleRate = mt.SampleRate;
            channels = mt.ChannelCount;
            return true;
        }
        catch
        {
            sampleRate = 0;
            channels = 0;
            return false;
        }
    }
}
