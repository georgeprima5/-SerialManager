namespace SerialManager.Data;

/// <summary>
/// 設定資料表（Settings）的讀寫：一個 Key 對應一個文字 Value。
/// </summary>
public class SettingsRepository
{
    /// <summary>
    /// 上一次填的效期（手動新增和批次匯入共用）。
    /// </summary>
    public const string LastExpiresOnKey = "Serial.LastExpiresOn";

    /// <summary>
    /// 幾天內到期算「快到期」（預設 7，第 8 步的管理頁可以調整）。
    /// </summary>
    public const string ExpiringDaysKey = "Serial.ExpiringDays";
    public const int DefaultExpiringDays = 7;

    public int GetExpiringDays() =>
        int.TryParse(Get(ExpiringDaysKey), out var days) && days >= 0 ? days : DefaultExpiringDays;

    private readonly Database _database;

    public SettingsRepository(Database database)
    {
        _database = database;
    }

    /// <summary>
    /// 讀取設定；沒有這個 Key 時回傳 null。
    /// </summary>
    public string? Get(string key)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM Settings WHERE Key = @key";
        command.Parameters.AddWithValue("@key", key);
        return command.ExecuteScalar() as string;
    }

    /// <summary>
    /// 寫入設定；Key 已存在就更新。
    /// </summary>
    public void Set(string key, string value)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO Settings (Key, Value) VALUES (@key, @value)
            ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
            """;
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }
}
