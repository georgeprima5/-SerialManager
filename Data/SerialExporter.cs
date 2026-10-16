using ClosedXML.Excel;

namespace SerialManager.Data;

/// <summary>
/// 匯出要包含哪些欄位（管理頁關掉的欄位不匯出）。
/// </summary>
public record ExportColumns(bool PrizeLevel, bool AcquiredAt, bool Give, bool Note);

/// <summary>
/// 批次匯出：把勾選的序號轉成表格，再輸出成 Excel 檔或 Tab 分隔的文字（貼到 Excel 會自動分欄）。
/// </summary>
public static class SerialExporter
{
    /// <summary>
    /// 第一列是標題，之後每筆序號一列。所有值都是文字，序號才不會被 Excel 當成數字（例如前面的 0 不見）。
    /// </summary>
    public static List<string[]> BuildTable(IEnumerable<SerialCode> items, ExportColumns columns, DateTime today)
    {
        var header = new List<string> { "序號", "內容" };
        if (columns.PrizeLevel) header.Add("獎項等級");
        if (columns.AcquiredAt) header.Add("取得時間");
        header.AddRange(["效期", "狀態", "兌換時間"]);
        if (columns.Give) header.Add("送給誰");
        if (columns.Note) header.Add("備註");

        var table = new List<string[]> { header.ToArray() };
        foreach (var item in items)
        {
            var row = new List<string> { item.Code, item.ItemName };
            if (columns.PrizeLevel) row.Add(item.PrizeLevel ?? "");
            if (columns.AcquiredAt) row.Add(item.AcquiredAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "");
            row.Add(item.ExpiresOn.ToString("yyyy/MM/dd"));
            row.Add(StatusText(item.GetDisplayStatus(today)));
            row.Add(item.RedeemedAt?.ToString("yyyy/MM/dd HH:mm:ss") ?? "");
            if (columns.Give) row.Add(item.GivenTo ?? "");
            if (columns.Note) row.Add(item.Note ?? "");
            table.Add(row.ToArray());
        }
        return table;
    }

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
