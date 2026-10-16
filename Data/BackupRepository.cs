using Microsoft.Data.Sqlite;

namespace SerialManager.Data;

public record RestoreResult(int Added, int Skipped);

/// <summary>
/// 備份：把整個資料庫（序號＋設定）複製成一個 .db 檔。
/// 讀取：把備份檔合併回來——序號只加入目前沒有的，設定以備份檔為準。
/// </summary>
public class BackupRepository
{
    private readonly Database _database;

    public BackupRepository(Database database)
    {
        _database = database;
    }

    public void Backup(string destinationPath)
    {
        using var source = _database.Open();
        // Pooling=False：用完立刻釋放檔案，備份檔才能馬上被移動或刪除
        using var destination = new SqliteConnection($"Data Source={destinationPath};Pooling=False");
        destination.Open();
        source.BackupDatabase(destination);
    }

    /// <exception cref="InvalidDataException">檔案不是這個程式的備份檔。</exception>
    public RestoreResult Restore(string backupPath)
    {
        using var connection = _database.Open();

        // ATTACH：把備份檔當成第二個資料庫掛上來，就能用 SQL 在兩個資料庫之間搬資料
        try
        {
            Execute(connection, "ATTACH DATABASE @path AS backup", ("@path", backupPath));
        }
        catch (SqliteException)
        {
            // 選到的不是 SQLite 檔案
            throw new InvalidDataException();
        }

        try
        {
            if (!HasSerialTable(connection))
            {
                throw new InvalidDataException();
            }

            using var transaction = connection.BeginTransaction();

            var total = Convert.ToInt32(Scalar(connection, transaction, "SELECT COUNT(*) FROM backup.SerialCodes"));

            // 序號重複（不分大小寫，包含垃圾桶裡的）就跳過，維持目前的狀態
            var added = Execute(connection, transaction, """
                INSERT INTO main.SerialCodes
                    (Code, ItemName, PrizeLevel, AcquiredAt, ExpiresOn, Status,
                     RedeemedAt, GivenTo, Note, CreatedAt, DeletedAt)
                SELECT Code, ItemName, PrizeLevel, AcquiredAt, ExpiresOn, Status,
                       RedeemedAt, GivenTo, Note, CreatedAt, DeletedAt
                FROM backup.SerialCodes
                WHERE true
                ON CONFLICT(Code) DO NOTHING
                """);

            // 設定以備份檔為準
            Execute(connection, transaction, """
                INSERT INTO main.Settings (Key, Value)
                SELECT Key, Value FROM backup.Settings
                WHERE true
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value
                """);

            transaction.Commit();
            return new RestoreResult(added, total - added);
        }
        finally
        {
            Execute(connection, "DETACH DATABASE backup");
        }
    }

    private static bool HasSerialTable(SqliteConnection connection)
    {
        try
        {
            var count = Scalar(connection, null,
                "SELECT COUNT(*) FROM backup.sqlite_master WHERE type = 'table' AND name IN ('SerialCodes', 'Settings')");
            return Convert.ToInt32(count) == 2;
        }
        catch (SqliteException)
        {
            // 選到的不是 SQLite 檔案
            return false;
        }
    }

    private static int Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters) =>
        Execute(connection, null, sql, parameters);

    private static int Execute(SqliteConnection connection, SqliteTransaction? transaction, string sql,
        params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }
        return command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, SqliteTransaction? transaction, string sql)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return command.ExecuteScalar();
    }
}
