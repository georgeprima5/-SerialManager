using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SerialManager.Data;

/// <summary>解析成功的一筆。RecordNumber 是貼上的第幾筆資料（從 1 開始）。</summary>
public record ImportRow(int RecordNumber, DateTime? AcquiredAt, string? PrizeLevel, string ItemName, string Code);

/// <summary>格式有問題的一筆。</summary>
public record ImportError(int RecordNumber, string Reason, string RawText);

public record ParseResult(List<ImportRow> Rows, List<ImportError> Errors);

/// <summary>
/// 解析從 Excel／Google 試算表複製出來的文字，接受三種格式：
///   4 欄：取得時間 [Tab] 獎項等級 [Tab] 內容 [Tab] 序號
///   3 欄：獎項等級 [Tab] 內容 [Tab] 序號（沒有取得時間）
///   2 欄：內容 [Tab] 序號（沒有取得時間、獎項等級）
/// 儲存格裡有換行時，Excel 會用雙引號把整格包起來，例如 "09/16(換行)19:49:52"；
/// 沒有雙引號、日期和時間分在兩行的情況也能處理。
/// 一行裡沒有 Tab 時改用空白分欄（見 SplitByWhitespace）。
/// </summary>
public static class SerialImportParser
{
    private const int FullColumnCount = 4;    // 取得時間、獎項等級、內容、序號
    private const int ShortColumnCount = 3;   // 獎項等級、內容、序號
    private const int MinimalColumnCount = 2; // 內容、序號

    /// <param name="now">現在時間；取得時間沒有年份時，用它判斷是今年還是去年。</param>
    public static ParseResult Parse(string text, DateTime now)
    {
        var rows = new List<ImportRow>();
        var errors = new List<ImportError>();
        var recordNumber = 0;

        foreach (var record in MergeSplitDateLines(SplitRecords(text).ToList()))
        {
            var fields = record.Select(f => f.Trim()).ToList();
            if (fields.Count == 1)
            {
                fields = SplitByWhitespace(fields[0]);   // 沒有 Tab：改用空白分欄
            }
            if (fields.All(f => f.Length == 0))
            {
                continue;   // 空白行
            }

            // 去掉第 4 欄之後多餘的空白欄（從 Excel 多選到空白欄時會出現）；空的序號欄要保留，才能提示「沒有序號」
            while (fields.Count > FullColumnCount && fields[^1].Length == 0)
            {
                fields.RemoveAt(fields.Count - 1);
            }

            recordNumber++;
            var rawText = string.Join("  ", fields.Select(f => f.Replace('\n', ' ')));

            if (fields.Count == ShortColumnCount)
            {
                // 3 欄但第 1 欄是日期：多半是 4 欄少了一欄，不能把日期當成獎項等級存進去
                if (TryParseAcquiredAt(fields[0], now, out _))
                {
                    errors.Add(new(recordNumber, "欄位數量不對（有取得時間時應該是 4 欄，這筆有 3 欄）", rawText));
                    continue;
                }
                fields.Insert(0, "");   // 沒有取得時間
            }
            else if (fields.Count == MinimalColumnCount)
            {
                // 2 欄但第 1 欄是日期：不能把日期當成內容存進去
                if (TryParseAcquiredAt(fields[0], now, out _))
                {
                    errors.Add(new(recordNumber, "欄位數量不對（有取得時間時應該是 4 欄，這筆有 2 欄）", rawText));
                    continue;
                }
                fields.InsertRange(0, ["", ""]);   // 沒有取得時間、獎項等級
            }

            if (fields.Count != FullColumnCount)
            {
                errors.Add(new(recordNumber, $"欄位數量不對（應該是 2、3 或 4 欄，這筆有 {fields.Count} 欄）", rawText));
                continue;
            }

            var code = fields[3];
            if (code.Length == 0)
            {
                errors.Add(new(recordNumber, "沒有序號", rawText));
                continue;
            }

            DateTime? acquiredAt = null;
            if (fields[0].Length > 0)
            {
                if (!TryParseAcquiredAt(fields[0], now, out var parsed))
                {
                    errors.Add(new(recordNumber, "取得時間格式不正確", rawText));
                    continue;
                }
                acquiredAt = parsed;
            }

            var prizeLevel = fields[1].Length == 0 ? null : fields[1];
            rows.Add(new(recordNumber, acquiredAt, prizeLevel, fields[2], code));
        }

        return new(rows, errors);
    }

    /// <summary>
    /// 沒有雙引號時，「09/16(換行)19:49:52 [Tab] B …」會被切成兩行：
    /// 第一行只有日期，第二行從時間開始。遇到這種情況就把兩行合併回同一筆。
    /// </summary>
    private static IEnumerable<List<string>> MergeSplitDateLines(List<List<string>> records)
    {
        for (var i = 0; i < records.Count; i++)
        {
            var current = records[i];
            var isDateOnly = current.Count(f => f.Trim().Length > 0) == 1 && IsMonthDay(current[0].Trim());
            if (isDateOnly && i + 1 < records.Count && records[i + 1].Count > 0 && StartsWithTime(records[i + 1][0].Trim()))
            {
                var next = records[i + 1];
                var merged = new List<string> { current[0].Trim() + " " + next[0].Trim() };
                merged.AddRange(next.Skip(1));
                yield return merged;
                i++;   // 下一行已經合併進來了
                continue;
            }
            yield return current;
        }
    }

