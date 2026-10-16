using Microsoft.Data.Sqlite;

namespace SerialManager.Data;

/// <summary>
/// 負責 SQLite 連線與建立資料表。
/// </summary>
public class Database
{
    private readonly string _connectionString;

    public Database(string dbPath)
    {
        _connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
    }

    /// <summary>
    /// 開啟一條連線。呼叫端用 using 包起來，用完自動關閉。
    /// </summary>
    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    /// <summary>
    /// 資料表不存在才建立，已存在就不動（不會清掉資料）。
    /// </summary>
    public void EnsureCreated()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = Schema;
        command.ExecuteNonQuery();
    }

    // Code 用 COLLATE NOCASE：比對重複時不分大小寫
    // 日期一律存成文字（ISO 格式），SQLite 沒有專門的日期型別
    private const string Schema = """
        CREATE TABLE IF NOT EXISTS SerialCodes (
            Id          INTEGER PRIMARY KEY AUTOINCREMENT,
            Code        TEXT    NOT NULL COLLATE NOCASE UNIQUE,
            ItemName    TEXT    NOT NULL DEFAULT '',
            PrizeLevel  TEXT    NULL,
            AcquiredAt  TEXT    NULL,
            ExpiresOn   TEXT    NOT NULL,
            Status      INTEGER NOT NULL DEFAULT 0,
            RedeemedAt  TEXT    NULL,
            GivenTo     TEXT    NULL,
            Note        TEXT    NULL,
            CreatedAt   TEXT    NOT NULL,
            DeletedAt   TEXT    NULL
        );

        CREATE TABLE IF NOT EXISTS Settings (
            Key   TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );
        """;
}
