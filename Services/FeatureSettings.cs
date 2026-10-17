using System.Globalization;
using SerialManager.Data;

namespace SerialManager.Services;

/// <summary>
/// 管理頁的設定：功能開關、欄位開關、數值設定。關掉的功能或欄位在畫面上完全隱藏，資料不會刪除。
/// </summary>
public class FeatureSettings
{
    // 功能開關
    private const string ImportKey = "Feature.Import";
    private const string ExportKey = "Feature.Export";
    private const string NotifyKey = "Feature.Notify";
    private const string CopyAskRedeemKey = "Feature.CopyAskRedeem";

    // 欄位開關
    private const string GiveKey = "Field.Give";
    private const string PrizeLevelKey = "Field.PrizeLevel";
    private const string AcquiredAtKey = "Field.AcquiredAt";
    private const string NoteKey = "Field.Note";

    // 數值設定
    private const string NotifyIntervalKey = "Notify.IntervalHours";
    public const int DefaultNotifyIntervalHours = 24;
    public const int MinExpiringDays = 0, MaxExpiringDays = 365;
    public const int MinNotifyIntervalHours = 1, MaxNotifyIntervalHours = 168;

    private readonly SettingsRepository _settings;

    public bool ImportEnabled { get; private set; }
    public bool ExportEnabled { get; private set; }
    public bool NotifyEnabled { get; private set; }
    public bool CopyAskRedeemEnabled { get; private set; }
    public bool GiveEnabled { get; private set; }
    public bool PrizeLevelVisible { get; private set; }
    public bool AcquiredAtVisible { get; private set; }
    public bool NoteVisible { get; private set; }
    public int ExpiringDays { get; private set; }
    public int NotifyIntervalHours { get; private set; }

    /// <summary>任何設定改變時觸發（例如到期通知要重新排定檢查時間）。</summary>
    public event Action? Changed;

    public FeatureSettings(SettingsRepository settings)
    {
        _settings = settings;
        Load();
    }

    public void SetImport(bool value) => SetFlag(ImportKey, value);
    public void SetExport(bool value) => SetFlag(ExportKey, value);
    public void SetNotify(bool value) => SetFlag(NotifyKey, value);
    public void SetCopyAskRedeem(bool value) => SetFlag(CopyAskRedeemKey, value);
    public void SetGive(bool value) => SetFlag(GiveKey, value);
    public void SetPrizeLevel(bool value) => SetFlag(PrizeLevelKey, value);
    public void SetAcquiredAt(bool value) => SetFlag(AcquiredAtKey, value);
    public void SetNote(bool value) => SetFlag(NoteKey, value);

    /// <summary>匯出欄位是否可用：欄位開關關掉的欄位不顯示在匯出設定裡，也不匯出。</summary>
    public bool IsExportFieldVisible(ExportField field) => field switch
    {
        ExportField.PrizeLevel => PrizeLevelVisible,
        ExportField.AcquiredAt => AcquiredAtVisible,
        ExportField.GivenTo => GiveEnabled,
        ExportField.Note => NoteVisible,
        _ => true,
    };

    /// <summary>快到期天數（超出範圍會自動調整到範圍內）。</summary>
    public void SetExpiringDays(int days)
    {
        _settings.Set(SettingsRepository.ExpiringDaysKey,
            Math.Clamp(days, MinExpiringDays, MaxExpiringDays).ToString(CultureInfo.InvariantCulture));
        Reload();
    }

    /// <summary>通知檢查頻率（小時，超出範圍會自動調整到範圍內）。</summary>
    public void SetNotifyIntervalHours(int hours)
    {
        _settings.Set(NotifyIntervalKey,
            Math.Clamp(hours, MinNotifyIntervalHours, MaxNotifyIntervalHours).ToString(CultureInfo.InvariantCulture));
        Reload();
    }

    /// <summary>重新從資料庫讀取（讀取備份後使用）。</summary>
    public void Reload()
    {
        Load();
        Changed?.Invoke();
    }

    private void SetFlag(string key, bool value)
    {
        _settings.Set(key, value ? "true" : "false");
        Reload();
    }

    private void Load()
    {
        ImportEnabled = GetFlag(ImportKey, true);
        ExportEnabled = GetFlag(ExportKey, true);
        NotifyEnabled = GetFlag(NotifyKey, true);
        CopyAskRedeemEnabled = GetFlag(CopyAskRedeemKey, false);   // 預設不詢問
        GiveEnabled = GetFlag(GiveKey, true);
        PrizeLevelVisible = GetFlag(PrizeLevelKey, true);
        AcquiredAtVisible = GetFlag(AcquiredAtKey, false);   // 預設不顯示取得時間
        NoteVisible = GetFlag(NoteKey, true);
        ExpiringDays = Math.Clamp(_settings.GetExpiringDays(), MinExpiringDays, MaxExpiringDays);
        NotifyIntervalHours = int.TryParse(_settings.Get(NotifyIntervalKey), out var hours)
            ? Math.Clamp(hours, MinNotifyIntervalHours, MaxNotifyIntervalHours)
            : DefaultNotifyIntervalHours;
    }

    // 沒設定過就用預設值
    private bool GetFlag(string key, bool defaultValue) => _settings.Get(key) switch
    {
        "true" => true,
        "false" => false,
        _ => defaultValue,
    };
}
