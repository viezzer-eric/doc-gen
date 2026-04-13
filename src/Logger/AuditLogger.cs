using System.Text.Json;
using DocGen.Models;

namespace DocGen.Logger;

public class AuditLogger
{
    private const string LogFile = ".doc_gen_log.json";
    private const int MaxEntries = 500;

    private readonly string _logPath;

    public AuditLogger(string repoPath)
    {
        _logPath = Path.Combine(Path.GetFullPath(repoPath), LogFile);
    }

    public async Task LogAsync(LogEntry entry)
    {
        var entries = await LoadAsync();
        entries.Insert(0, entry);

        // Rotate if over limit
        if (entries.Count > MaxEntries)
            entries = entries[..MaxEntries];

        var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_logPath, json);
    }

    public async Task<List<LogEntry>> LoadAsync()
    {
        if (!File.Exists(_logPath)) return [];

        try
        {
            var json = await File.ReadAllTextAsync(_logPath);
            return JsonSerializer.Deserialize<List<LogEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static LogEntry BuildEntry(
        string hashBefore,
        string hashAfter,
        string result,
        SynthesisResult? synthesis = null,
        string? error = null) => new()
    {
        Timestamp = DateTime.UtcNow.ToString("o"),
        HashBefore = hashBefore,
        HashAfter = hashAfter,
        Result = result,
        TokensUsed = synthesis?.TokensUsed ?? 0,
        CommitsProcessed = 0,
        BreakingChanges = synthesis?.BreakingChanges.Count ?? 0,
        NewFeatures = synthesis?.NewFeatures.Count ?? 0,
        Refactors = synthesis?.Refactors.Count ?? 0,
        Error = error
    };
}
