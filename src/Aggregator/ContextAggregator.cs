using DocGen.Models;
using System.Text.RegularExpressions;

namespace DocGen.Aggregator;

public class ContextAggregator(string repoPath)
{
    private const string DocGenFolder = ".docgen";
    private const string DocFile = ".docgen/ARCHITECTURE_MEMORY.md";

    // Section delimiters
    private const string AutoStart = "<!-- AUTO:START -->";
    private const string AutoEnd = "<!-- AUTO:END -->";
    private const string ManualStart = "<!-- MANUAL:START -->";
    private const string ManualEnd = "<!-- MANUAL:END -->";

    private readonly string _docGenPath = Path.Combine(Path.GetFullPath(repoPath), DocGenFolder);

    // ─── Public API ───────────────────────────────────────────────────────────

    public async Task<AggregatedContext> BuildContextAsync(RepoDelta delta)
    {
        var docPath = Path.Combine(_docGenPath, DocFile);
        var fullDoc = File.Exists(docPath)
            ? await File.ReadAllTextAsync(docPath)
            : string.Empty;

        var (autoSections, manualSections) = SegmentDocument(fullDoc);

        return new AggregatedContext
        {
            CurrentDocumentFull = fullDoc,
            AutoSections = autoSections,
            ManualSections = manualSections,
            Delta = delta
        };
    }

    public async Task<string> ReadFullDocumentAsync()
    {
        var docPath = Path.Combine(_docGenPath, DocFile);
        return File.Exists(docPath)
            ? await File.ReadAllTextAsync(docPath)
            : string.Empty;
    }

    // ─── Document Segmentation ────────────────────────────────────────────────

    private static (string auto, string manual) SegmentDocument(string content)
    {
        if (string.IsNullOrEmpty(content))
            return (string.Empty, string.Empty);

        var autoContent = ExtractSections(content, AutoStart, AutoEnd);
        var manualContent = ExtractSections(content, ManualStart, ManualEnd);

        return (autoContent, manualContent);
    }

    private static string ExtractSections(string content, string startTag, string endTag)
    {
        var result = new List<string>();
        var remaining = content;

        while (true)
        {
            var startIdx = remaining.IndexOf(startTag, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0) break;

            var contentStart = startIdx + startTag.Length;
            var endIdx = remaining.IndexOf(endTag, contentStart, StringComparison.OrdinalIgnoreCase);
            if (endIdx < 0) break;

            result.Add(remaining[contentStart..endIdx].Trim());
            remaining = remaining[(endIdx + endTag.Length)..];
        }

        return string.Join("\n\n---\n\n", result);
    }

    // ─── Bootstrap document generation ───────────────────────────────────────

    public static string BuildBootstrapPrompt(string repoPath)
    {
        var files = ScanRelevantFiles(repoPath).Take(40).ToList();
        var fileList = string.Join("\n", files.Select(f =>
            "- " + f.Replace(repoPath, "").TrimStart(Path.DirectorySeparatorChar)));

        return $"""
            Você é um CTO sênior com profundo conhecimento em arquitetura de software.
            Analise a estrutura de arquivos deste repositório e gere a versão inicial do ARCHITECTURE_MEMORY.md.

            ESTRUTURA DO PROJETO:
            {fileList}

            Gere um documento Markdown estruturado com as seguintes seções dentro das tags <!-- AUTO:START --> e <!-- AUTO:END -->:

            1. **Visão Geral** — Qual o objetivo desse projeto e como pode ser usado
            2. **Stack Tecnológico** — Linguagens, frameworks, principais dependências
            3. **Estrutura de Módulos** — Como o código está organizado
            4. **Fluxo Principal** — Como os componentes se comunicam
            5. **Decisões Arquiteturais** — Padrões e escolhas relevantes identificados

            Após as tags AUTO, inclua uma seção de exemplo para anotações manuais:
            <!-- MANUAL:START -->
            *(Espaço para anotações manuais do desenvolvedor — nunca será sobrescrito pela IA)*
            <!-- MANUAL:END -->

            IMPORTANTE: Seja preciso e técnico. Evite generalidades. Baseie-se no que está realmente nos arquivos listados.
            """;
    }

    public static string BuildUpdatePrompt(AggregatedContext ctx)
    {
        var commits = ctx.Delta.Commits.Count > 0
            ? string.Join("\n", ctx.Delta.Commits.Select(c =>
                $"  [{c.Date}] {c.Sha} — {c.Message} ({c.Author})"))
            : "  (sem commits novos identificados)";

        var diff = string.IsNullOrEmpty(ctx.Delta.DiffSummary)
            ? "  (diff não disponível)"
            : ctx.Delta.DiffSummary;

        var manual = string.IsNullOrEmpty(ctx.ManualSections)
            ? "(nenhuma anotação manual)"
            : ctx.ManualSections;

        return
            "Você é um CTO sênior. Seu papel é manter o ARCHITECTURE_MEMORY.md atualizado e preciso.\n\n" +
            "═══ DOCUMENTO ATUAL (seções AUTO) ═══\n" +
            $"{ctx.AutoSections}\n\n" +
            "═══ ANOTAÇÕES MANUAIS (contexto somente-leitura, NUNCA altere) ═══\n" +
            $"{manual}\n\n" +
            $"═══ NOVOS COMMITS ({ctx.Delta.Commits.Count} commits, {ctx.Delta.FilesChanged} arquivos) ═══\n" +
            $"{commits}\n\n" +
            "═══ DIFF DE CÓDIGO ═══\n" +
            $"{diff}\n\n" +
            "═══ INSTRUÇÃO ═══\n" +
            "Atualize as seções AUTO do documento incorporando as mudanças acima.\n" +
            "Mantenha o estilo técnico e direto. Preserve informações que continuam válidas.\n" +
            "Remova apenas o que ficou explicitamente desatualizado pelo diff.\n\n" +
            "Responda SOMENTE com o conteúdo atualizado das seções AUTO (sem as tags delimitadoras).\n" +
            "Depois, em uma linha separada, escreva exatamente:\n" +
            "---CHANGES---\n" +
            "E liste em JSON:\n" +
            "{\"breaking_changes\": [...], \"new_features\": [...], \"refactors\": []}";
    }

    private static IEnumerable<string> ScanRelevantFiles(string repoPath)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "node_modules", "bin", "obj", ".git", "dist", "build", "__pycache__", ".docgen" };

        return Directory
            .EnumerateFiles(repoPath, "*", SearchOption.AllDirectories)
            .Where(f => !skip.Any(s => f.Contains(s)))
            .OrderBy(f => f.Length);
    }
}