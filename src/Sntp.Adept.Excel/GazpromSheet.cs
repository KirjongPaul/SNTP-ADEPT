using ClosedXML.Excel;
using Sntp.Adept.Domain.Catalog;

namespace Sntp.Adept.Excel;

public static class GazpromSheet
{
    public static List<VendorCoeff> Read(Stream stream)
    {
        using var book = new XLWorkbook(stream);
        var sheet = book.Worksheets.First();
        var rows = new List<VendorCoeff>();
        var first = true;
        foreach (var row in sheet.RowsUsed())
        {
            if (first)
            {
                first = false;
                if (row.Cell(1).GetString().Contains("код", StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var id = row.Cell(1).GetString().Trim();
            if (id.Length == 0) continue;
            decimal? value = null;
            if (decimal.TryParse(row.Cell(3).GetString().Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
                value = d;
            else if (row.Cell(3).TryGetValue(out double dbl))
                value = (decimal)dbl;
            rows.Add(new VendorCoeff
            {
                Identifier = id,
                Caption = row.Cell(2).GetString().Trim(),
                Value = value,
                Role = row.Cell(4).GetString().Trim(),
                Source = "user-import"
            });
        }
        return rows;
    }
}
