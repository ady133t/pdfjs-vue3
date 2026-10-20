using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using Tabula;
using Tabula.Detectors;
using Tabula.Extractors;

namespace PdfConverter.Api.Services;

public sealed record ExtractedTable(
    int Page,
    string Method,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    double Left,
    double Bottom,
    double Right,
    double Top
);

/// <summary>
/// Extracts tabular data with tabula-sharp (a port of tabula-java).
/// Lattice extraction handles ruled tables; stream extraction contributes
/// borderless tables. Every result includes its PDF-space bounds so the Word
/// exporter can exclude table text from the prose flow and rebuild a real table.
/// </summary>
public sealed class TableExtractor
{
    private const int MaxTables = 40;

    public IReadOnlyList<ExtractedTable> Extract(byte[] pdfBytes)
    {
        using var input = new MemoryStream(pdfBytes, writable: false);
        using var document = PdfDocument.Open(input, new ParsingOptions { ClipPaths = true });
        if (document.NumberOfPages > 300)
        {
            throw new ConversionException(
                422,
                "too_many_pages",
                "This PDF has too many pages. The converter supports up to 300 pages."
            );
        }

        var results = new List<ExtractedTable>();
        for (var pageNumber = 1; pageNumber <= document.NumberOfPages && results.Count < MaxTables; pageNumber++)
        {
            PageArea page;
            try
            {
                page = ObjectExtractor.Extract(document, pageNumber);
            }
            catch
            {
                continue;
            }

            if (!page.HasText)
            {
                continue;
            }

            var lattice = new List<ExtractedTable>();
            var latticeSignatures = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (var table in new SpreadsheetExtractionAlgorithm().Extract(page))
                {
                    var rows = ReadRows(table);
                    var extracted = BuildTable(rows, pageNumber, "lattice");
                    if (extracted is null)
                    {
                        continue;
                    }

                    lattice.Add(extracted);
                    foreach (var row in rows)
                    {
                        var signature = RowSignature(row);
                        if (signature.Length > 0)
                        {
                            latticeSignatures.Add(signature);
                        }
                    }
                }
            }
            catch
            {
                // A lattice failure does not prevent stream extraction.
            }

            foreach (var table in lattice)
            {
                results.Add(table);
                if (results.Count >= MaxTables)
                {
                    break;
                }
            }

            if (results.Count >= MaxTables)
            {
                break;
            }

            foreach (var group in ExtractStreamGroups(page))
            {
                if (results.Count >= MaxTables)
                {
                    break;
                }

                var kept = group
                    .Where(row => NonEmptyCount(row) >= 2)
                    .Where(row => !latticeSignatures.Contains(RowSignature(row)))
                    .ToList();

                var extracted = BuildTable(kept, pageNumber, "stream");
                if (extracted is null)
                {
                    continue;
                }

                var signature = TableSignature(extracted);
                if (results.Any(existing => TableSignature(existing) == signature))
                {
                    continue;
                }

                results.Add(extracted);
            }
        }

