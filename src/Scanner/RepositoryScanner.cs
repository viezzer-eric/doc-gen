using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DocGen.Models;
using LibGit2Sharp;

namespace DocGen.Scanner;

public class RepositoryScanner
{
    private readonly string _repoPath;
    private readonly HashSet<string> _ignorePatterns;
    private const string StateFile = ".doc_state.json";
    private const string IgnoreFile = ".docignore";

    public RepositoryScanner(string repoPath)
    {
        _repoPath = Path.GetFullPath(repoPath);
        _ignorePatterns = LoadIgnorePatterns();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    public async Task<RepoDelta> ComputeDeltaAsync()
    {
        var currentHash = ComputeSnapshotHash();
        var state = await LoadStateAsync();

        if (state.SnapshotHash == currentHash)
        {
            Console.WriteLine("→ Nenhuma mudança relevante detectada (hash idêntico).");
            return new RepoDelta { HasChanges = false, CurrentHash = currentHash };
        }

        var delta = ExtractGitDelta(state.LastCommitSha);

        return delta with
        {
            HasChanges = true,
            CurrentHash = currentHash
        };
    }

    public async Task<DocState> LoadStateAsync()
    {
        var statePath = Path.Combine(_repoPath, StateFile);
        if (!File.Exists(statePath))
            return new DocState();

        var json = await File.ReadAllTextAsync(statePath);
        return JsonSerializer.Deserialize<DocState>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DocState();
    }

    public async Task SaveStateAsync(DocState state)
    {
        var statePath = Path.Combine(_repoPath, StateFile);
        var json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(statePath, json);
    }

    // ─── Hash Computation ─────────────────────────────────────────────────────

    private string ComputeSnapshotHash()
    {
        var files = GetRelevantFiles()
            .OrderBy(f => f)
            .ToList();

        using var sha = SHA256.Create();
        var combined = new StringBuilder();

        foreach (var file in files)
        {
            try
            {
                var content = File.ReadAllText(file);
                combined.Append(file.Replace(_repoPath, ""));
                combined.Append(content);
            }
            catch { /* skip unreadable files */ }
        }

        var bytes = Encoding.UTF8.GetBytes(combined.ToString());
        var hash = sha.ComputeHash(bytes);
        return "sha256:" + Convert.ToHexString(hash)[..16].ToLower();
    }

    private IEnumerable<string> GetRelevantFiles()
    {
        return Directory
            .EnumerateFiles(_repoPath, "*", SearchOption.AllDirectories)
            .Where(f => !ShouldIgnore(f))
            .Where(f => IsCodeFile(f));
    }

    // ─── Git Delta ────────────────────────────────────────────────────────────

    private RepoDelta ExtractGitDelta(string fromCommitSha)
    {
        try
        {
            using var repo = new Repository(_repoPath);
            var commits = new List<CommitInfo>();
            var diffLines = new List<string>();

            var head = repo.Head.Tip;
            if (head == null)
                return new RepoDelta { HasChanges = true };

            // Collect commits since last run
            var commitLog = repo.Commits.QueryBy(new CommitFilter
            {
                SortBy = CommitSortStrategies.Topological,
                IncludeReachableFrom = head
            });

            foreach (var commit in commitLog.Take(50))
            {
                if (!string.IsNullOrEmpty(fromCommitSha) && commit.Sha.StartsWith(fromCommitSha))
                    break;

                commits.Add(new CommitInfo
                {
                    Sha = commit.Sha[..8],
                    Message = commit.MessageShort.Trim(),
                    Author = commit.Author.Name,
                    Date = commit.Author.When.ToString("yyyy-MM-dd")
                });
            }

            // Build diff summary
            if (commits.Count > 0 && !string.IsNullOrEmpty(fromCommitSha))
            {
                try
                {
                    var fromCommit = repo.Lookup<Commit>(fromCommitSha);
                    if (fromCommit != null)
                    {
                        var diff = repo.Diff.Compare<Patch>(fromCommit.Tree, head.Tree);
                        foreach (var entry in diff.Take(20))
                        {
                            diffLines.Add($"[{entry.Status}] {entry.Path}");
                            if (!string.IsNullOrEmpty(entry.Patch))
                            {
                                var patchLines = entry.Patch.Split('\n')
                                    .Where(l => l.StartsWith('+') || l.StartsWith('-'))
                                    .Where(l => !l.StartsWith("+++") && !l.StartsWith("---"))
                                    .Take(15);
                                diffLines.AddRange(patchLines);
                            }
                        }
                    }
                }
                catch { /* diff unavailable, proceed without it */ }
            }

            return new RepoDelta
            {
                HasChanges = true,
                FromCommit = fromCommitSha,
                ToCommit = head.Sha[..8],
                Commits = commits,
                DiffSummary = string.Join("\n", diffLines),
                FilesChanged = commits.Count > 0 ? diffLines.Count(l => l.StartsWith("[")) : 0
            };
        }
        catch (RepositoryNotFoundException)
        {
            // Not a git repo — still proceed with hash change detection
            return new RepoDelta
            {
                HasChanges = true,
                DiffSummary = "(repositório git não encontrado — análise de arquivos apenas)"
            };
        }
    }

    // ─── .docignore ───────────────────────────────────────────────────────────

    private HashSet<string> LoadIgnorePatterns()
    {
        var defaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "node_modules", "bin", "obj", ".git", ".vs", ".idea",
            "dist", "build", "out", "coverage", "__pycache__",
            ".doc_state.json", ".doc_gen_log.json", "ARCHITECTURE_MEMORY.md"
        };

        var ignorePath = Path.Combine(_repoPath, IgnoreFile);
        if (!File.Exists(ignorePath)) return defaults;

        foreach (var line in File.ReadAllLines(ignorePath))
        {
            var trimmed = line.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !trimmed.StartsWith('#'))
                defaults.Add(trimmed.Trim('/'));
        }

        return defaults;
    }

    private bool ShouldIgnore(string filePath)
    {
        var relative = filePath.Replace(_repoPath, "").TrimStart(Path.DirectorySeparatorChar);
        return _ignorePatterns.Any(pattern =>
            relative.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCodeFile(string path)
    {
        var codeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".cs", ".fs", ".vb",
            ".ts", ".tsx", ".js", ".jsx",
            ".py", ".rb", ".go", ".rs", ".java", ".kt", ".swift",
            ".cpp", ".c", ".h", ".hpp",
            ".json", ".yaml", ".yml", ".toml",
            ".sql", ".graphql", ".proto",
            ".md", ".txt", ".env.example",
            ".csproj", ".fsproj", ".sln", ".gradle", ".pom"
        };

        return codeExtensions.Contains(Path.GetExtension(path));
    }
}
