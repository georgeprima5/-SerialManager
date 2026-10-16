using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SerialManager.Data;

public enum AddResult
{
    Added,
    Duplicate,   // 序號已存在（不分大小寫，包含垃圾桶裡的）
}

/// <summary>批次匯入的結果：新增幾筆，以及哪些序號因為重複而略過。</summary>
public record ImportResult(int Added, List<string> Duplicates);

/// <summary>
/// 序號資料表的查詢與寫入（SQL 都寫在這裡）。
/// </summary>
public class SerialRepository
{
    private readonly Database _database;

    public SerialRepository(Database database)
    {
        _database = database;
    }

    /// <summary>
    /// 取得所有不在垃圾桶裡的序號，效期近的排前面。
    /// </summary>
    public List<SerialCode> GetAll() =>
        Query("WHERE DeletedAt IS NULL ORDER BY ExpiresOn, Id");

    /// <summary>
    /// 取得垃圾桶裡的序號，最近刪除的排前面。
    /// </summary>
    public List<SerialCode> GetTrash() =>
        Query("WHERE DeletedAt IS NOT NULL ORDER BY DeletedAt DESC, Id DESC");

    public int CountTrash()
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM SerialCodes WHERE DeletedAt IS NOT NULL";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    /// <summary>
    /// 刪除：只是標記刪除時間，資料還在，可以從垃圾桶復原。
    /// </summary>
    public void MoveToTrash(IEnumerable<long> ids) =>
        ExecuteForEach(ids, "UPDATE SerialCodes SET DeletedAt = @now WHERE Id = @id AND DeletedAt IS NULL");

    public void RestoreFromTrash(IEnumerable<long> ids) =>
        ExecuteForEach(ids, "UPDATE SerialCodes SET DeletedAt = NULL WHERE Id = @id");

    /// <summary>
    /// 永久刪除：真的從資料庫刪掉，無法復原。只刪得掉已經在垃圾桶裡的。
    /// </summary>
    public void DeletePermanently(IEnumerable<long> ids) =>
        ExecuteForEach(ids, "DELETE FROM SerialCodes WHERE Id = @id AND DeletedAt IS NOT NULL");

    public void EmptyTrash() =>
        Execute("DELETE FROM SerialCodes WHERE DeletedAt IS NOT NULL");

    private List<SerialCode> Query(string whereAndOrder)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT Id, Code, ItemName, PrizeLevel, AcquiredAt, ExpiresOn, Status,
                   RedeemedAt, GivenTo, Note, CreatedAt, DeletedAt
            FROM SerialCodes
            {whereAndOrder}
            """;

        var list = new List<SerialCode>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            list.Add(Map(reader));
        }
        return list;
    }

    /// <summary>
    /// 批次標記已兌換。本來就是已兌換的不動（兌換時間不會被改掉）；已送出的也可以標（送給誰保留）。
    /// 回傳實際變更的筆數。
    /// </summary>
    public int MarkRedeemedMany(IEnumerable<long> ids) =>
        ExecuteForEach(ids, "UPDATE SerialCodes SET Status = 1, RedeemedAt = @now WHERE Id = @id AND Status <> 1");

    /// <summary>
    /// 批次送出給同一個人。只變更「未兌換」的序號，已兌換、已送出的不動。回傳實際變更的筆數。
    /// </summary>
    public int GiveToMany(IEnumerable<long> ids, string name) =>
        ExecuteForEach(ids, "UPDATE SerialCodes SET Status = 2, GivenTo = @name WHERE Id = @id AND Status = 0",
            ("@name", name));

    /// <summary>
    /// 批次改回未兌換：不管原本是已兌換還是已送出，一律清掉兌換時間和送給誰。回傳實際變更的筆數。
    /// </summary>
    public int ResetToUnredeemedMany(IEnumerable<long> ids) =>
        ExecuteForEach(ids, "UPDATE SerialCodes SET Status = 0, RedeemedAt = NULL, GivenTo = NULL WHERE Id = @id AND Status <> 0");

    /// <summary>
    /// 批次更改效期：不管狀態，勾選的每一筆都改成同一個日期。回傳變更的筆數。
    /// </summary>
    public int UpdateExpiresOnMany(IEnumerable<long> ids, DateTime expiresOn) =>
        ExecuteForEach(ids, "UPDATE SerialCodes SET ExpiresOn = @expiresOn WHERE Id = @id",
            ("@expiresOn", FormatDate(expiresOn)));

    /// <summary>
    /// 對每個 Id 執行同一句 SQL（@id、@now 會自動帶入），整批在同一個交易裡，中途出錯就全部不算。
    /// 回傳總共影響的筆數。
    /// </summary>
    private int ExecuteForEach(IEnumerable<long> ids, string sql, params (string Name, object Value)[] extra)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();
        var now = FormatDateTime(DateTime.Now);
        var affected = 0;

        foreach (var id in ids)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;
            command.Parameters.AddWithValue("@id", id);
            command.Parameters.AddWithValue("@now", now);
            foreach (var (name, value) in extra)
            {
                command.Parameters.AddWithValue(name, value);
            }
            affected += command.ExecuteNonQuery();
        }

        transaction.Commit();
        return affected;
    }

    /// <summary>
    /// 手動新增一筆。序號已存在就不新增。
    /// </summary>
    public AddResult Add(string code, string itemName, string? prizeLevel, DateTime expiresOn)
    {
        var affected = Execute("""
            INSERT INTO SerialCodes (Code, ItemName, PrizeLevel, ExpiresOn, Status, CreatedAt)
            VALUES (@code, @itemName, @prizeLevel, @expiresOn, 0, @createdAt)
            ON CONFLICT(Code) DO NOTHING
            """,
            ("@code", code),
            ("@itemName", itemName),
            ("@prizeLevel", prizeLevel),
            ("@expiresOn", FormatDate(expiresOn)),
            ("@createdAt", FormatDateTime(DateTime.Now)));

        return affected == 1 ? AddResult.Added : AddResult.Duplicate;
    }

    /// <summary>
    /// 批次匯入：整批使用同一個效期。序號已存在（包含同一批裡重複貼兩次的）就整筆略過，原本的狀態不動。
    /// </summary>
    public ImportResult Import(IEnumerable<ImportRow> rows, DateTime expiresOn)
    {
        using var connection = _database.Open();
        using var transaction = connection.BeginTransaction();

        var added = 0;
        var duplicates = new List<string>();
        var createdAt = FormatDateTime(DateTime.Now);

        foreach (var row in rows)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO SerialCodes (Code, ItemName, PrizeLevel, AcquiredAt, ExpiresOn, Status, CreatedAt)
                VALUES (@code, @itemName, @prizeLevel, @acquiredAt, @expiresOn, 0, @createdAt)
                ON CONFLICT(Code) DO NOTHING
                """;
            command.Parameters.AddWithValue("@code", row.Code);
            command.Parameters.AddWithValue("@itemName", row.ItemName);
            command.Parameters.AddWithValue("@prizeLevel", (object?)row.PrizeLevel ?? DBNull.Value);
            command.Parameters.AddWithValue("@acquiredAt", row.AcquiredAt is { } at ? FormatDateTime(at) : (object)DBNull.Value);
            command.Parameters.AddWithValue("@expiresOn", FormatDate(expiresOn));
            command.Parameters.AddWithValue("@createdAt", createdAt);

