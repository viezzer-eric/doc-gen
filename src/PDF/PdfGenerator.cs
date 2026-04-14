using iText.Html2pdf;
using iText.Kernel.Pdf;
using iText.Layout.Borders;
using iText.Layout.Properties;
using Markdig;
using static System.Net.Mime.MediaTypeNames;

namespace DocGen.Pdf;

public static class PdfGenerator
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    public static async Task GeneratePdfAsync(string markdownContent, string outputPath)
    {
        var html = ConvertMarkdownToHtml(markdownContent);
        var htmlWithStyles = WrapHtmlWithStyles(html);

        await using var pdfWriter = new PdfWriter(outputPath);
        using var pdfDoc = new PdfDocument(pdfWriter);

        HtmlConverter.ConvertToPdf(htmlWithStyles, pdfDoc);
    }

    private static string ConvertMarkdownToHtml(string markdown)
    {
        return Markdown.ToHtml(markdown, Pipeline);
    }

    private static string WrapHtmlWithStyles(string bodyHtml)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <style>
                    body {{font - family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Arial, sans-serif;
                        line-height: 1.6;
                        color: #333;
                        max-width: 900px;
                        margin: 40px auto;
                        padding: 0 20px;
                    }}
                    h1 {{color: #2c3e50;
                        border-bottom: 3px solid #3498db;
                        padding-bottom: 10px;
                        margin-top: 40px;
                    }}
                    h2 {{color: #34495e;
                        border-bottom: 2px solid #95a5a6;
                        padding-bottom: 8px;
                        margin-top: 30px;
                    }}
                    h3 {{color: #555;
                        margin-top: 20px;
                    }}
                    code {{background: #f4f4f4;
                        padding: 2px 6px;
                        border-radius: 3px;
                        font-family: 'Courier New', monospace;
                        font-size: 0.9em;
                    }}
                    pre {{background: #f8f8f8;
                        border: 1px solid #ddd;
                        border-left: 3px solid #3498db;
                        padding: 12px;
                        overflow-x: auto;
                        border-radius: 4px;
                    }}
                    pre code {{background: none;
                        padding: 0;
                    }}
                    table {{border - collapse: collapse;
                        width: 100%;
                        margin: 20px 0;
                    }}
                    th, td {{border: 1px solid #ddd;
                        padding: 10px;
                        text-align: left;
                    }}
                    th {{background: #3498db;
                        color: white;
                        font-weight: bold;
                    
        }}
                    tr:nth-child(even) {{background: #f9f9f9;
                    }}
                    blockquote {{border - left: 4px solid #3498db;
                        padding-left: 16px;
                        margin-left: 0;
                        color: #555;
                        font-style: italic;
                    }}
                    hr {{border: none;
                        border-top: 2px solid #e0e0e0;
                        margin: 30px 0;
                    }}
                </style>
            </head>
            <body>
                {bodyHtml}
            </body>
            </html>
            """;
    }
}