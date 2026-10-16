using System.IO;
using NAudio.CoreAudioApi;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Orate.Services;

/// <summary>
/// Records the microphone and, on stop, produces 16 kHz mono FLAC using the Media Foundation
/// FLAC encoder built into Windows 10/11 (no extra dependency). The Windows analog of macOS
/// AudioRecorder. Raises <see cref="OnLevel"/> with a normalized 0…1 level for the overlay
/// waveform (on the capture thread — marshal to UI in the handler).
///
/// Capture uses WASAPI in the device's own mix format (e.g. 48 kHz float stereo), downmixes to
/// mono and resamples to 16 kHz on stop. Asking the legacy waveIn API for 16 kHz directly fails
/// with "InvalidParameter calling waveInOpen" on some drivers, so waveIn (through the Windows
/// format mapper) is only a fallback for when WASAPI can't start.
/// </summary>
public sealed class AudioRecorder
{
    // FLAC media subtype GUID (format tag 0xF1AC). Matches what the Cloudflare Worker expects.
    private static readonly Guid MFAudioFormat_FLAC = new("0000F1AC-0000-0010-8000-00AA00389B71");

    // KSDATAFORMAT_SUBTYPE_IEEE_FLOAT — the usual WASAPI mix format subtype.
    private static readonly Guid SubtypeIeeeFloat = new("00000003-0000-0010-8000-00AA00389B71");

    // What we send: 16 kHz, 16-bit, mono. Positional to avoid depending on parameter names.
    private static readonly WaveFormat RecordingFormat = new(16000, 16, 1);

    private const int WaveMapper = -1; // WAVE_MAPPER: lets Windows convert to the requested format

    private readonly object _gate = new();
    private IWaveIn? _capture;
    private WaveFormat _captureFormat = RecordingFormat;
    private bool _captureIsFloat;
    private List<float> _samples = new(); // mono, at _captureFormat.SampleRate
    private int _dataCallbacks;

    /// <summary>Called with a normalized audio level (0…1) for each captured buffer.</summary>
    public Action<double>? OnLevel;

    public bool IsRecording => _capture != null;

    /// <summary>
    /// Logs whether the Windows Media Foundation FLAC encoder is present, without needing a
    /// recording. Call once at startup — if this logs 0 types, that's the whole problem
    /// (typical on Windows N / Server editions that lack the Media Feature Pack).
    /// </summary>
    public static void LogFlacAvailability()
    {
        try
        {
            MediaFoundationApi.Startup();
            var candidates = MediaFoundationEncoder.GetOutputMediaTypes(MFAudioFormat_FLAC);
            Logger.Log($"AudioRecorder: MF FLAC encoder check — {candidates.Length} output media type(s) available.");
        }
        catch (Exception ex)
        {
            Logger.Log("AudioRecorder: MF FLAC availability check threw", ex);
        }
    }

    /// <summary>Opens the mic and starts capturing. Returns false if no capture method worked.</summary>
    public bool StartRecording()
    {
        if (_capture != null) return true;

        lock (_gate)
        {
            _samples = new List<float>(16000 * 30);
            _dataCallbacks = 0;
        }

        if (TryStart(CreateWasapi, "WASAPI") || TryStart(CreateWaveIn, "waveIn (format mapper)"))
            return true;

        Logger.Log("AudioRecorder: no capture method could open the microphone. Check Settings → Privacy → Microphone, and that an input device is enabled.");
        return false;
    }

    private static IWaveIn CreateWasapi() => new WasapiCapture();

    private static IWaveIn CreateWaveIn() => new WaveInEvent
    {
        DeviceNumber = WaveMapper,
        WaveFormat = RecordingFormat,
        BufferMilliseconds = 33,
        NumberOfBuffers = 3,
    };

