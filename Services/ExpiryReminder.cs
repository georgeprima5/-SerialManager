using SerialManager.Data;

namespace SerialManager.Services;

/// <summary>
/// 到期通知的內容：只算「未兌換、還沒過期、N 天內到期」的序號。沒有就不通知（回傳 null）。
/// </summary>
public static class ExpiryReminder
{
    public static string? BuildMessage(IEnumerable<SerialCode> items, DateTime today, int days)
    {
        var count = items.Count(item => item.IsExpiringSoon(today, days));
        if (count == 0)
        {
            return null;
        }

        return days == 0
            ? $"有 {count} 筆序號今天到期"
            : $"有 {count} 筆序號將在 {days} 天內到期";
    }
}
