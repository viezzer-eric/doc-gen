using iText.Html2pdf;
using Markdig;

namespace DocGen.Pdf;

public static class PdfGenerator
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static async Task GeneratePdfAsync(string markdownContent, string outputPath)
    {
        var html = Markdown.ToHtml(markdownContent, Pipeline);
        var htmlWithStyles = WrapHtmlWithStyles(html);

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        using var fileStream = new FileStream(outputPath, FileMode.Create);

        HtmlConverter.ConvertToPdf(htmlWithStyles, fileStream);

        await Task.CompletedTask;
    }

    private static string WrapHtmlWithStyles(string bodyHtml)
    {
        string template = """
        <!DOCTYPE html>
        <html>
        <head>
            <meta charset="UTF-8">
            <style>
                /* Configurações para garantir que o PDF respeite a largura da página */
                body { 
                    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif; 
                    line-height: 1.6; 
                    color: #333; 
                    max-width: 100%; /* Mudado de 900px para 100% para PDF */
                    margin: 0; 
                    padding: 20px; 
                    word-wrap: break-word; /* Força quebra de palavras longas no corpo */
                }

                h1 { color: #2c3e50; border-bottom: 3px solid #3498db; padding-bottom: 10px; margin-top: 40px; }
                h2 { color: #34495e; border-bottom: 2px solid #95a5a6; padding-bottom: 8px; margin-top: 30px; }
                
                /* Ajuste crítico para blocos de código */
                code { 
                    background: #f4f4f4; 
                    padding: 2px 4px; 
                    border-radius: 3px; 
                    font-family: 'Courier New', monospace; 
                    font-size: 0.85em; 
                    word-break: break-all; /* Garante que strings longas não estourem */
                }

                pre { 
                    background: #f8f8f8; 
                    border: 1px solid #ddd; 
                    border-left: 3px solid #3498db; 
                    padding: 12px; 
                    white-space: pre-wrap;       /* Mantém espaços, mas quebra linha no final da largura */
                    word-wrap: break-word;       /* Força a quebra de linha */
                    overflow-wrap: break-word;
                    border-radius: 4px; 
                }

                pre code { 
                    background: none; 
                    padding: 0; 
                    word-break: normal; /* No bloco pre, deixamos o pre-wrap do pai agir */
                }

                table { 
                    border-collapse: collapse; 
                    width: 100%; 
                    margin: 20px 0; 
                    table-layout: fixed; /* Força a tabela a respeitar a largura de 100% */
                }

                th, td { 
                    border: 1px solid #ddd; 
                    padding: 8px; 
                    text-align: left; 
                    word-wrap: break-word; /* Quebra texto dentro das células */
                }

                th { background: #3498db; color: white; font-weight: bold; }
                tr:nth-child(even) { background: #f9f9f9; }
                
                blockquote { border-left: 4px solid #3498db; padding-left: 16px; margin-left: 0; color: #555; font-style: italic; }
                hr { border: none; border-top: 2px solid #e0e0e0; margin: 30px 0; }

                /* Otimização para impressão/PDF */
                @media print {
                    body { padding: 0; margin: 0; }
                    pre { page-break-inside: avoid; }
                }
            </style>
        </head>
        <body>
            PLACEHOLDER_BODY
        </body>
        </html>
        """;

        return template.Replace("PLACEHOLDER_BODY", bodyHtml);
    }
}