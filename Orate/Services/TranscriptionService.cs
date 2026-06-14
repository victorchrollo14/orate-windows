using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Orate.Services;

public sealed record TranscriptionResult(
    string Transcript,
    long LatencyMs,
    string Model,
    int? WordsRemaining = null);

public sealed class TranscriptionException : Exception
{
    public TranscriptionException(string message) : base(message) { }
}

/// <summary>
/// Sends FLAC audio to one of three backends (Orate Cloud / Google AI / Vertex) and returns
/// the cleaned transcript. Direct port of macOS TranscriptionService.swift.
/// </summary>
public static class TranscriptionService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    private const string OrateCloudBaseUrl = "https://orate-api.lavisht22.workers.dev";
    public const string Model = "gemini-3.1-flash-lite-preview";

    private const string SystemPrompt =
        """
        You are Orate, an intelligent speech-to-text assistant. Your job is to transcribe spoken audio and produce clean, polished text ready to be inserted directly into whatever the user is typing.

        Core rules:
        - Transcribe the spoken content accurately, preserving the speaker's intended meaning.
        - Clean up speech disfluencies: remove filler words (um, uh, like, you know), false starts, and repeated words — unless they are clearly intentional for emphasis.
        - Fix grammar and punctuation naturally. Add proper capitalization, periods, commas, and other punctuation as appropriate for written text.
        - Do NOT add any preamble, commentary, labels, or formatting beyond the transcription itself. Output ONLY the final clean text.
        - Do NOT wrap the output in quotes or add "Transcription:" or similar prefixes.
        - If the speaker dictates punctuation explicitly (e.g. says "period", "comma", "new line", "question mark"), convert those to the actual punctuation characters.
        - CRITICAL: If the audio contains no spoken words (silence, background noise, breathing, typing, or other non-speech sounds), you MUST output an empty string. Do not generate any text whatsoever — not even from vocabulary hints or custom instructions. Only transcribe actual spoken words.
        - Preserve the speaker's tone and intent: if they are writing a casual message, keep it casual. If formal, keep it formal.
        - For numbers, use digits for quantities and measurements (e.g. "5 minutes", "200 users") and words for conversational usage (e.g. "a couple of things").
        - If the speaker is dictating a list (e.g. "first... second... third..." or "number one... number two..." or "bullet point..."), format the output as a properly structured list with line breaks and markers (1. 2. 3. or - bullets) as appropriate.
        """;

    private static string? ApiKey(AIProvider provider) => provider switch
    {
        AIProvider.OrateCloud => CredentialStore.Read("orateCloudAPIKey"),
        AIProvider.GoogleAI => CredentialStore.Read("geminiAPIKey"),
        AIProvider.VertexAI => CredentialStore.Read("vertexAPIKey"),
        _ => null,
    };

    private static string BuildSystemInstruction()
    {
        var settings = SettingsStore.Current;
        var sb = new StringBuilder(SystemPrompt);

        var vocab = settings.VocabularyWords;
        if (vocab.Count > 0)
        {
            sb.Append("\n\nVocabulary — the user has registered these custom words. When you hear something that sounds like one of these words, use the exact spelling provided here:\n");
            sb.Append(string.Join("\n", vocab.Select(w => $"- {w}")));
            sb.Append("\nIMPORTANT: These vocabulary words are spelling hints ONLY. Do not use them to generate or infer content. If no speech is present in the audio, output an empty string regardless of these words.");
        }

        var custom = settings.CustomInstructions;
        if (!string.IsNullOrWhiteSpace(custom))
        {
            sb.Append("\n\nUser's custom instructions:\n").Append(custom);
        }

        return sb.ToString();
    }

    public static async Task<TranscriptionResult> TranscribeAsync(byte[] audioData, CancellationToken ct = default)
    {
        var provider = SettingsStore.Current.Provider;
        var apiKey = ApiKey(provider);
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new TranscriptionException("No API key configured. Open Settings to add your API key.");
        }

        var base64Audio = Convert.ToBase64String(audioData);

        return provider == AIProvider.OrateCloud
            ? await TranscribeOrateCloudAsync(apiKey, base64Audio, ct)
            : await TranscribeGeminiAsync(provider, apiKey, base64Audio, ct);
    }

    private static async Task<TranscriptionResult> TranscribeOrateCloudAsync(string apiKey, string base64Audio, CancellationToken ct)
    {
        var payload = new
        {
            audio = base64Audio,
            system_prompt = BuildSystemInstruction(),
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{OrateCloudBaseUrl}/transcribe")
        {
            Content = JsonContent(payload),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var sw = Stopwatch.StartNew();
        using var response = await Http.SendAsync(request, ct);
        sw.Stop();

        var body = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (root.TryGetProperty("error", out var errorEl) && errorEl.ValueKind == JsonValueKind.String)
        {
            var error = errorEl.GetString();
            if (error == "insufficient_balance")
            {
                throw new TranscriptionException("Insufficient word balance. Please recharge your Orate Cloud API key.");
            }
            throw new TranscriptionException($"API error ({(int)response.StatusCode}): {error}");
        }

        if (!response.IsSuccessStatusCode || !root.TryGetProperty("text", out var textEl))
        {
            throw new TranscriptionException($"API error ({(int)response.StatusCode}): {body}");
        }

        var text = textEl.GetString()?.Trim() ?? "";
        int? wordsRemaining = root.TryGetProperty("words_remaining", out var wr) && wr.TryGetInt32(out var wrv) ? wrv : null;

        return new TranscriptionResult(text, sw.ElapsedMilliseconds, Model, wordsRemaining);
    }

    private static async Task<TranscriptionResult> TranscribeGeminiAsync(AIProvider provider, string apiKey, string base64Audio, CancellationToken ct)
    {
        var payload = new
        {
            system_instruction = new { parts = new[] { new { text = BuildSystemInstruction() } } },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = "audio/flac", data = base64Audio } },
                    },
                },
            },
        };

        var url = BuildGeminiUrl(provider, apiKey);

        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent(payload) };

        var sw = Stopwatch.StartNew();
        using var response = await Http.SendAsync(request, ct);
        sw.Stop();

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new TranscriptionException($"API error ({(int)response.StatusCode}): {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var text = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString()?.Trim() ?? "";

        return new TranscriptionResult(text, sw.ElapsedMilliseconds, Model);
    }

    private static string BuildGeminiUrl(AIProvider provider, string apiKey)
    {
        if (provider == AIProvider.GoogleAI)
        {
            return $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent?key={apiKey}";
        }

        // Vertex AI
        var settings = SettingsStore.Current;
        if (string.IsNullOrEmpty(settings.VertexProjectId))
        {
            throw new TranscriptionException("Vertex AI requires a Project ID. Open Settings to configure it.");
        }
        var region = string.IsNullOrEmpty(settings.VertexRegion) ? "us-central1" : settings.VertexRegion;
        var host = region == "global" ? "aiplatform.googleapis.com" : $"{region}-aiplatform.googleapis.com";
        return $"https://{host}/v1/projects/{settings.VertexProjectId}/locations/{region}/publishers/google/models/{Model}:generateContent?key={apiKey}";
    }

    private static StringContent JsonContent(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
