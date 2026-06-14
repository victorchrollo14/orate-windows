using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orate.Services;

/// <summary>One saved transcription: the cleaned text plus metadata. Mirrors macOS RecordingMetadata.</summary>
public sealed class RecordingMetadata
{
    public string Id { get; set; } = "";
    public DateTime Timestamp { get; set; }
    public string Transcript { get; set; } = "";
    public long LatencyMs { get; set; }
    public string AudioFile { get; set; } = "";
    public long AudioSizeBytes { get; set; }
    public string Model { get; set; } = "";

    [JsonIgnore]
    public string AudioPath => Path.Combine(RecordingStore.Dir, AudioFile);
}

/// <summary>
/// Persists recordings to %APPDATA%\Orate\recordings as a .flac audio file plus a .json
/// metadata sidecar. The Windows analog of macOS RecordingStore.
/// </summary>
public static class RecordingStore
{
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orate", "recordings");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(byte[] audioData, TranscriptionResult result)
    {
        try
        {
            Directory.CreateDirectory(Dir);

            var id = Guid.NewGuid().ToString("N");
            var prefix = $"{DateTime.Now:yyyyMMdd-HHmmss}-{id[..8]}";
            var audioFileName = $"{prefix}.flac";

            File.WriteAllBytes(Path.Combine(Dir, audioFileName), audioData);

            var metadata = new RecordingMetadata
            {
                Id = id,
                Timestamp = DateTime.Now,
                Transcript = result.Transcript,
                LatencyMs = result.LatencyMs,
                AudioFile = audioFileName,
                AudioSizeBytes = audioData.Length,
                Model = result.Model,
            };

            File.WriteAllText(Path.Combine(Dir, $"{prefix}.json"), JsonSerializer.Serialize(metadata, JsonOptions));
        }
        catch (Exception ex)
        {
            Logger.Log("RecordingStore: save failed", ex);
        }
    }

    public static List<RecordingMetadata> LoadAll()
    {
        var list = new List<RecordingMetadata>();
        if (!Directory.Exists(Dir)) return list;

        foreach (var file in Directory.EnumerateFiles(Dir, "*.json"))
        {
            try
            {
                var meta = JsonSerializer.Deserialize<RecordingMetadata>(File.ReadAllText(file));
                if (meta != null) list.Add(meta);
            }
            catch (Exception ex)
            {
                Logger.Log($"RecordingStore: failed to read {Path.GetFileName(file)}", ex);
            }
        }

        list.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return list;
    }

    public static void Delete(RecordingMetadata recording)
    {
        TryDelete(recording.AudioPath);
        TryDelete(Path.Combine(Dir, Path.ChangeExtension(recording.AudioFile, ".json")));
    }

    public static void Clear(int? olderThanDays)
    {
        var cutoff = olderThanDays.HasValue ? DateTime.Now.AddDays(-olderThanDays.Value) : (DateTime?)null;
        foreach (var rec in LoadAll())
        {
            if (cutoff == null || rec.Timestamp < cutoff) Delete(rec);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { Logger.Log($"RecordingStore: delete failed {path}", ex); }
    }
}
