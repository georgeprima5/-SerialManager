namespace SerialManager.Data;

/// <summary>
/// 儲存在資料庫的狀態。「已過期」不存進資料庫，由 IsExpired 依效期判斷。
/// </summary>
public enum SerialStatus
{
    Unredeemed = 0,   // 未兌換
    Redeemed = 1,     // 已兌換
    Given = 2,        // 已送出
}

/// <summary>
/// 畫面上顯示的狀態：資料庫的三種狀態，再加上依效期判斷的「已過期」。
/// </summary>
public enum DisplayStatus
{
    Unredeemed,
    Redeemed,
    Given,
    Expired,
}

/// <summary>
/// 一筆序號，對應資料表 SerialCodes 的一列。
/// </summary>
public class SerialCode
{
    public long Id { get; set; }
    public string Code { get; set; } = "";
    public string ItemName { get; set; } = "";
    public string? PrizeLevel { get; set; }
    public DateTime? AcquiredAt { get; set; }
    public DateTime ExpiresOn { get; set; }
    public SerialStatus Status { get; set; }
    public DateTime? RedeemedAt { get; set; }
    public string? GivenTo { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// 未兌換而且已經超過效期（效期當天仍可兌換）。
    /// </summary>
    public bool IsExpired(DateTime today) =>
        Status == SerialStatus.Unredeemed && ExpiresOn.Date < today.Date;

    /// <summary>
    /// 離效期還有幾天：今天到期是 0，明天到期是 1，已經過期是負數。
    /// </summary>
    public int DaysLeft(DateTime today) => (ExpiresOn.Date - today.Date).Days;

    /// <summary>
    /// 快到期：未兌換、還沒過期，而且在指定天數內到期（包含今天與第 N 天）。
    /// </summary>
    public bool IsExpiringSoon(DateTime today, int days)
    {
        var left = DaysLeft(today);
        return Status == SerialStatus.Unredeemed && left >= 0 && left <= days;
    }

    public DisplayStatus GetDisplayStatus(DateTime today) => IsExpired(today)
        ? DisplayStatus.Expired
        : Status switch
        {
            SerialStatus.Redeemed => DisplayStatus.Redeemed,
            SerialStatus.Given => DisplayStatus.Given,
            _ => DisplayStatus.Unredeemed,
        };
}
