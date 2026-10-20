using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;

namespace PdfConverter.Api.Services;

/// <summary>
/// Produces an editable DOCX from the text flow of a PDF. This is a
/// reconstruction based on text blocks, not a pixel-perfect PDF renderer:
/// complex layouts and images are simplified.
/// </summary>
public sealed class PdfToDocxConverter
{
    private static readonly Regex SubsetPrefix = new("^[A-Z]{6}\\+", RegexOptions.Compiled);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    private readonly TableExtractor _tables;

    public PdfToDocxConverter(TableExtractor tables)
    {
        _tables = tables;
    }

    public byte[] Convert(byte[] pdfBytes, string? title = null)
    {
        using var input = new MemoryStream(pdfBytes, writable: false);
        using var pdf = PdfDocument.Open(input, new ParsingOptions { ClipPaths = true });
        if (pdf.NumberOfPages > 300)
        {
            throw new ConversionException(
                422,
                "too_many_pages",
                "This PDF has too many pages. The converter supports up to 300 pages."
            );
        }

        var tablesByPage = _tables
            .Extract(pdfBytes)
            .GroupBy(table => table.Page)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ExtractedTable>)group.ToList());

        using var output = new MemoryStream();
        using (var word = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            var mainPart = word.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = new Body();
            mainPart.Document.Append(body);

            var firstPage = pdf.GetPage(1);
            var pageWidth = firstPage.Width;
            var pageHeight = firstPage.Height;
            var pageNumber = 0;
            var tableNumber = 0;

            foreach (var page in pdf.GetPages())
            {
                if (pageNumber > 0)
                {
                    body.Append(PageBreak());
                }

                pageNumber++;
                var pageTables = tablesByPage.TryGetValue(pageNumber, out var found)
                    ? found
                    : Array.Empty<ExtractedTable>();
                var words = page.GetWords()
                    .Where(IsUsefulWord)
                    .Where(word => !IsInsideTable(word, pageTables))
                    .ToList();

                if (words.Count == 0 && pageTables.Count == 0)
                {
                    body.Append(new Paragraph(
                        new Run(new Text("[This page contains no extractable text. It may be a scanned image.]"))
                    ));
                    continue;
                }

                if (words.Count > 0)
                {
                    var bodyFontSize = Median(words.Select(FontSizeOf).Where(size => size > 0).ToList());
                    if (bodyFontSize <= 0)
                    {
                        bodyFontSize = 10;
                    }

                    var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
                    var ordered = UnsupervisedReadingOrderDetector.Instance
                        .Get(blocks)
                        .OrderBy(block => block.ReadingOrder)
                        .ToList();

                    foreach (var block in ordered)
                    {
                        AppendBlock(body, block, bodyFontSize, pageWidth);
                    }
                }

                foreach (var table in pageTables.OrderByDescending(table => table.Top))
                {
                    tableNumber++;
                    AppendTable(body, table, pageNumber, tableNumber);
                }
            }

            body.Append(new SectionProperties(
                new DocumentFormat.OpenXml.Wordprocessing.PageSize
                {
                    Width = (uint)Math.Round(pageWidth * 20),
                    Height = (uint)Math.Round(pageHeight * 20),
                },
                new PageMargin
                {
                    Top = 1080,
                    Bottom = 1080,
                    Left = 1080,
                    Right = 1080,
                    Header = 720,
                    Footer = 720,
                    Gutter = 0,
                }
            ));

            mainPart.Document.Save();
            word.PackageProperties.Title = string.IsNullOrWhiteSpace(title) ? "Converted document" : title;
            word.PackageProperties.Creator = "pdfjs-vue3 PDF converter";
        }

        return output.ToArray();
    }

    private static void AppendBlock(Body body, TextBlock block, double bodyFontSize, double pageWidth)
    {
        var lines = block.TextLines
            .Where(line => IsUsefulLine(NormalizeText(line.Text)))
            .ToList();

        foreach (var group in SplitLines(lines, bodyFontSize))
        {
            var text = string.Join(
                " ",
                group.Select(line => NormalizeText(line.Text)).Where(IsUsefulLine)
            );
            if (text.Length == 0)
            {
                continue;
            }

            var firstWord = group
                .SelectMany(line => line.Words)
                .FirstOrDefault(word => word.Text.Any(char.IsLetterOrDigit));
            if (firstWord is null)
            {
                continue;
            }

            AppendParagraph(body, text, firstWord, block.BoundingBox, bodyFontSize, pageWidth);
        }
    }

    private static IEnumerable<List<TextLine>> SplitLines(
        IReadOnlyList<TextLine> lines,
        double bodyFontSize)
    {
        var group = new List<TextLine>();
        TextLine? previous = null;
        foreach (var line in lines)
        {
            if (previous is null)
            {
                group.Add(line);
                previous = line;
                continue;
            }

            var previousSize = LineFontSize(previous);
            var size = LineFontSize(line);
            var sizeChange = Math.Abs(size - previousSize) >=
                Math.Max(1.5, Math.Max(size, previousSize) * 0.18);
            var significantSize = Math.Max(size, previousSize) >= bodyFontSize * 1.25 ||
                Math.Abs(size - previousSize) >= 3;

            var gap = previous.BoundingBox.Bottom - line.BoundingBox.Top;
            var gapLimit = Math.Max(10, bodyFontSize * 1.15);

            if ((sizeChange && significantSize) || gap > gapLimit)
            {
                yield return group;
                group = new List<TextLine>();
            }

            group.Add(line);
            previous = line;
        }

        if (group.Count > 0)
        {
            yield return group;
        }
    }

    private static void AppendParagraph(
        Body body,
        string text,
        Word firstWord,
        UglyToad.PdfPig.Core.PdfRectangle box,
        double bodyFontSize,
        double pageWidth)
    {
        var firstLetter = firstWord.Letters.FirstOrDefault();
        var details = firstLetter?.FontDetails;
        var fontSize = Math.Clamp(firstLetter?.FontSize ?? bodyFontSize, 7, 48);
        var isHeading = fontSize >= bodyFontSize * 1.35;
        var centered = isHeading && IsCentered(box, pageWidth);

        var paragraph = new Paragraph();
        var properties = new ParagraphProperties(
            new SpacingBetweenLines
            {
                Before = isHeading ? "160" : "0",
                After = isHeading ? "120" : "100",
                Line = "264",
                LineRule = LineSpacingRuleValues.Auto,
            },
            new Justification
            {
                Val = centered ? JustificationValues.Center : JustificationValues.Left,
            }
        );
        paragraph.Append(properties);

        var runProperties = new RunProperties();
        var fontName = CleanFontName(details?.Name);
        if (fontName is not null)
        {
            runProperties.Append(new RunFonts
            {
                Ascii = fontName,
                HighAnsi = fontName,
                ComplexScript = fontName,
            });
        }

        if (details?.IsBold == true || isHeading)
        {
            runProperties.Append(new Bold());
        }

        if (details?.IsItalic == true)
        {
            runProperties.Append(new Italic());
        }

        runProperties.Append(new FontSize
        {
            Val = Math.Round(fontSize * 2, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture),
        });

        paragraph.Append(new Run(
            runProperties,
            new Text(text) { Space = SpaceProcessingModeValues.Preserve }
        ));
        body.Append(paragraph);
    }

    private static double LineFontSize(TextLine line) =>
        line.Words
            .SelectMany(word => word.Letters)
            .FirstOrDefault()?.FontSize ?? 0;

    private static void AppendTable(Body body, ExtractedTable data, int page, int number)
    {
        var columnCount = Math.Max(1, data.Rows.Max(row => row.Count));
        var tableFontSize = columnCount > 6 ? "16" : "18";

        body.Append(new Paragraph(
            new ParagraphProperties(
                new SpacingBetweenLines { Before = "200", After = "80" }
            ),
            new Run(
                new RunProperties(new Bold(), new FontSize { Val = "18" }),
                new Text($"Table {number} (page {page})")
            )
        ));

        var table = new Table();
        table.Append(new TableProperties(
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = "B7BCC4" },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "B7BCC4" },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "B7BCC4" },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = "B7BCC4" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "DDE1E7" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "DDE1E7" }
            ),
            new TableLayout { Type = TableLayoutValues.Autofit }
        ));

        var grid = new TableGrid();
        for (var column = 0; column < columnCount; column++)
        {
            grid.Append(new GridColumn());
        }

        table.Append(grid);

        for (var rowIndex = 0; rowIndex < data.Rows.Count; rowIndex++)
        {
            var row = data.Rows[rowIndex];
            var tableRow = new TableRow();
            for (var column = 0; column < columnCount; column++)
            {
                var text = column < row.Count ? row[column] : string.Empty;
                var cell = new TableCell();
                var cellProperties = new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Auto }
                );
                if (rowIndex == 0)
                {
                    cellProperties.Append(new Shading
                    {
                        Val = ShadingPatternValues.Clear,
                        Fill = "F2F4F7",
                    });
                }

                cell.Append(cellProperties);

                var runProperties = new RunProperties();
                if (rowIndex == 0)
                {
                    runProperties.Append(new Bold());
                }

                runProperties.Append(new FontSize { Val = tableFontSize });
                var paragraph = new Paragraph(
                    new ParagraphProperties(
                        new SpacingBetweenLines
                        {
                            Before = "0",
                            After = "0",
                            Line = "240",
                            LineRule = LineSpacingRuleValues.Auto,
                        }
                    ),
                    new Run(
                        runProperties,
                        new Text(text) { Space = SpaceProcessingModeValues.Preserve }
                    )
                );
                cell.Append(paragraph);
                tableRow.Append(cell);
            }

            table.Append(tableRow);
        }

        body.Append(table);
        body.Append(new Paragraph(new Run(new Text(string.Empty))));
    }

    private static bool IsInsideTable(Word word, IReadOnlyList<ExtractedTable> tables)
    {
        if (tables.Count == 0)
        {
            return false;
        }

        var box = word.BoundingBox;
        var x = (box.Left + box.Right) / 2;
        var y = (box.Bottom + box.Top) / 2;
        foreach (var table in tables)
        {
            if (x >= table.Left - 2 && x <= table.Right + 2 &&
                y >= table.Bottom - 2 && y <= table.Top + 2)
            {
                return true;
            }
        }

        return false;
    }

    private static Paragraph PageBreak() => new(
        new Run(new Break { Type = BreakValues.Page })
    );

    private static double FontSizeOf(Word word) =>
        word.Letters.FirstOrDefault()?.FontSize ?? 0;

    private static bool IsUsefulWord(Word word) =>
        word.Text.Any(char.IsLetterOrDigit);

    private static bool IsUsefulLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= 4 && !trimmed.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        return true;
    }

    private static bool IsCentered(UglyToad.PdfPig.Core.PdfRectangle box, double pageWidth)
    {
        if (pageWidth <= 0)
        {
            return false;
        }

        var left = box.Left;
        var right = pageWidth - box.Right;
        return Math.Abs(left - right) < pageWidth * 0.08;
    }

    private static double Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.OrderBy(value => value).ToList();
        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    private static string NormalizeText(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text.Normalize(NormalizationForm.FormKC))
        {
            switch (character)
            {
                case '\uFB01':
                    builder.Append("fi");
                    break;
                case '\uFB02':
                    builder.Append("fl");
                    break;
                case '\uFB00':
                    builder.Append("ff");
                    break;
                case '\uFB03':
                    builder.Append("ffi");
                    break;
                case '\uFB04':
                    builder.Append("ffl");
                    break;
                case '\u00AD':
                case '\u200B':
                case '\uFEFF':
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return Spaces.Replace(builder.ToString(), " ").Trim();
    }

    private static string? CleanFontName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var cleaned = SubsetPrefix.Replace(name, string.Empty).Trim();
        var lower = cleaned.ToLowerInvariant();

        if (lower.Contains("nimbusrom") || lower.Contains("times") || lower.Contains("serif"))
        {
            return "Times New Roman";
        }

        if (lower.Contains("arial") || lower.Contains("helvetica") || lower.Contains("nimbussans"))
        {
            return "Arial";
        }

        if (lower.Contains("courier") || lower.Contains("mono"))
        {
            return "Courier New";
        }

        if (lower.Contains("cambria"))
        {
            return "Cambria";
        }

        if (lower.Contains("calibri"))
        {
            return "Calibri";
        }

        cleaned = Regex.Replace(cleaned, @"[^A-Za-z0-9 \-]", " ").Trim();
        if (cleaned.Length is < 2 or > 40)
        {
            return null;
        }

        return cleaned;
    }
}