            if (command.ExecuteNonQuery() == 1)
            {
                added++;
            }
            else
            {
                duplicates.Add(row.Code);
            }
        }

        transaction.Commit();
        return new ImportResult(added, duplicates);
    }

    /// <summary>
    /// 修改備註；空白就存成 NULL。
    /// </summary>
    public void UpdateNote(long id, string? note) =>
        Execute("UPDATE SerialCodes SET Note = @note WHERE Id = @id",
            ("@note", string.IsNullOrWhiteSpace(note) ? null : note.Trim()), ("@id", id));

    /// <summary>
    /// 勾選「已兌換」：記錄兌換時間。已送出的也可以勾（送給誰會保留）。
    /// </summary>
    public void MarkRedeemed(long id) =>
        Execute("UPDATE SerialCodes SET Status = 1, RedeemedAt = @now WHERE Id = @id",
            ("@now", FormatDateTime(DateTime.Now)), ("@id", id));

    /// <summary>
    /// 取消勾選「已兌換」：清掉兌換時間；有送給誰就回到「已送出」，否則回到「未兌換」。
    /// </summary>
    public void UnmarkRedeemed(long id) =>
        Execute("""
            UPDATE SerialCodes
            SET Status = CASE WHEN GivenTo IS NOT NULL AND GivenTo <> '' THEN 2 ELSE 0 END,
                RedeemedAt = NULL
            WHERE Id = @id
            """, ("@id", id));

    public void GiveTo(long id, string name) =>
        Execute("UPDATE SerialCodes SET Status = 2, GivenTo = @name WHERE Id = @id",
            ("@name", name), ("@id", id));

    /// <summary>
    /// 收回：已送出改回未兌換，清掉送給誰。
    /// </summary>
    public void TakeBack(long id) =>
        Execute("UPDATE SerialCodes SET Status = 0, GivenTo = NULL WHERE Id = @id", ("@id", id));

    private int Execute(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            // C# 的 null 要轉成 DBNull.Value，資料庫才會存成 NULL
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }
        return command.ExecuteNonQuery();
    }

    private static string FormatDate(DateTime value) =>
        value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string FormatDateTime(DateTime value) =>
        value.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static SerialCode Map(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt64(0),
        Code = reader.GetString(1),
        ItemName = reader.GetString(2),
        PrizeLevel = GetNullableString(reader, 3),
        AcquiredAt = GetNullableDate(reader, 4),
        ExpiresOn = ParseDate(reader.GetString(5)),
        Status = (SerialStatus)reader.GetInt32(6),
        RedeemedAt = GetNullableDate(reader, 7),
        GivenTo = GetNullableString(reader, 8),
        Note = GetNullableString(reader, 9),
        CreatedAt = ParseDate(reader.GetString(10)),
        DeletedAt = GetNullableDate(reader, 11),
    };

    // 資料庫的 NULL 要先用 IsDBNull 判斷，不能直接 GetString
    private static string? GetNullableString(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : reader.GetString(index);

    private static DateTime? GetNullableDate(SqliteDataReader reader, int index) =>
        reader.IsDBNull(index) ? null : ParseDate(reader.GetString(index));

    private static DateTime ParseDate(string value) =>
        DateTime.Parse(value, CultureInfo.InvariantCulture);
}