    private bool TryStart(Func<IWaveIn> create, string label)
    {
        IWaveIn? capture = null;
        try
        {
            capture = create();
            _captureFormat = capture.WaveFormat;
            _captureIsFloat = IsFloat(_captureFormat);
            capture.DataAvailable += OnDataAvailable;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();
            _capture = capture;
            Logger.Log($"AudioRecorder: recording started via {label} ({_captureFormat.SampleRate} Hz, {_captureFormat.BitsPerSample}-bit{(_captureIsFloat ? " float" : "")}, {_captureFormat.Channels} ch).");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"AudioRecorder: {label} failed to start", ex);
            if (capture != null)
            {
                capture.DataAvailable -= OnDataAvailable;
                capture.RecordingStopped -= OnRecordingStopped;
                try { capture.Dispose(); } catch { /* ignore */ }
            }
            return false;
        }
    }

    /// <summary>Stops capture and returns FLAC-encoded audio, or null if nothing was captured.</summary>
    public byte[]? StopRecording()
    {
        var capture = _capture;
        if (capture == null) return null;
        _capture = null;

        capture.DataAvailable -= OnDataAvailable;
        capture.RecordingStopped -= OnRecordingStopped;
        try { capture.StopRecording(); } catch { /* ignore */ }
        try { capture.Dispose(); } catch { /* ignore */ }

        float[] mono;
        int callbacks;
        lock (_gate)
        {
            mono = _samples.ToArray();
            callbacks = _dataCallbacks;
            _samples = new List<float>();
        }

        Logger.Log($"AudioRecorder: stopped — {callbacks} data callbacks, {mono.Length} samples at {_captureFormat.SampleRate} Hz.");

        if (mono.Length == 0)
        {
            Logger.Log("AudioRecorder: no audio captured.");
            return null;
        }

        try
        {
            var pcm = ToPcm16kMono(mono, _captureFormat.SampleRate);
            var flac = EncodeToFlac(pcm, RecordingFormat);
            Logger.Log($"AudioRecorder: FLAC encode OK — {pcm.Length} PCM bytes -> {flac.Length} FLAC bytes.");
            return flac;
        }
        catch (Exception ex)
        {
            Logger.Log("AudioRecorder: FLAC encode failed", ex);
            return null;
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        // Only fires mid-recording if capture dies (e.g. the device was unplugged).
        if (e.Exception != null) Logger.Log("AudioRecorder: capture stopped unexpectedly", e.Exception);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        int channels = _captureFormat.Channels;
        int bytesPerSample = _captureFormat.BitsPerSample / 8;
        int frameSize = bytesPerSample * channels;
        if (frameSize == 0) return;

        // Downmix to mono, buffer it, and compute RMS for the level meter.
        double sumSquares = 0;
        int frames = 0;
        lock (_gate)
        {
            _dataCallbacks++;
            for (int i = 0; i + frameSize <= e.BytesRecorded; i += frameSize)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                    sum += ReadSample(e.Buffer, i + c * bytesPerSample, bytesPerSample);
                float s = sum / channels;
                _samples.Add(s);
                sumSquares += s * s;
                frames++;
            }
        }

        // RMS -> dB, normalize -40…0 dB -> 0…1 (mirrors macOS averagePower normalization).
        if (frames > 0 && OnLevel != null)
        {
            double rms = Math.Sqrt(sumSquares / frames);
            double db = rms > 0 ? 20.0 * Math.Log10(rms) : -160.0;
            double clamped = Math.Max(-40.0, Math.Min(db, 0.0));
            OnLevel((clamped + 40.0) / 40.0);
        }
    }

    private float ReadSample(byte[] b, int offset, int bytesPerSample)
    {
        if (_captureIsFloat) return BitConverter.ToSingle(b, offset);
        return bytesPerSample switch
        {
            2 => BitConverter.ToInt16(b, offset) / 32768f,
            3 => ((b[offset] << 8) | (b[offset + 1] << 16) | (b[offset + 2] << 24)) / 2147483648f,
            4 => BitConverter.ToInt32(b, offset) / 2147483648f,
            _ => 0f,
        };
    }

    private static bool IsFloat(WaveFormat f) =>
        f.Encoding == WaveFormatEncoding.IeeeFloat
        || (f is WaveFormatExtensible ext && ext.SubFormat == SubtypeIeeeFloat);

    /// <summary>Resamples mono float audio to 16 kHz and converts it to 16-bit PCM.</summary>
    private static byte[] ToPcm16kMono(float[] mono, int sampleRate)
    {
        var floatBytes = new byte[mono.Length * sizeof(float)];
        Buffer.BlockCopy(mono, 0, floatBytes, 0, floatBytes.Length);

        ISampleProvider source = new RawSourceWaveStream(
            new MemoryStream(floatBytes), WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)).ToSampleProvider();
        if (sampleRate != RecordingFormat.SampleRate)
            source = new WdlResamplingSampleProvider(source, RecordingFormat.SampleRate);

        var pcm16 = new SampleToWaveProvider16(source);
        using var output = new MemoryStream();
        var buffer = new byte[RecordingFormat.AverageBytesPerSecond];
        int read;
        while ((read = pcm16.Read(buffer, 0, buffer.Length)) > 0)
            output.Write(buffer, 0, read);
        return output.ToArray();
    }

    // ---- FLAC encoding via Media Foundation ----

    private static byte[] EncodeToFlac(byte[] pcm, WaveFormat format)
    {
        Logger.Log("AudioRecorder: FLAC encode — calling MediaFoundationApi.Startup().");
        MediaFoundationApi.Startup();

        // Try output media types in order, best first. The MF FLAC encoder throws 0xC00D36B4
        // (MF_E_INVALIDMEDIATYPE) at SetInputMediaType when the chosen output type's sample
        // rate/channels don't match the PCM input (FLAC can't resample). The reliable fix is a
        // hand-built output type that mirrors the input exactly; enumerated types are a fallback.
        Exception? lastError = null;
        foreach (var (label, mediaType) in CandidateFlacOutputTypes(format))
        {
            try
            {
                var bytes = EncodeWith(mediaType, pcm, format);
                Logger.Log($"AudioRecorder: FLAC encode — succeeded via {label}.");
                return bytes;
            }
            catch (Exception ex)
            {
                Logger.Log($"AudioRecorder: FLAC encode — {label} failed: {ex.Message}");
                lastError = ex;
            }
        }
        throw lastError ?? new InvalidOperationException("No FLAC output media type available.");
    }

    private static byte[] EncodeWith(MediaType mediaType, byte[] pcm, WaveFormat format)
    {
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

    /// <summary>
    /// Output media types to attempt, best first: a hand-built FLAC type that mirrors the input
    /// format exactly (so sample rate/channels match), then any enumerated FLAC types as fallback.
    /// </summary>
    private static IEnumerable<(string Label, MediaType MediaType)> CandidateFlacOutputTypes(WaveFormat format)
    {
        // Build a FLAC output type from the input WaveFormat (carries the correct major type,
        // sample rate, channels, bits) and just flip the subtype PCM -> FLAC.
        MediaType? manual = null;
        try
        {
            manual = new MediaType(format) { SubType = MFAudioFormat_FLAC };
        }
        catch (Exception ex)
        {
            Logger.Log("AudioRecorder: building manual FLAC media type failed", ex);
        }
        if (manual != null) yield return ("manual media type", manual);

        MediaType[] enumerated;
        try
        {
            enumerated = MediaFoundationEncoder.GetOutputMediaTypes(MFAudioFormat_FLAC);
        }
        catch (Exception ex)
        {
            Logger.Log("AudioRecorder: GetOutputMediaTypes(FLAC) failed", ex);
            enumerated = Array.Empty<MediaType>();
        }
        Logger.Log($"AudioRecorder: FLAC encode — {enumerated.Length} enumerated output type(s) as fallback.");
        foreach (var mt in enumerated)
        {
            yield return ("enumerated type", mt);
        }
    }
}
