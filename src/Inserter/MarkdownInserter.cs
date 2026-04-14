using DocGen.Models;
using DocGen.Pdf;
using Markdig;

namespace DocGen.Inserter;

public class MarkdownInserter
{
    private const string DocGenFolder = ".docgen";
    private const string DocFile = ".docgen/ARCHITECTURE_MEMORY.md";
    private const string PdfFile = "ARCHITECTURE_MEMORY.pdf";
    private const string AutoStart = "<!-- AUTO:START -->";
    private const string AutoEnd = "<!-- AUTO:END -->";
    private const string ManualStart = "<!-- MANUAL:START -->";
    private const string ManualEnd = "<!-- MANUAL:END -->";

    private readonly string _repoPath;
    private readonly string _docGenPath;
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public MarkdownInserter(string repoPath)
    {
        _repoPath = Path.GetFullPath(repoPath);
        _docGenPath = Path.Combine(_repoPath, DocGenFolder);

        if (!Directory.Exists(_docGenPath))
            Directory.CreateDirectory(_docGenPath);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the AI-generated content and safely writes it back to the document.
    /// Only content inside AUTO tags is replaced. MANUAL sections are never touched.
    /// </summary>
    public async Task<(bool Success, string? Error)> ApplyAsync(
        string newAutoContent,
        bool dryRun = false)
    {
        var docPath = Path.Combine(_repoPath, DocFile);
        var pdfPath = Path.Combine(_docGenPath, PdfFile);

        if (!File.Exists(docPath))
            return (false, $"Arquivo não encontrado: {docPath}");

        // Validate the new content is valid Markdown
        if (!IsValidMarkdown(newAutoContent, out var mdError))
            return (false, $"Conteúdo da IA não é Markdown válido: {mdError}");

        var original = await File.ReadAllTextAsync(docPath);
        var updated = ReplaceAutoSections(original, newAutoContent);

        if (updated == null)
            return (false, "Falha ao localizar tags AUTO no documento. Verifique se <!-- AUTO:START --> e <!-- AUTO:END --> estão presentes.");

        // Verify manual sections survived intact
        if (!ManualSectionsIntact(original, updated))
            return (false, "ERRO CRÍTICO: seções MANUAL foram alteradas. Operação abortada.");

        if (dryRun)
        {
            Console.WriteLine("\n──── DRY RUN — diff do que seria escrito ────");
            ShowDiff(original, updated);
            Console.WriteLine("──────────────────────────────────────────────\n");
            return (true, null);
        }

        // Backup before writing
        var backupPath = docPath + ".bak";
        await File.WriteAllTextAsync(backupPath, original);

        try
        {
            await File.WriteAllTextAsync(docPath, updated);
            await PdfGenerator.GeneratePdfAsync(updated, pdfPath);
            File.Delete(backupPath);
            return (true, null);
        }
        catch (Exception ex)
        {
            // Restore from backup on failure
            if (File.Exists(backupPath))
                File.Copy(backupPath, docPath, overwrite: true);
            return (false, $"Erro ao escrever o arquivo: {ex.Message}");
        }
    }

    /// <summary>
    /// Writes the initial bootstrapped document (no existing tags required).
    /// </summary>
    public async Task WriteBootstrapAsync(string fullContent, bool dryRun = false)
    {
        var docPath = Path.Combine(_repoPath, DocFile);
        var pdfPath = Path.Combine(_docGenPath, PdfFile);

        if (dryRun)
        {
            Console.WriteLine("\n──── DRY RUN — conteúdo que seria criado ────");
            Console.WriteLine(fullContent[..Math.Min(500, fullContent.Length)] + "\n...");
            Console.WriteLine("──────────────────────────────────────────────\n");
            return;
        }

        await File.WriteAllTextAsync(docPath, fullContent);
        await PdfGenerator.GeneratePdfAsync(fullContent, pdfPath);
    }

    // ─── Section Replacement ──────────────────────────────────────────────────

    private static string? ReplaceAutoSections(string original, string newContent)
    {
        var result = original;
        var hasAnyAutoSection = false;

        while (true)
        {
            var startIdx = result.IndexOf(AutoStart, StringComparison.OrdinalIgnoreCase);
            if (startIdx < 0) break;

            var contentStart = startIdx + AutoStart.Length;
            var endIdx = result.IndexOf(AutoEnd, contentStart, StringComparison.OrdinalIgnoreCase);
            if (endIdx < 0) break;

            hasAnyAutoSection = true;

            var before = result[..(startIdx + AutoStart.Length)];
            var after = result[endIdx..];
            result = before + "\n" + newContent + "\n" + after;

            // Only replace the first AUTO block — if there are multiple, they all get the same content
            break;
        }

        return hasAnyAutoSection ? result : null;
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    private static bool IsValidMarkdown(string content, out string error)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            error = "Conteúdo vazio";
            return false;
        }

        try
        {
            // Markdig parse — if it throws, it's malformed
            var doc = Markdown.Parse(content, Pipeline);
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private static bool ManualSectionsIntact(string original, string updated)
    {
        var originalManual = ExtractAllBetween(original, ManualStart, ManualEnd);
        var updatedManual = ExtractAllBetween(updated, ManualStart, ManualEnd);

        if (originalManual.Count != updatedManual.Count) return false;

        return !originalManual.Where((t, i) => t.Trim() != updatedManual[i].Trim()).Any();
    }

    private static List<string> ExtractAllBetween(string content, string start, string end)
    {
        var result = new List<string>();
        var remaining = content;

        while (true)
        {
            var si = remaining.IndexOf(start, StringComparison.OrdinalIgnoreCase);
            if (si < 0) break;
            var ci = si + start.Length;
            var ei = remaining.IndexOf(end, ci, StringComparison.OrdinalIgnoreCase);
            if (ei < 0) break;
            result.Add(remaining[ci..ei]);
            remaining = remaining[(ei + end.Length)..];
        }

        return result;
    }

    // ─── Dry-run diff ─────────────────────────────────────────────────────────

    private static void ShowDiff(string original, string updated)
    {
        var origLines = original.Split('\n');
        var newLines = updated.Split('\n');

        // Simple unified diff for console
        var maxLines = Math.Max(origLines.Length, newLines.Length);
        for (int i = 0; i < maxLines; i++)
        {
            var orig = i < origLines.Length ? origLines[i] : null;
            var next = i < newLines.Length ? newLines[i] : null;

            if (orig == next) continue;

            if (orig != null) Console.WriteLine($"\u001b[31m- {orig}\u001b[0m");
            if (next != null) Console.WriteLine($"\u001b[32m+ {next}\u001b[0m");
        }
    }
}