namespace SerialManager.Services;

/// <summary>
/// 讓畫面（.razor）可以控制視窗：拖曳、最小化、最大化、關閉，以及開啟檔案選擇視窗。
/// </summary>
public class WindowService
{
    private readonly MainForm _form;

    public WindowService(MainForm form)
    {
        _form = form;
        _form.WindowStateChanged += () => StateChanged?.Invoke();
    }

    /// <summary>
    /// 最大化／還原時觸發，標題列用來切換按鈕圖示。
    /// </summary>
    public event Action? StateChanged;

    public bool IsMaximized => _form.WindowState == FormWindowState.Maximized;

    public bool CloseToTray => _form.CloseToTray;

    public void BeginDrag() => _form.BeginDrag();

    public void Minimize() => _form.WindowState = FormWindowState.Minimized;

    public void ToggleMaximize() => _form.ToggleMaximize();

    /// <summary>
    /// 等同按 X：依設定收進系統匣或結束。
    /// </summary>
    public void Close() => _form.Close();

    public void SetCloseToTray(bool value) => _form.SetCloseToTray(value);

    public void CopyText(string text) => Clipboard.SetText(text);

    public void ReloadSettings() => _form.LoadWindowSettings();

    /// <summary>
    /// 選擇備份檔要存在哪裡；取消時回傳 null。
    /// </summary>
    public string? AskBackupSavePath()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "序號備份 (*.db)|*.db",
            FileName = $"序號備份_{DateTime.Now:yyyyMMdd_HHmmss}.db",
        };
        return dialog.ShowDialog(_form) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>
    /// 選擇匯出的 Excel 檔要存在哪裡；取消時回傳 null。
    /// </summary>
    public string? AskExportSavePath()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "Excel 檔案 (*.xlsx)|*.xlsx",
            FileName = $"序號匯出_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
        };
        return dialog.ShowDialog(_form) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>
    /// 到期通知被點擊時觸發：畫面切到序號列表，只看「快到期」的。
    /// </summary>
    public event Action? ExpiringNotificationClicked;

    public void RaiseExpiringNotificationClicked() => ExpiringNotificationClicked?.Invoke();

    /// <summary>
    /// 選擇要讀取的備份檔；取消時回傳 null。
    /// </summary>
    public string? AskBackupOpenPath()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "序號備份 (*.db)|*.db",
        };
        return dialog.ShowDialog(_form) == DialogResult.OK ? dialog.FileName : null;
    }
}
