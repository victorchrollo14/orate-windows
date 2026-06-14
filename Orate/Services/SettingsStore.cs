using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Orate.Services;

public enum AIProvider
{
    OrateCloud,
    GoogleAI,
    VertexAI,
}

public static class AIProviderExtensions
{
    public static string DisplayName(this AIProvider p) => p switch
    {
        AIProvider.OrateCloud => "Orate Cloud",
        AIProvider.GoogleAI => "Google AI Studio",
        AIProvider.VertexAI => "Vertex AI",
        _ => p.ToString(),
    };
}

/// <summary>Plain settings model serialized to JSON. The Windows analog of macOS UserDefaults.</summary>
public sealed class Settings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AIProvider Provider { get; set; } = AIProvider.OrateCloud;

    public string? VertexProjectId { get; set; }
    public string VertexRegion { get; set; } = "us-central1";

    public string CustomInstructions { get; set; } = "";
    public List<string> VocabularyWords { get; set; } = new();

    /// <summary>Push-to-talk virtual-key code. Default = Right Alt (VK_RMENU 0xA5).</summary>
    public int PushToTalkVk { get; set; } = 0xA5;

    public bool HasCompletedOnboarding { get; set; }
}

/// <summary>Loads/persists <see cref="Settings"/> at %APPDATA%\Orate\settings.json.</summary>
public static class SettingsStore
{
    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Orate");

    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static Settings? _current;

    public static Settings Current => _current ??= Load();

    private static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                return JsonSerializer.Deserialize<Settings>(json, JsonOptions) ?? new Settings();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex}");
        }
        return new Settings();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex}");
        }
    }
}
