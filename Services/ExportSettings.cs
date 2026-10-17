using SerialManager.Data;

namespace SerialManager.Services;

/// <summary>匯出到哪裡：「匯出 Excel」和「複製文字」各有一組欄位設定。</summary>
public enum ExportTarget { Excel, Text }

/// <summary>匯出設定裡的一個欄位：要不要匯出。</summary>
public record ExportFieldItem(ExportField Field, bool Enabled);

/// <summary>
/// 匯出欄位設定：每組各自決定要匯出哪些欄位、順序，以及複製文字要不要包含標題列。
/// 存成「Code:1,ItemName:1,…」的文字，1 是要匯出、0 是不匯出，順序就是欄位順序。
/// 管理頁「欄位開關」關掉的欄位不顯示也不匯出，但原本的勾選與順序會保留。
/// </summary>
public class ExportSettings
{
    private const string ExcelFieldsKey = "Export.Excel.Fields";
    private const string TextFieldsKey = "Export.Text.Fields";
    private const string TextHeaderKey = "Export.Text.Header";

    private readonly SettingsRepository _settings;
    private readonly Dictionary<ExportTarget, List<ExportFieldItem>> _items = new();

    /// <summary>複製文字的第一行要不要放標題（序號、內容…）。Excel 一律有標題列。</summary>
    public bool TextIncludeHeader { get; private set; }

    public event Action? Changed;

    public ExportSettings(SettingsRepository settings)
    {
        _settings = settings;
        Load();
    }

    /// <summary>設定頁用：這一組的全部欄位（包含沒勾的），依目前順序。</summary>
    public IReadOnlyList<ExportFieldItem> GetItems(ExportTarget target) => _items[target];

    /// <summary>匯出用：這一組要匯出的欄位，依目前順序；isVisible 回傳 false 的欄位（欄位開關關掉的）跳過。</summary>
    public IReadOnlyList<ExportField> GetFields(ExportTarget target, Func<ExportField, bool> isVisible) =>
        _items[target].Where(item => item.Enabled && isVisible(item.Field)).Select(item => item.Field).ToList();

    /// <summary>勾選或取消一個欄位。序號一定要匯出，不能取消。</summary>
    public void SetEnabled(ExportTarget target, ExportField field, bool enabled)
    {
        if (field == ExportField.Code)
        {
            return;
        }
        var list = _items[target];
        var index = list.FindIndex(item => item.Field == field);
        list[index] = list[index] with { Enabled = enabled };
        Save(target);
    }

    /// <summary>
    /// 往上（direction = -1）或往下（+1）移一格：跟上面或下面「看得到的」欄位交換位置。
    /// </summary>
    public void Move(ExportTarget target, ExportField field, int direction, Func<ExportField, bool> isVisible)
    {
        var list = _items[target];
        var index = list.FindIndex(item => item.Field == field);
        var other = index + direction;
        while (other >= 0 && other < list.Count && !isVisible(list[other].Field))
        {
            other += direction;
        }
        if (other < 0 || other >= list.Count)
        {
            return;   // 已經在最上面或最下面
        }
        (list[index], list[other]) = (list[other], list[index]);
        Save(target);
    }

    public void SetTextIncludeHeader(bool value)
    {
        _settings.Set(TextHeaderKey, value ? "true" : "false");
        Reload();
    }

    /// <summary>重新從資料庫讀取（讀取備份後使用）。</summary>
    public void Reload()
    {
        Load();
        Changed?.Invoke();
    }

    private void Save(ExportTarget target)
    {
        var text = string.Join(",", _items[target].Select(item => $"{item.Field}:{(item.Enabled ? 1 : 0)}"));
        _settings.Set(target == ExportTarget.Excel ? ExcelFieldsKey : TextFieldsKey, text);
        Reload();
    }

    private void Load()
    {
        _items[ExportTarget.Excel] = Parse(_settings.Get(ExcelFieldsKey));
        _items[ExportTarget.Text] = Parse(_settings.Get(TextFieldsKey));
        TextIncludeHeader = _settings.Get(TextHeaderKey) != "false";   // 預設包含標題列
    }

    /// <summary>
    /// 讀取存檔的文字。沒存過時全部欄位都匯出、依預設順序；
    /// 看不懂的項目略過，存檔裡沒有的欄位（例如以後新增的）補在最後並預設匯出。
    /// </summary>
    private static List<ExportFieldItem> Parse(string? text)
    {
        var list = new List<ExportFieldItem>();
        foreach (var part in (text ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(':');
            if (pieces.Length == 2 && Enum.TryParse<ExportField>(pieces[0], out var field)
                && list.All(item => item.Field != field))
            {
                list.Add(new(field, pieces[1] != "0" || field == ExportField.Code));
            }
        }
        foreach (var field in Enum.GetValues<ExportField>())
        {
            if (list.All(item => item.Field != field))
            {
                list.Add(new(field, true));
            }
        }
        return list;
    }
}
