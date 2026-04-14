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

// ─── Factory com detecção automática ──────────────────────────────────────────

public static class PromptEngineFactory
{
    public static IPromptEngine Create(string provider) => provider.ToLower() switch
    {
        "anthropic" => new AnthropicEngine(),
        "gemini" => new GeminiEngine(),
        "openai" => new OpenAIEngine(),
        "ollama" => new OllamaEngine(),
        "auto" => DetectAvailableProvider(),
        _ => throw new InvalidOperationException(
            $"Provider '{provider}' desconhecido.\n" +
            "Use: anthropic | gemini | openai | ollama | auto\n\n" +
            "Variáveis de ambiente suportadas:\n" +
            "  ANTHROPIC_API_KEY     (Claude)\n" +
            "  GEMINI_API_KEY        (Gemini)\n" +
            "  OPENAI_API_KEY        (GPT-4/3.5)\n" +
            "  OLLAMA_HOST           (Local)")
    };

    private static IPromptEngine DetectAvailableProvider()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")))
            return new AnthropicEngine();

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY")))
            return new OpenAIEngine();

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
            return new GeminiEngine();

        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OLLAMA_HOST")))
            return new OllamaEngine();

        throw new InvalidOperationException(
            "❌ Nenhum provider configurado.\n\n" +
            "Configure uma dessas variáveis de ambiente:\n\n" +
            "Anthropic (recomendado):\n" +
            "  export ANTHROPIC_API_KEY=sk-ant-...\n" +
            "  https://console.anthropic.com\n\n" +
            "Google Gemini (gratuito):\n" +
            "  export GEMINI_API_KEY=AIza...\n" +
            "  https://aistudio.google.com/app/apikey\n\n" +
            "OpenAI:\n" +
            "  export OPENAI_API_KEY=sk-...\n" +
            "  https://platform.openai.com/api-keys\n\n" +
            "Ollama (local, sem API):\n" +
            "  export OLLAMA_HOST=http://localhost:11434\n" +
            "  https://ollama.ai");
    }
}

// ─── Response Parser comum ────────────────────────────────────────────────────

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

// ─── Anthropic Claude ─────────────────────────────────────────────────────────

public class AnthropicEngine : IPromptEngine
{
    private readonly HttpClient _http;
    private const string Model = "claude-sonnet-4-20250514";
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";

    public AnthropicEngine()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY")
            ?? throw new InvalidOperationException(
                "ANTHROPIC_API_KEY não definida.\n" +
                "Obtenha em: https://console.anthropic.com\n" +
                "Defina com: export ANTHROPIC_API_KEY=sk-ant-...");

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

// ─── Google Gemini ────────────────────────────────────────────────────────────

public class GeminiEngine : IPromptEngine
{
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private const string Model = "gemini-2.5-flash";
    private const string ApiUrl =
        "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent?key={1}";

    public GeminiEngine()
    {
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? throw new InvalidOperationException(
                "GEMINI_API_KEY não definida.\n" +
                "Obtenha grátis em: https://aistudio.google.com/app/apikey\n" +
                "Defina com: export GEMINI_API_KEY=AIza...");

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

// ─── OpenAI GPT ───────────────────────────────────────────────────────────────

public class OpenAIEngine : IPromptEngine
{
    private readonly HttpClient _http;
    private const string Model = "gpt-4-turbo";
    private const string ApiUrl = "https://api.openai.com/v1/chat/completions";

    public OpenAIEngine()
    {
        var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? throw new InvalidOperationException(
                "OPENAI_API_KEY não definida.\n" +
                "Obtenha em: https://platform.openai.com/api-keys\n" +
                "Defina com: export OPENAI_API_KEY=sk-...");

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
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
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = ResponseParser.SystemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        try
        {
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(ApiUrl, body);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, "", 0, $"OpenAI {(int)resp.StatusCode}: {raw[..Math.Min(300, raw.Length)]}");

            var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? "";

            var tokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var u))
                tokens = (u.TryGetProperty("prompt_tokens", out var p) ? p.GetInt32() : 0)
                       + (u.TryGetProperty("completion_tokens", out var c) ? c.GetInt32() : 0);

            return (true, text, tokens, null);
        }
        catch (TaskCanceledException) { return (false, "", 0, "Timeout (>3min)."); }
        catch (Exception ex) { return (false, "", 0, ex.Message); }
    }
}

// ─── Ollama (Local) ───────────────────────────────────────────────────────────

public class OllamaEngine : IPromptEngine
{
    private readonly HttpClient _http;
    private readonly string _host;
    private const string Model = "mistral"; // ou "neural-chat", "llama2"

    public OllamaEngine()
    {
        _host = Environment.GetEnvironmentVariable("OLLAMA_HOST")
            ?? throw new InvalidOperationException(
                "OLLAMA_HOST não definido.\n" +
                "Instale: https://ollama.ai\n" +
                "Execute: ollama serve\n" +
                "Defina com: export OLLAMA_HOST=http://localhost:11434");

        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
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
        var fullPrompt = ResponseParser.SystemPrompt + "\n\n" + userMessage;

        var payload = new
        {
            model = Model,
            prompt = fullPrompt,
            stream = false,
            temperature = 0.2
        };

        try
        {
            var url = _host.TrimEnd('/') + "/api/generate";
            var body = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var resp = await _http.PostAsync(url, body);
            var raw = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
                return (false, "", 0, $"Ollama {(int)resp.StatusCode}: {raw[..Math.Min(300, raw.Length)]}");

            var doc = JsonDocument.Parse(raw);
            var text = doc.RootElement.GetProperty("response").GetString() ?? "";

            return (true, text, 0, null); // Ollama não retorna token count
        }
        catch (TaskCanceledException) { return (false, "", 0, "Timeout (>5min)."); }
        catch (Exception ex) { return (false, "", 0, $"Ollama indisponível: {ex.Message}"); }
    }
}