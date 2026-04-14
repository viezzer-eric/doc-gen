namespace DocGen.Models;

// ─── State ────────────────────────────────────────────────────────────────────

public record DocState
{
    public string LastRun { get; init; } = "";
    public string SnapshotHash { get; init; } = "";
    public string LastCommitSha { get; init; } = "";
    public long TokensUsedTotal { get; init; } = 0;
    public int RunCount { get; init; } = 0;
}

// ─── Delta ────────────────────────────────────────────────────────────────────

public record RepoDelta
{
    public bool HasChanges { get; init; }
    public string CurrentHash { get; init; } = "";
    public string FromCommit { get; init; } = "";
    public string ToCommit { get; init; } = "";
    public List<CommitInfo> Commits { get; init; } = [];
    public string DiffSummary { get; init; } = "";
    public int FilesChanged { get; init; }
}

public record CommitInfo
{
    public string Sha { get; init; } = "";
    public string Message { get; init; } = "";
    public string Author { get; init; } = "";
    public string Date { get; init; } = "";
}

// ─── Context ──────────────────────────────────────────────────────────────────

public record AggregatedContext
{
    public string CurrentDocumentFull { get; init; } = "";
    public string AutoSections { get; init; } = "";
    public string ManualSections { get; init; } = "";
    public RepoDelta Delta { get; init; } = new();
}

// ─── AI Result ────────────────────────────────────────────────────────────────

public record SynthesisResult
{
    public bool Success { get; init; }
    public string UpdatedAutoContent { get; init; } = "";
    public List<string> BreakingChanges { get; init; } = [];
    public List<string> NewFeatures { get; init; } = [];
    public List<string> Refactors { get; init; } = [];
    public int TokensUsed { get; init; }
    public string? Error { get; init; }
}

// ─── Log Entry ────────────────────────────────────────────────────────────────

public record LogEntry
{
    public string Timestamp { get; init; } = "";
    public string HashBefore { get; init; } = "";
    public string HashAfter { get; init; } = "";
    public string Result { get; init; } = "";  // success | skipped | error
    public int TokensUsed { get; init; }
    public int CommitsProcessed { get; init; }
    public int BreakingChanges { get; init; }
    public int NewFeatures { get; init; }
    public int Refactors { get; init; }
    public string? Error { get; init; }
}

// ─── CLI Options ──────────────────────────────────────────────────────────────

public record UpdateOptions
{
    public string RepoPath { get; init; } = ".";
    public bool DryRun { get; init; }
    public bool JsonOutput { get; init; }
    public bool AutoCommit { get; init; }
    public bool Verbose { get; init; }
}