    // 「09/16」這種只有月／日的寫法
    private static bool IsMonthDay(string value)
    {
        var parts = value.Split('/');
        return parts.Length == 2 && parts.All(p => p.Length is >= 1 and <= 2 && p.All(char.IsDigit));
    }

    // 「19:49:52」或「19:49」開頭；後面可以接空白和其他欄位（沒有 Tab、用空白分隔時）
    private static bool StartsWithTime(string value) => LeadingTimeRegex.IsMatch(value);

    private static readonly Regex LeadingTimeRegex = new(@"^\d{1,2}:\d{2}(:\d{2})?(\s|$)");

    /// <summary>
    /// 沒有 Tab、用空白（一個或多個）分隔的一行，拆成欄位：
    ///   開頭是日期（可加時間）→ 取得時間；最後一個字串 → 序號；
    ///   中間剩 2 個字串以上 → 第一個是獎項等級、其餘是內容（內容可以有空白）；只剩 1 個 → 內容。
    /// 拆不出來（例如整行只有一個字串）就原樣回傳，交給欄位數檢查報錯。
    /// </summary>
    private static List<string> SplitByWhitespace(string line)
    {
        var match = WhitespaceLineRegex.Match(line);
        if (!match.Success)
        {
            return [line];
        }

        var fields = new List<string>();
        if (match.Groups["at"].Success)
        {
            fields.Add(Regex.Replace(match.Groups["at"].Value, @"\s+", " "));
        }
        var rest = match.Groups["rest"].Value;
        var parts = rest.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2)
        {
            fields.Add(parts[0]);                              // 獎項等級
            fields.Add(Regex.Replace(parts[1], @"\s+", " "));  // 內容
        }
        else
        {
            if (fields.Count > 0)
            {
                fields.Add("");   // 有取得時間但只剩一個字串：當成沒有獎項等級
            }
            fields.Add(rest);
        }
        fields.Add(match.Groups["code"].Value);
        return fields;
    }

    // [取得時間：月/日 或 年/月/日，可加時:分(:秒)] 中間 序號（最後一個不含空白的字串）
    // 中間不能以時間開頭，避免「日期 時間 序號」被拆成「日期」＋內容「時間」
    private static readonly Regex WhitespaceLineRegex = new(
        @"^(?:(?<at>(?:\d{4}/)?\d{1,2}/\d{1,2}(?:\s+\d{1,2}:\d{2}(?::\d{2})?)?)\s+)?(?<rest>(?!\d{1,2}:\d{2}(?::\d{2})?\s)\S.*?)\s+(?<code>\S+)$",
        RegexOptions.Singleline);

    /// <summary>
    /// 把文字切成一筆一筆、每筆一個欄位清單。雙引號裡面的 Tab 和換行屬於儲存格內容，不切開。
    /// </summary>
    private static IEnumerable<List<string>> SplitRecords(string text)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var atFieldStart = true;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');   // 兩個雙引號代表一個雙引號字元
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else if (ch != '\r')
                {
                    field.Append(ch);
                }
                continue;
            }

            switch (ch)
            {
                case '"' when atFieldStart:
                    inQuotes = true;
                    atFieldStart = false;
                    break;
                case '\t':
                    record.Add(field.ToString());
                    field.Clear();
                    atFieldStart = true;
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    yield return record;
                    record = new List<string>();
                    atFieldStart = true;
                    break;
                default:
                    field.Append(ch);
                    atFieldStart = false;
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }

    /// <summary>
    /// 解析「09/16 19:49:52」（日期和時間之間可以是空白或換行）。
    /// 沒有年份時用今年；算出來比現在還晚，就往前一年（例如 1 月匯入 12/30 的序號）。
    /// </summary>
    private static bool TryParseAcquiredAt(string value, DateTime now, out DateTime result)
    {
        result = default;
        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is < 1 or > 2)
        {
            return false;
        }

        var dateParts = parts[0].Split('/');
        if (dateParts.Length is < 2 or > 3 || !dateParts.All(p => int.TryParse(p, out _)))
        {
            return false;
        }

        var time = TimeSpan.Zero;
        if (parts.Length == 2 &&
            !TimeSpan.TryParseExact(parts[1], [@"h\:mm\:ss", @"h\:mm"], CultureInfo.InvariantCulture, out time))
        {
            return false;
        }

        var numbers = dateParts.Select(int.Parse).ToArray();
        if (numbers.Length == 3)
        {
            // 有年份：yyyy/MM/dd
            return TryCreate(numbers[0], numbers[1], numbers[2], time, out result);
        }

        // 沒有年份：MM/dd，從今年往前找第一個合理的年份（2/29 會找到最近的閏年）
        for (var year = now.Year; year >= now.Year - 8; year--)
        {
            if (TryCreate(year, numbers[0], numbers[1], time, out result) && result <= now)
            {
                return true;
            }
        }
        return false;
    }

    private static bool TryCreate(int year, int month, int day, TimeSpan time, out DateTime result)
    {
        result = default;
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }
        result = new DateTime(year, month, day).Add(time);
        return true;
    }
}