        return results;
    }

    private static List<List<Cell>> ReadRows(Table table)
    {
        return table.Rows
            .Select(row => row.ToList())
            .ToList();
    }

    private static IEnumerable<List<List<Cell>>> ExtractStreamGroups(PageArea page)
    {
        var tables = new List<Table>();
        try
        {
            var regions = new SimpleNurminenDetectionAlgorithm().Detect(page);
            foreach (var region in regions)
            {
                PageArea area;
                try
                {
                    area = page.GetArea(region.BoundingBox);
                }
                catch
                {
                    continue;
                }

                try
                {
                    tables.AddRange(new BasicExtractionAlgorithm().Extract(area));
                }
                catch
                {
                    // Ignore one bad region and keep the other candidates.
                }
            }
        }
        catch
        {
            // No stream candidates.
        }

        foreach (var table in tables)
        {
            var group = new List<List<Cell>>();
            foreach (var row in ReadRows(table))
            {
                if (NonEmptyCount(row) < 2)
                {
                    if (group.Count > 0)
                    {
                        yield return group;
                        group = new List<List<Cell>>();
                    }

                    continue;
                }

                group.Add(row);
            }

            if (group.Count > 0)
            {
                yield return group;
            }
        }
    }

    private static ExtractedTable? BuildTable(List<List<Cell>> source, int page, string method)
    {
        var rows = source
            .Where(row => row.Any(cell => !string.IsNullOrWhiteSpace(TextOf(cell))))
            .ToList();

        if (rows.Count < 2)
        {
            return null;
        }

        var columnCount = rows.Max(row => row.Count);
        var occupied = new bool[columnCount];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Count; column++)
            {
                if (!string.IsNullOrWhiteSpace(TextOf(row[column])))
                {
                    occupied[column] = true;
                }
            }
        }

        var keep = Enumerable.Range(0, columnCount).Where(index => occupied[index]).ToArray();
        if (keep.Length < 2)
        {
            return null;
        }

        var textRows = new List<IReadOnlyList<string>>();
        PdfRectangle? bounds = null;
        foreach (var row in rows)
        {
            var cells = new List<string>(keep.Length);
            var hasText = false;
            foreach (var column in keep)
            {
                var cell = column < row.Count ? row[column] : Cell.EMPTY;
                var text = TextOf(cell);
                cells.Add(text);
                if (text.Length == 0)
                {
                    continue;
                }

                hasText = true;
                if (cell.BoundingBox.Width > 0 && cell.BoundingBox.Height > 0)
                {
                    bounds = bounds is null ? cell.BoundingBox : Union(bounds, cell.BoundingBox);
                }
            }

            if (hasText)
            {
                textRows.Add(cells);
            }
        }

        if (textRows.Count < 2 || keep.Length < 2 || bounds is null)
        {
            return null;
        }

        var tableBounds = bounds.Value;
        var populatedCells = textRows.Sum(row => row.Count(value => value.Length > 0));
        if (populatedCells < 4)
        {
            return null;
        }

        var multiCellRows = textRows.Count(row => row.Count(value => value.Length > 0) >= 2);
        if (multiCellRows < 2)
        {
            return null;
        }

        var cellTexts = textRows
            .SelectMany(row => row)
            .Where(value => value.Length > 0)
            .ToList();
        var totalCharacters = cellTexts.Sum(value => value.Length);
        var longestCell = cellTexts.Max(value => value.Length);
        if (cellTexts.Count >= 4 && longestCell > 250 && longestCell > totalCharacters * 0.35)
        {
            // A page-sized border/box often arrives as one giant merged cell, not
            // real tabular data.
            return null;
        }

        var questionMarkRatio = textRows
            .SelectMany(row => row)
            .Count(value => value.Contains('?')) / (double)populatedCells;
        if (questionMarkRatio > 0.15)
        {
            return null;
        }

        if (method == "stream" && !IsLikelyStreamTable(textRows, keep.Length))
        {
            return null;
        }

        return new ExtractedTable(
            page,
            method,
            textRows,
            tableBounds.Left,
            tableBounds.Bottom,
            tableBounds.Right,
            tableBounds.Top
        );
    }

    private static bool IsLikelyStreamTable(
        IReadOnlyList<IReadOnlyList<string>> rows,
        int columnCount)
    {
        var cells = rows
            .SelectMany(row => row)
            .Where(cell => cell.Length > 0)
            .ToList();

        if (cells.Count < 6)
        {
            return false;
        }

        var numericRatio = cells.Count(IsNumericLike) / (double)cells.Count;
        var avgLength = cells.Average(cell => cell.Length);
        var longRatio = cells.Count(cell => cell.Length > 60) / (double)cells.Count;

        // Stream mode sees many things as grids (code listings, references,
        // figure labels). Requiring numeric data keeps precision reasonable.
        if (numericRatio < 0.25)
        {
            return false;
        }

        // A two-row, two-column fragment is usually prose. Require strong
        // numeric evidence for tiny candidates.
        if (rows.Count < 3 && numericRatio < 0.4)
        {
            return false;
        }

        // Two-column prose is common; only accept it when the cells are short
        // or contain enough numbers to look like a data table.
        if (columnCount == 2 && avgLength > 55 && numericRatio < 0.2)
        {
            return false;
        }

        if (avgLength > 70 && numericRatio < 0.2)
        {
            return false;
        }

        if (longRatio > 0.35 && numericRatio < 0.2)
        {
            return false;
        }

        return true;
    }

    private static bool IsNumericLike(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0 || trimmed.Length > 40)
        {
            return false;
        }

        return trimmed.Any(char.IsDigit) &&
            trimmed.All(character =>
                char.IsDigit(character) ||
                character is ' ' or ',' or '.' or '%' or '-' or '+' or '$' or '€' or '£' or '(' or ')' or '/'
            );
    }

    private static PdfRectangle Union(PdfRectangle? left, PdfRectangle right)
    {
        if (left is null)
        {
            return right;
        }

        var current = left.Value;
        return new PdfRectangle(
            Math.Min(current.Left, right.Left),
            Math.Min(current.Bottom, right.Bottom),
            Math.Max(current.Right, right.Right),
            Math.Max(current.Top, right.Top)
        );
    }

    private static int NonEmptyCount(IReadOnlyList<Cell> row) =>
        row.Count(cell => !string.IsNullOrWhiteSpace(TextOf(cell)));

    private static string RowSignature(IReadOnlyList<Cell> row) =>
        string.Join(" | ", row.Select(TextOf).Where(cell => cell.Length > 0));

    private static string TableSignature(ExtractedTable table) =>
        string.Join(
            "\u001E",
            table.Rows.Select(row =>
                string.Concat(row.Select(value => value.Where(character => !char.IsWhiteSpace(character))))
            )
        );

    private static string TextOf(Cell? cell)
    {
        if (cell is null)
        {
            return string.Empty;
        }

        try
        {
            return NormalizeCell(cell.GetText());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeCell(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return string.Join(" ", value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
