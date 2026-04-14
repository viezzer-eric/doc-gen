using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocGen.Models;

namespace DocGen.PromptEngine;

// ─── Interface comum ──────────────────────────────────────────────────────────

public interface IPromptEngine
{
    Task<SynthesisResult> GenerateInitialDocAsync(string prompt);
    Task<SynthesisResult> SynthesizeUpdateAsync(string prompt);
}

// ─── Factory ──────────────────────────────────────────────────────────────────

public static class PromptEngineFactory
{
    public static IPromptEngine Create(string provider) => provider.ToLower() switch
    {
        "anthropic" => new AnthropicEngine(),
        "gemini" => new GeminiEngine(),
        _ => throw new InvalidOperationException(
            $"Provider '{provider}' desconhecido. Use: anthropic | gemini")
    };
}

// ─── Shared helpers ───────────────────────────────────────────────────────────

internal static class ResponseParser
{
    internal static SynthesisResult Parse(string raw, int tokens)
    {
        const string separator = "---CHANGES---";
        var sepIdx = raw.IndexOf(separator, StringComparison.Ordinal);

        string autoContent;
        var breaking = new List<string>();
        var features = new List<string>();
        var refactors = new List<string>();

        if (sepIdx >= 0)
        {
            autoContent = raw[..sepIdx].Trim();
            var changesRaw = raw[(sepIdx + separator.Length)..].Trim();
            try
            {
                var json = JsonNode.Parse(changesRaw);
                breaking = ReadList(json?["breaking_changes"]);
                features = ReadList(json?["new_features"]);
                refactors = ReadList(json?["refactors"]);
            }
            catch { /* JSON inválido — conteúdo ainda válido */ }
        }
        else
        {
            autoContent = raw.Trim();
        }

        return new SynthesisResult
        {
            Success = true,
            UpdatedAutoContent = autoContent,
            BreakingChanges = breaking,
            NewFeatures = features,
            Refactors = refactors,
            TokensUsed = tokens
        };
    }

    private static List<string> ReadList(JsonNode? node)
    {
        if (node is not JsonArray arr) return [];
        return arr.Select(i => i?.GetValue<string>() ?? "")
                  .Where(s => !string.IsNullOrEmpty(s))
                  .ToList();
    }

    internal static string SystemPrompt => """
        Você é um CTO sênior altamente experiente.
        Sua especialidade é documentar arquitetura de software de forma clara, técnica e objetiva.
        Você analisa código, commits e diffs com olhar estratégico — identificando padrões,
        decisões arquiteturais e impactos de mudanças.
        Seja direto e preciso. Evite generalidades. Use terminologia técnica adequada.
        Foque no que realmente importa para um desenvolvedor que está chegando no projeto.
        """;
}

// ─── Anthropic ────────────────────────────────────────────────────────────────

public class AnthropicEngine : IPromptEngine
{
    private readonly HttpClient _http;
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    public AnthropicEngine()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "ANTHROPIC_API_KEY não definida.\nDefina com: $env:ANTHROPIC_API_KEY=\"sk-ant-...\"");

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("x-api-key", apiKey);
        _http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        _http.Timeout = TimeSpan.FromMinutes(3);
    }

    public async Task<SynthesisResult> GenerateInitialDocAsync(string prompt)
    {
        var r = await CallAsync(prompt, 4000);
        return r.Success
            ? new SynthesisResult { Success = true, UpdatedAutoContent = r.Content, TokensUsed = r.Tokens }
            : new SynthesisResult { Success = false, Error = r.Error };
    }

    public async Task<SynthesisResult> SynthesizeUpdateAsync(string prompt)
    {
        var r = await CallAsync(prompt, 4000);
        return r.Success ? ResponseParser.Parse(r.Content, r.Tokens)
                         : new SynthesisResult { Success = false, Error = r.Error };
    }

    private async Task<(bool Success, string Content, int Tokens, string? Error)> CallAsync(
        string userMessage, int maxTokens)
    {
        var payload = new
        {
            model = Model,
            max_tokens = maxTokens,
            system = ResponseParser.SystemPrompt,
            messages = new[] { new { role = "user", content = userMessage } }
        };

        try
        {
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(ApiUrl, body);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, "", 0, $"Anthropic {(int)resp.StatusCode}: {raw[..Math.Min(300, raw.Length)]}");

            var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            var tokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var u))
                tokens = (u.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0)
                       + (u.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0);

            return (true, text, tokens, null);
        }
        catch (TaskCanceledException) { return (false, "", 0, "Timeout (>3min)."); }
        catch (Exception ex) { return (false, "", 0, ex.Message); }
    }
}

// ─── Gemini ───────────────────────────────────────────────────────────────────

public class GeminiEngine : IPromptEngine
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    // gemini-1.5-flash → gratuito (60 req/min, 1M tokens/dia)
    private const string Model = "gemini-3.5-flash";
    private const string ApiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

    public GeminiEngine()
    {
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException(
                "GEMINI_API_KEY não definida.\n" +
                "Obtenha grátis em: https://aistudio.google.com/app/apikey\n" +
                "Defina com: $env:GEMINI_API_KEY=\"AIza...\"");

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
    }

    public async Task<SynthesisResult> GenerateInitialDocAsync(string prompt)
    {
        var r = await CallAsync(prompt);
        return r.Success
            ? new SynthesisResult { Success = true, UpdatedAutoContent = r.Content, TokensUsed = r.Tokens }
            : new SynthesisResult { Success = false, Error = r.Error };
    }

    public async Task<SynthesisResult> SynthesizeUpdateAsync(string prompt)
    {
        var r = await CallAsync(prompt);
        return r.Success ? ResponseParser.Parse(r.Content, r.Tokens)
                         : new SynthesisResult { Success = false, Error = r.Error };
    }

    private async Task<(bool Success, string Content, int Tokens, string? Error)> CallAsync(string userMessage)
    {
        // Gemini não tem "system" separado — concatenamos no início do conteúdo
        var fullPrompt = ResponseParser.SystemPrompt + "\n\n" + userMessage;

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = fullPrompt } }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 4000,
                temperature = 0.2
            }
        };

        try
        {
            var url = string.Format(ApiUrl, Model, _apiKey);
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(url, body);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, "", 0, $"Gemini {(int)resp.StatusCode}: {raw[..Math.Min(300, raw.Length)]}");

            var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString() ?? "";

            var tokens = 0;
            if (doc.RootElement.TryGetProperty("usageMetadata", out var u))
                tokens = u.TryGetProperty("totalTokenCount", out var t) ? t.GetInt32() : 0;

            return (true, text, tokens, null);
        }
        catch (TaskCanceledException) { return (false, "", 0, "Timeout (>3min)."); }
        catch (Exception ex) { return (false, "", 0, ex.Message); }
    }
}