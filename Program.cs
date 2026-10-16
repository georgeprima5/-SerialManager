using System.Runtime.InteropServices;

namespace SerialManager;

internal static class Program
{
    // 同一個 Windows 使用者登入期間共用的名稱，用來判斷程式是否已經在執行
    private const string InstanceMutexName = @"Local\SerialManager.SingleInstance";
    private const string ShowWindowEventName = @"Local\SerialManager.ShowWindow";

    /// <summary>
    /// 程式進入點：只允許開一個程式。已經有一個在執行時，請它把視窗叫出來，自己直接結束。
    /// </summary>
    [STAThread]
    static void Main()
    {
        using var instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            AskRunningInstanceToShow();
            return;
        }

        ApplicationConfiguration.Initialize();

        using var showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowWindowEventName);
        var form = new MainForm();

        // 在背景等待「把視窗叫出來」的通知，收到後回到視窗的執行緒處理
        var waitHandle = ThreadPool.RegisterWaitForSingleObject(showWindowEvent, (_, _) =>
        {
            if (form.IsHandleCreated)
            {
                form.BeginInvoke(form.RestoreFromTray);
            }
        }, null, Timeout.Infinite, executeOnlyOnce: false);

        Application.Run(form);
        waitHandle.Unregister(null);
    }

    private static void AskRunningInstanceToShow()
    {
        if (!EventWaitHandle.TryOpenExisting(ShowWindowEventName, out var showWindowEvent))
        {
            return;   // 原本的程式還在啟動中，還沒準備好接收通知
        }

        using (showWindowEvent)
        {
            // Windows 預設不讓背景程式搶到最前面；由使用者剛開的這個程式授權給原本的程式
            AllowSetForegroundWindow(ASFW_ANY);
            showWindowEvent.Set();
        }
    }

    private const int ASFW_ANY = -1;

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
