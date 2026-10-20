using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace PdfConverter.Api.Services;

/// <summary>Builds an XLSX workbook from tables detected in a PDF.</summary>
public sealed class PdfToXlsxConverter
{
    private readonly TableExtractor _tables;

    public PdfToXlsxConverter(TableExtractor tables)
    {
        _tables = tables;
    }

    public byte[] Convert(byte[] pdfBytes, string? title = null)
    {
        var tables = _tables.Extract(pdfBytes);
        if (tables.Count == 0)
        {
            throw new ConversionException(
                422,
                "no_tables",
                "No tables were detected in this PDF. Excel export works best with report and table-style PDFs. The document may be scanned, contain no ruled tables, or use an unsupported layout."
            );
        }

        using var output = new MemoryStream();
        using (var spreadsheet = SpreadsheetDocument.Create(output, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = spreadsheet.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            stylesPart.Stylesheet = CreateStylesheet();
            stylesPart.Stylesheet.Save();

            var sheets = new Sheets();
            var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sheetId = 0;
            var tablesPerPage = new Dictionary<int, int>();

            foreach (var table in tables)
            {
                sheetId++;
                tablesPerPage[table.Page] = tablesPerPage.GetValueOrDefault(table.Page) + 1;
                var tableNumber = tablesPerPage[table.Page];
                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheetData = new SheetData();
                var columnCount = table.Rows.Max(row => row.Count);

                for (var rowIndex = 0; rowIndex < table.Rows.Count; rowIndex++)
                {
                    var row = new Row { RowIndex = (uint)(rowIndex + 1) };
                    for (var columnIndex = 0; columnIndex < columnCount; columnIndex++)
                    {
                        var text = columnIndex < table.Rows[rowIndex].Count
                            ? table.Rows[rowIndex][columnIndex]
                            : string.Empty;

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            continue;
                        }

                        var cell = new Cell
                        {
                            CellReference = $"{ColumnName(columnIndex)}{rowIndex + 1}",
                            StyleIndex = (uint)(rowIndex == 0 ? 1 : 0),
                        };

                        if (TryNumber(text, out var number, out var isPercent))
                        {
                            cell.DataType = CellValues.Number;
                            cell.CellValue = new CellValue(number.ToString("G17", CultureInfo.InvariantCulture));
                            if (isPercent)
                            {
                                cell.StyleIndex = 2;
                            }
                        }
                        else
                        {
                            cell.DataType = CellValues.InlineString;
                            cell.InlineString = new InlineString(
                                new Text(text) { Space = SpaceProcessingModeValues.Preserve }
                            );
                        }

                        row.Append(cell);
                    }

                    sheetData.Append(row);
                }

                var worksheet = new Worksheet();
                var sheetView = new SheetView
                {
                    WorkbookViewId = 0,
                    TabSelected = sheetId == 1,
                };
                sheetView.Append(new Pane
                {
                    VerticalSplit = 1,
                    TopLeftCell = "A2",
                    ActivePane = PaneValues.BottomLeft,
                    State = PaneStateValues.Frozen,
                });
                worksheet.Append(new SheetViews(sheetView));
                worksheet.Append(BuildColumns(table, columnCount));
                worksheet.Append(sheetData);
                worksheet.Append(new AutoFilter
                {
                    Reference = $"A1:{ColumnName(columnCount - 1)}{table.Rows.Count}",
                });

                worksheetPart.Worksheet = worksheet;
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = (uint)sheetId,
                    Name = FileNames.SheetName($"Page {table.Page} Table {tableNumber}", usedNames),
                });
            }

            workbookPart.Workbook.Append(sheets);
            workbookPart.Workbook.Save();
            spreadsheet.PackageProperties.Title = string.IsNullOrWhiteSpace(title)
                ? "Converted tables"
                : title;
            spreadsheet.PackageProperties.Creator = "pdfjs-vue3 PDF converter";
        }

        return output.ToArray();
    }

    private static Columns BuildColumns(ExtractedTable table, int columnCount)
    {
        var columns = new Columns();
        for (var column = 0; column < columnCount; column++)
        {
            var maxLength = table.Rows
                .Select(row => column < row.Count ? row[column].Length : 0)
                .DefaultIfEmpty(0)
                .Max();
            var width = Math.Clamp(maxLength + 2, 10, 48);
            columns.Append(new Column
            {
                Min = (uint)(column + 1),
                Max = (uint)(column + 1),
                Width = width,
                CustomWidth = true,
            });
        }

        return columns;
    }

    private static Stylesheet CreateStylesheet()
    {
        var fonts = new Fonts(
            new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }),
            new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })
        );
        fonts.Count = 2;

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }),
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 })
        );
        fills.Count = 2;

        var borders = new Borders(
            new Border(
                new LeftBorder(),
                new RightBorder(),
                new TopBorder(),
                new BottomBorder(),
                new DiagonalBorder()
            )
        );
        borders.Count = 1;

        var cellStyleFormats = new CellStyleFormats(
            new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }
        );
        cellStyleFormats.Count = 1;

        var cellFormats = new CellFormats(
            new CellFormat
            {
                NumberFormatId = 0,
                FontId = 0,
                FillId = 0,
                BorderId = 0,
                ApplyFont = true,
            },
            new CellFormat
            {
                NumberFormatId = 0,
                FontId = 1,
                FillId = 0,
                BorderId = 0,
                ApplyFont = true,
            },
            new CellFormat
            {
                NumberFormatId = 10,
                FontId = 0,
                FillId = 0,
                BorderId = 0,
                ApplyNumberFormat = true,
            }
        );
        cellFormats.Count = 3;

        var cellStyles = new CellStyles(
            new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }
        );
        cellStyles.Count = 1;

        return new Stylesheet(fonts, fills, borders, cellStyleFormats, cellFormats, cellStyles);
    }

    private static bool TryNumber(string text, out double value, out bool isPercent)
    {
        value = 0;
        isPercent = false;
        var candidate = text.Trim();
        if (candidate.Length == 0)
        {
            return false;
        }

        if (candidate.EndsWith('%'))
        {
            isPercent = true;
            candidate = candidate[..^1].Trim();
        }

        if (candidate.StartsWith('0') && candidate.Length > 1 && !candidate.StartsWith("0."))
        {
            return false;
        }

        if (!Regex.IsMatch(candidate, @"^-?\d{1,3}(,\d{3})*(\.\d+)?$|^-?\d+(\.\d+)?$"))
        {
            return false;
        }

        candidate = candidate.Replace(",", string.Empty, StringComparison.Ordinal);
        if (!double.TryParse(candidate, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        if (isPercent)
        {
            value /= 100;
        }

        return true;
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
        {
            name = (char)('A' + (value - 1) % 26) + name;
        }

        return name;
    }
}
