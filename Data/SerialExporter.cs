using ClosedXML.Excel;

namespace SerialManager.Data;

/// <summary>
/// 匯出可以選的欄位。列舉的順序就是預設的欄位順序。
/// </summary>
public enum ExportField { Code, ItemName, PrizeLevel, AcquiredAt, ExpiresOn, Status, RedeemedAt, GivenTo, Note }

/// <summary>
/// 批次匯出：把勾選的序號轉成表格，再輸出成 Excel 檔或 Tab 分隔的文字（貼到 Excel 會自動分欄）。
/// </summary>
public static class SerialExporter
{
    public static string FieldName(ExportField field) => field switch
    {
        ExportField.Code => "序號",
        ExportField.ItemName => "內容",
        ExportField.PrizeLevel => "獎項等級",
        ExportField.AcquiredAt => "取得時間",
        ExportField.ExpiresOn => "效期",
        ExportField.Status => "狀態",
        ExportField.RedeemedAt => "兌換時間",
        ExportField.GivenTo => "送給誰",
        _ => "備註",
    };

    /// <summary>
    /// 依指定的欄位與順序產生表格；includeHeader 為 true 時第一列是標題。
    /// 所有值都是文字，序號才不會被 Excel 當成數字（例如前面的 0 不見）。
    /// </summary>
    public static List<string[]> BuildTable(IEnumerable<SerialCode> items, IReadOnlyList<ExportField> fields, DateTime today, bool includeHeader = true)
    {
        var table = new List<string[]>();
        if (includeHeader)
        {
            table.Add(fields.Select(FieldName).ToArray());
        }
        foreach (var item in items)
        {
            table.Add(fields.Select(field => FieldValue(item, field, today)).ToArray());
        }
        return table;
    }

    private static string FieldValue(SerialCode item, ExportField field, DateTime today) => field switch
    {
        ExportField.Code => item.Code,
        ExportField.ItemName => item.ItemName,
        ExportField.PrizeLevel => item.PrizeLevel ?? "",
        ExportField.AcquiredAt => item.AcquiredAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "",
        ExportField.ExpiresOn => item.ExpiresOn.ToString("yyyy/MM/dd"),
        ExportField.Status => StatusText(item.GetDisplayStatus(today)),
        ExportField.RedeemedAt => item.RedeemedAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "",
        ExportField.GivenTo => item.GivenTo ?? "",
        _ => item.Note ?? "",
    };

    /// <summary>
    /// 轉成 Tab 分隔文字。值裡的 Tab、換行換成空白，貼上時才不會跑欄。
    /// </summary>
    public static string ToTabText(List<string[]> table) =>
        string.Join("\r\n", table.Select(row => string.Join("\t", row.Select(Clean))));

    public static void SaveExcel(List<string[]> table, string path)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("序號");

        for (var r = 0; r < table.Count; r++)
        {
            for (var c = 0; c < table[r].Length; c++)
            {
                var cell = sheet.Cell(r + 1, c + 1);
                cell.SetValue(table[r][c]);                     // 一律存成文字
                cell.Style.NumberFormat.Format = "@";
            }
        }

        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);                          // 捲動時標題列固定

        // 欄寬：依每欄最長的內容計算（中文字算 2 個字寬），打開就看得到完整內容，不用手動拉
        var columnCount = table.Max(row => row.Length);
        for (var c = 0; c < columnCount; c++)
        {
            var longest = table.Max(row => c < row.Length ? DisplayWidth(row[c]) : 0);
            sheet.Column(c + 1).Width = Math.Clamp(longest + 3, 8, 80);
        }

        workbook.SaveAs(path);
    }

    /// <summary>
    /// 文字在 Excel 裡大約佔幾個字寬：中文、全形字算 2，英數字算 1。
    /// </summary>
    public static int DisplayWidth(string text) => text.Sum(ch => ch >= 0x2E80 ? 2 : 1);

    private static string Clean(string value) =>
        value.Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");

    private static string StatusText(DisplayStatus status) => status switch
    {
        DisplayStatus.Redeemed => "已兌換",
        DisplayStatus.Given => "已送出",
        DisplayStatus.Expired => "已過期",
        _ => "未兌換",
    };
}
