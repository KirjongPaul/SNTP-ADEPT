using System.Globalization;
using ClosedXML.Excel;
using ExcelDataReader;
using Sntp.Adept.Domain.Calc;

namespace Sntp.Adept.Excel;

public sealed class SheetTable
{
    public string Name { get; }
    public SheetCell[,] Cells { get; }
    public int Rows { get; }
    public int Cols { get; }

    public SheetTable(string name, SheetCell[,] cells)
    {
        Name = name;
        Cells = cells;
        Rows = cells.GetLength(0);
        Cols = cells.GetLength(1);
    }

    public SheetCell CellAt(int row, int col) =>
        row < 0 || col < 0 || row >= Rows || col >= Cols ? SheetCell.Empty : Cells[row, col];

    public string Text(int row, int col) => CellAt(row, col).DisplayInvariant;

    public string Display(int row, int col) => CellAt(row, col).Display;

    public decimal? Number(int row, int col) => CellAt(row, col).Number;

    public string? Formula(int row, int col) => CellAt(row, col).Formula;

    public string Concat(int maxRows = 40, int maxCols = 20)
    {
        var sb = new System.Text.StringBuilder();
        var rmax = Math.Min(Rows, maxRows);
        var cmax = Math.Min(Cols, maxCols);
        for (var r = 0; r < rmax; r++)
        for (var c = 0; c < cmax; c++)
        {
            var t = Display(r, c);
            if (t.Length > 0) sb.Append(t).Append('\n');
            var f = Formula(r, c);
            if (!string.IsNullOrEmpty(f)) sb.Append(f).Append('\n');
        }
        return sb.ToString();
    }

    public static List<SheetTable> LoadWorkbook(Stream stream)
    {
        if (stream.CanSeek)
            stream.Position = 0;
        MemoryStream buffer;
        if (stream is MemoryStream ms && ms.CanSeek)
        {
            buffer = ms;
        }
        else
        {
            buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
        }

        try
        {
            return LoadClosedXml(buffer);
        }
        catch
        {
            buffer.Position = 0;
            return LoadExcelDataReader(buffer);
        }
    }

    public static List<SheetTable> LoadWorkbook(string path)
    {
        using var fs = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return LoadWorkbook(fs);
    }

    private static List<SheetTable> LoadClosedXml(Stream stream)
    {
        using var wb = new XLWorkbook(stream);
        var list = new List<SheetTable>();
        foreach (var ws in wb.Worksheets)
        {
            var used = ws.RangeUsed(XLCellsUsedOptions.All);
            if (used is null)
            {
                list.Add(new SheetTable(ws.Name, new SheetCell[0, 0]));
                continue;
            }

            var rows = used.LastRow().RowNumber();
            var cols = Math.Min(used.LastColumn().ColumnNumber(), 40);
            var cells = new SheetCell[rows, cols];
            for (var r = 1; r <= rows; r++)
            for (var c = 1; c <= cols; c++)
                cells[r - 1, c - 1] = ReadClosedXmlCell(ws.Cell(r, c));
            list.Add(new SheetTable(ws.Name, cells));
        }
        return list;
    }

    private static SheetCell ReadClosedXmlCell(IXLCell cell)
    {
        string? formula = null;
        if (cell.HasFormula)
        {
            formula = cell.FormulaA1;
            if (!string.IsNullOrEmpty(formula) && !formula.StartsWith('='))
                formula = "=" + formula;
        }

        string display = "";
        decimal? number = null;
        try
        {
            var cached = cell.CachedValue;
            if (cached.IsNumber)
            {
                number = Convert.ToDecimal(cached.GetNumber(), CultureInfo.InvariantCulture);
                display = number.Value.ToString(CultureInfo.InvariantCulture);
            }
            else if (cached.IsText)
                display = cached.GetText() ?? "";
            else if (cached.IsDateTime)
                display = cached.GetDateTime().ToString("d", CultureInfo.GetCultureInfo("ru-RU"));
            else if (cached.IsBoolean)
                display = cached.GetBoolean() ? "TRUE" : "FALSE";
            else if (!cached.IsBlank)
                display = cached.ToString();
        }
        catch
        {
            try { display = cell.GetFormattedString(); }
            catch { display = cell.GetString(); }
        }

        if (string.IsNullOrEmpty(display))
        {
            try { display = cell.GetFormattedString(); }
            catch { /* ignore */ }
        }

        if (number is null)
            number = NumberParse.TryParse(display);

        return new SheetCell(display ?? "", number, formula);
    }

    private static List<SheetTable> LoadExcelDataReader(Stream stream)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var reader = ExcelDataReader.ExcelReaderFactory.CreateReader(stream);
        var ds = reader.AsDataSet(new ExcelDataReader.ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataReader.ExcelDataTableConfiguration { UseHeaderRow = false }
        });
        var list = new List<SheetTable>();
        foreach (System.Data.DataTable table in ds.Tables)
        {
            var rows = table.Rows.Count;
            var cols = Math.Min(table.Columns.Count, 40);
            var cells = new SheetCell[rows, cols];
            for (var r = 0; r < rows; r++)
            for (var c = 0; c < cols; c++)
            {
                var v = table.Rows[r][c];
                decimal? n = null;
                var display = "";
                if (v is not null and not DBNull)
                {
                    display = v.ToString() ?? "";
                    if (v is double d) n = (decimal)d;
                    else if (v is decimal m) n = m;
                    else if (v is int i) n = i;
                    else if (v is long l) n = l;
                    else n = NumberParse.TryParse(display);
                }
                cells[r, c] = new SheetCell(display, n, null);
            }
            list.Add(new SheetTable(table.TableName, cells));
        }
        return list;
    }
}

public readonly struct SheetCell(string display, decimal? number, string? formula)
{
    public static SheetCell Empty { get; } = new("", null, null);
    public string Display { get; } = display ?? "";
    public decimal? Number { get; } = number;
    public string? Formula { get; } = formula;
    public string DisplayInvariant => Number is decimal n
        ? n.ToString(CultureInfo.InvariantCulture)
        : Display;
}
