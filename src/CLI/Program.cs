using System.CommandLine;
using System.Text.Json;
using DocGen.Aggregator;
using DocGen.Inserter;
using DocGen.Logger;
using DocGen.Models;
using DocGen.PromptEngine;
using DocGen.Scanner;

// ─── Root Command ─────────────────────────────────────────────────────────────

var root = new RootCommand("doc-gen — Arquitetura Viva: documentação gerada e mantida por IA");

// ─── Shared Options ───────────────────────────────────────────────────────────

var repoOption = new Option<string>(
    ["--repo", "-r"],
    getDefaultValue: () => ".",
    description: "Caminho do repositório (padrão: diretório atual)");

var dryRunOption = new Option<bool>(
    ["--dry-run", "-d"],
    description: "Simula a execução sem salvar alterações");

var jsonOption = new Option<bool>(
    ["--json", "-j"],
    description: "Emite saída estruturada em JSON");

var verboseOption = new Option<bool>(
    ["--verbose", "-v"],
    description: "Exibe informações detalhadas da execução");

var autoCommitOption = new Option<bool>(
    ["--auto-commit"],
    description: "Faz commit automático das mudanças no ARCHITECTURE_MEMORY.md");

var providerOption = new Option<string>(
    ["--provider", "-p"],
    getDefaultValue: () => "anthropic",
    description: "Provider de IA: anthropic | gemini");

// ─── init command ─────────────────────────────────────────────────────────────

var initCmd = new Command("init", "Inicializa o ARCHITECTURE_MEMORY.md a partir do repositório atual");
initCmd.AddOption(repoOption);
initCmd.AddOption(dryRunOption);
initCmd.AddOption(verboseOption);
initCmd.AddOption(providerOption);

initCmd.SetHandler(async (repo, dryRun, verbose, provider) =>
{
    var repoPath = Path.GetFullPath(repo);
    Console.WriteLine($"\n🏗  doc-gen init [{provider}] — {repoPath}\n");

    try
    {
        var docPath = Path.Combine(repoPath, "ARCHITECTURE_MEMORY.md");
        if (File.Exists(docPath) && !dryRun)
        {
            Console.Write("ARCHITECTURE_MEMORY.md já existe. Sobrescrever? [s/N] ");
            var ans = Console.ReadLine()?.Trim().ToLower();
            if (ans != "s") { Console.WriteLine("Cancelado."); return; }
        }

        var aggregator = new ContextAggregator(repoPath);
        var engine = PromptEngineFactory.Create(provider);
        var inserter = new MarkdownInserter(repoPath);
        var logger = new AuditLogger(repoPath);

        Console.WriteLine("→ Analisando estrutura do repositório...");
        var prompt = ContextAggregator.BuildBootstrapPrompt(repoPath);

        if (verbose) Console.WriteLine($"\nPrompt ({prompt.Length} chars):\n{prompt[..Math.Min(300, prompt.Length)]}...\n");

        Console.WriteLine("→ Chamando IA para gerar documento inicial...");
        var result = await engine.GenerateInitialDocAsync(prompt);

        if (!result.Success)
        {
            Console.WriteLine($"\n✗ Erro na IA: {result.Error}");
            return;
        }

        // Wrap the AI output in the expected structure
        var fullDoc = $"""
            # ARCHITECTURE_MEMORY

            > Documento de arquitetura vivo — gerado e mantido por IA.
            > Atualize as seções `AUTO` via `doc-gen update`. Escreva em `MANUAL` para anotações permanentes.

            <!-- AUTO:START -->
            {result.UpdatedAutoContent}
            <!-- AUTO:END -->

            ---

            ## Anotações Manuais

            <!-- MANUAL:START -->
            *(Adicione aqui decisões de negócio, contexto histórico ou restrições importantes.
            Este bloco NUNCA será sobrescrito pela IA.)*
            <!-- MANUAL:END -->
            """;

        await inserter.WriteBootstrapAsync(fullDoc, dryRun);

        // Save initial state
        if (!dryRun)
        {
            var scanner = new RepositoryScanner(repoPath);
            var delta = await scanner.ComputeDeltaAsync();
            var state = new DocState
            {
                LastRun = DateTime.UtcNow.ToString("o"),
                SnapshotHash = delta.CurrentHash,
                LastCommitSha = delta.ToCommit,
                TokensUsedTotal = result.TokensUsed
            };
            await scanner.SaveStateAsync(state);
            await logger.LogAsync(AuditLogger.BuildEntry("", delta.CurrentHash, "bootstrap", result));
        }

        Console.WriteLine(dryRun
            ? "\n✓ Dry-run concluído — nenhum arquivo foi alterado."
            : $"\n✓ ARCHITECTURE_MEMORY.md criado com sucesso! ({result.TokensUsed} tokens usados)");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("ANTHROPIC_API_KEY"))
    {
        Console.WriteLine($"\n✗ {ex.Message}");
        Console.WriteLine("  Exemplo: export ANTHROPIC_API_KEY=sk-ant-...");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n✗ Erro inesperado: {ex.Message}");
        if (verbose) Console.WriteLine(ex.StackTrace);
    }

}, repoOption, dryRunOption, verboseOption, providerOption);

// ─── update command ───────────────────────────────────────────────────────────

var updateCmd = new Command("update", "Atualiza o ARCHITECTURE_MEMORY.md com as mudanças recentes");
updateCmd.AddOption(repoOption);
updateCmd.AddOption(dryRunOption);
updateCmd.AddOption(jsonOption);
updateCmd.AddOption(autoCommitOption);
updateCmd.AddOption(verboseOption);
updateCmd.AddOption(providerOption);

updateCmd.SetHandler(async (repo, dryRun, jsonOut, autoCommit, verbose, provider) =>
{
    var repoPath = Path.GetFullPath(repo);

    if (!jsonOut)
        Console.WriteLine($"\n🔄  doc-gen update [{provider}] — {repoPath}\n");

    try
    {
        var scanner = new RepositoryScanner(repoPath);
        var aggregator = new ContextAggregator(repoPath);
        var engine = PromptEngineFactory.Create(provider);
        var inserter = new MarkdownInserter(repoPath);
        var logger = new AuditLogger(repoPath);

        var state = await scanner.LoadStateAsync();

        // 1. Check for changes
        if (!jsonOut) Console.WriteLine("→ Verificando mudanças no repositório...");
        var delta = await scanner.ComputeDeltaAsync();

        if (!delta.HasChanges)
        {
            if (jsonOut)
                Console.WriteLine(JsonSerializer.Serialize(new { status = "skipped", reason = "no_changes" }));
            else
                Console.WriteLine("✓ Nada a fazer.");

            await logger.LogAsync(AuditLogger.BuildEntry(state.SnapshotHash, state.SnapshotHash, "skipped"));
            return;
        }

        if (!jsonOut)
        {
            Console.WriteLine($"  {delta.Commits.Count} commit(s) | {delta.FilesChanged} arquivo(s) alterado(s)");
            if (verbose && delta.Commits.Any())
            {
                Console.WriteLine("  Commits:");
                foreach (var c in delta.Commits.Take(5))
                    Console.WriteLine($"    [{c.Date}] {c.Sha} {c.Message}");
            }
        }

        // 2. Build context
        if (!jsonOut) Console.WriteLine("→ Montando contexto...");
        var ctx = await aggregator.BuildContextAsync(delta);

        var docPath = Path.Combine(repoPath, "ARCHITECTURE_MEMORY.md");
        if (!File.Exists(docPath))
        {
            Console.WriteLine("✗ ARCHITECTURE_MEMORY.md não encontrado. Execute: doc-gen init");
            return;
        }

        // 3. Call AI
        if (!jsonOut) Console.WriteLine("→ Chamando IA para síntese...");
        var prompt = ContextAggregator.BuildUpdatePrompt(ctx);
        if (verbose) Console.WriteLine($"\nPrompt ({prompt.Length} chars):\n{prompt[..Math.Min(400, prompt.Length)]}...\n");

        var synthesis = await engine.SynthesizeUpdateAsync(prompt);

        if (!synthesis.Success)
        {
            Console.WriteLine($"\n✗ Erro na IA: {synthesis.Error}");
            await logger.LogAsync(AuditLogger.BuildEntry(state.SnapshotHash, delta.CurrentHash, "error", error: synthesis.Error));
            return;
        }

        // 4. Validate + write
        if (!jsonOut) Console.WriteLine("→ Validando e aplicando mudanças...");
        var (ok, err) = await inserter.ApplyAsync(synthesis.UpdatedAutoContent, dryRun);

        if (!ok)
        {
            Console.WriteLine($"\n✗ Falha ao aplicar: {err}");
            await logger.LogAsync(AuditLogger.BuildEntry(state.SnapshotHash, delta.CurrentHash, "error", error: err));
            return;
        }

        // 5. Update state + log
        if (!dryRun)
        {
            var newState = state with
            {
                LastRun = DateTime.UtcNow.ToString("o"),
                SnapshotHash = delta.CurrentHash,
                LastCommitSha = delta.ToCommit,
                TokensUsedTotal = state.TokensUsedTotal + synthesis.TokensUsed,
                RunCount = state.RunCount + 1
            };
            await scanner.SaveStateAsync(newState);
            await logger.LogAsync(AuditLogger.BuildEntry(
                state.SnapshotHash, delta.CurrentHash, "success", synthesis));

            // 6. Auto commit
            if (autoCommit)
            {
                var commitMsg = BuildCommitMessage(synthesis);
                RunGitCommit(repoPath, commitMsg, verbose);
            }
        }

        // 7. Output
        if (jsonOut)
        {
            var output = new
            {
                status = dryRun ? "dry_run" : "success",
                tokens_used = synthesis.TokensUsed,
                breaking_changes = synthesis.BreakingChanges,
                new_features = synthesis.NewFeatures,
                refactors = synthesis.Refactors
            };
            Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            var prefix = dryRun ? "Dry-run" : "✓ Documentação atualizada";
            var bc = synthesis.BreakingChanges.Count;
            var nf = synthesis.NewFeatures.Count;
            var ref_ = synthesis.Refactors.Count;

            Console.WriteLine($"\n{prefix}. {synthesis.TokensUsed} tokens usados.");
            if (bc > 0) PrintList("⚠ Breaking changes", synthesis.BreakingChanges);
            if (nf > 0) PrintList("✦ Novas features", synthesis.NewFeatures);
            if (ref_ > 0) PrintList("↺ Refatorações", synthesis.Refactors);
        }
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("ANTHROPIC_API_KEY"))
    {
        Console.WriteLine($"\n✗ {ex.Message}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"\n✗ Erro inesperado: {ex.Message}");
        if (verbose) Console.WriteLine(ex.StackTrace);
    }

}, repoOption, dryRunOption, jsonOption, autoCommitOption, verboseOption, providerOption);

// ─── status command ───────────────────────────────────────────────────────────

var statusCmd = new Command("status", "Exibe o estado atual da documentação");
statusCmd.AddOption(repoOption);

statusCmd.SetHandler(async (repo) =>
{
    var repoPath = Path.GetFullPath(repo);
    var scanner = new RepositoryScanner(repoPath);
    var logger = new AuditLogger(repoPath);
    var state = await scanner.LoadStateAsync();
    var logs = await logger.LoadAsync();

    Console.WriteLine($"\n📋  Status — {repoPath}\n");

    if (string.IsNullOrEmpty(state.LastRun))
    {
        Console.WriteLine("  Nenhuma execução registrada. Execute: doc-gen init");
        return;
    }

    Console.WriteLine($"  Última execução : {state.LastRun}");
    Console.WriteLine($"  Último hash     : {state.SnapshotHash}");
    Console.WriteLine($"  Último commit   : {state.LastCommitSha}");
    Console.WriteLine($"  Total de tokens : {state.TokensUsedTotal:N0}");
    Console.WriteLine($"  Total de runs   : {state.RunCount}");

    if (logs.Any())
    {
        Console.WriteLine($"\n  Últimas {Math.Min(5, logs.Count)} execuções:");
        foreach (var log in logs.Take(5))
            Console.WriteLine($"    [{log.Timestamp[..19]}] {log.Result,-10} {log.TokensUsed,5} tokens");
    }

    Console.WriteLine();

}, repoOption);

// ─── Helpers ──────────────────────────────────────────────────────────────────

static void PrintList(string label, List<string> items)
{
    Console.WriteLine($"  {label}:");
    foreach (var item in items)
        Console.WriteLine($"    • {item}");
}

static string BuildCommitMessage(SynthesisResult s)
{
    var parts = new List<string>();
    if (s.BreakingChanges.Count > 0) parts.Add($"{s.BreakingChanges.Count} breaking");
    if (s.NewFeatures.Count > 0) parts.Add($"{s.NewFeatures.Count} features");
    if (s.Refactors.Count > 0) parts.Add($"{s.Refactors.Count} refactors");

    var summary = parts.Any() ? string.Join(", ", parts) : "atualização";
    return $"docs: atualiza arquitetura — {summary}";
}

static void RunGitCommit(string repoPath, string message, bool verbose)
{
    try
    {
        RunProcess(repoPath, "git", "add ARCHITECTURE_MEMORY.md .doc_state.json .doc_gen_log.json");
        RunProcess(repoPath, "git", $"commit -m \"{message}\"");
        Console.WriteLine($"  Git: commit '{message}'");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ⚠ Git commit falhou: {ex.Message}");
    }
}

static void RunProcess(string workDir, string cmd, string args)
{
    var psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
    {
        WorkingDirectory = workDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false
    };
    var p = System.Diagnostics.Process.Start(psi)!;
    p.WaitForExit();
}

// ─── Wire up ──────────────────────────────────────────────────────────────────

root.AddGlobalOption(repoOption);
root.AddCommand(initCmd);
root.AddCommand(updateCmd);
root.AddCommand(statusCmd);

return await root.InvokeAsync(args);