using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using SerialManager.Data;
using SerialManager.Services;

namespace SerialManager;

/// <summary>
/// 主視窗：拿掉 Windows 原本的標題列和邊框，改成圓角視窗；
/// 標題列由畫面（TitleBar.razor）自己畫，這裡負責拖曳、縮放、最大化。
/// </summary>
public class MainForm : Form
{
    private const int CornerRadius = 12;
    private const int ResizeBorder = 5;   // 視窗邊緣可以拖曳調整大小的寬度

    private const string CloseToTrayKey = "Window.CloseToTray";
    private const string TrayHintShownKey = "Window.TrayHintShown";

    private readonly AppearanceService _appearance;
    private readonly SettingsRepository _settings;
    private readonly SerialRepository _serials;
    private readonly FeatureSettings _features;
    private readonly WindowService _windowService;
    private readonly BlazorWebView _blazorWebView;
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _notifyTimer = new();
    private Color _borderColor;
    private bool _exiting;   // true 代表真的要結束，不是收進系統匣
    private bool _lastBalloonIsExpiring;   // 最後跳出的通知是不是到期通知（點了要切到快到期）

    /// <summary>
    /// 按 X 時收進系統匣（true）或直接結束（false）。
    /// </summary>
    public bool CloseToTray { get; private set; }

    /// <summary>
    /// 最大化／還原／最小化時觸發。
    /// </summary>
    public event Action? WindowStateChanged;

    public MainForm()
    {
        Text = "序號管理";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("SerialManager.app.ico")!)
        {
            Icon = new Icon(iconStream);
        }
        FormBorderStyle = FormBorderStyle.None;
        Width = 1200;
        Height = 800;
        MinimumSize = new Size(640, 400);
        StartPosition = FormStartPosition.CenterScreen;
        Padding = new Padding(ResizeBorder);
        DoubleBuffered = true;

        // 資料庫檔案放在程式旁邊；第一次執行時自動建立資料表
        var dbPath = Path.Combine(AppContext.BaseDirectory, "serials.db");
        var database = new Database(dbPath);
        database.EnsureCreated();

        var settingsRepository = new SettingsRepository(database);
        _settings = settingsRepository;
        _serials = new SerialRepository(database);
        _appearance = new AppearanceService(settingsRepository);
        _features = new FeatureSettings(settingsRepository);
        _windowService = new WindowService(this);
        LoadWindowSettings();

        // 登記畫面元件會用到的物件，元件裡用 @inject 取得
        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton(database);
        services.AddSingleton(_serials);
        services.AddSingleton(settingsRepository);
        services.AddSingleton<BackupRepository>();
        services.AddSingleton(_appearance);
        services.AddSingleton(_features);
        services.AddSingleton(new ExportSettings(settingsRepository));
        services.AddSingleton(_windowService);
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif

        _blazorWebView = new EmbeddedBlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot/index.html",
            Services = services.BuildServiceProvider(),
        };
        _blazorWebView.RootComponents.Add<Components.App>("#app");
        Controls.Add(_blazorWebView);

        ApplyThemeColors();
        _appearance.Changed += () =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(ApplyThemeColors);
            }
        };

        // 右下角系統匣圖示：左鍵點一下叫回視窗，右鍵選單可以結束程式
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("開啟", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("結束", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = new Icon(Icon, SystemInformation.SmallIconSize),   // 系統匣用小尺寸，才不會縮得模糊
            Text = "序號管理",
            ContextMenuStrip = trayMenu,
            Visible = true,
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                RestoreFromTray();
            }
        };

        // 點到期通知：叫回視窗，並切到序號列表的「快到期」
        _trayIcon.BalloonTipClicked += (_, _) =>
        {
            if (_lastBalloonIsExpiring)
            {
                RestoreFromTray();
                _windowService.RaiseExpiringNotificationClicked();
            }
        };

        // 到期通知：開啟 5 秒後檢查一次，之後依「通知檢查頻率」定時檢查
        _notifyTimer.Tick += (_, _) => CheckExpiring();
        Shown += async (_, _) =>
        {
            await Task.Delay(5000);
            CheckExpiring();
            RestartNotifyTimer();
        };
        _features.Changed += () =>
        {
            if (IsHandleCreated)
            {
                BeginInvoke(RestartNotifyTimer);
            }
        };
    }

    private void RestartNotifyTimer()
    {
        _notifyTimer.Stop();
        _notifyTimer.Interval = _features.NotifyIntervalHours * 60 * 60 * 1000;
        _notifyTimer.Start();
    }

    /// <summary>
    /// 有快到期的序號就跳 Windows 通知（管理頁關掉「到期通知」時不跳）。
    /// </summary>
    private void CheckExpiring()
    {
        if (!_features.NotifyEnabled)
        {
            return;
        }

        var message = ExpiryReminder.BuildMessage(_serials.GetAll(), DateTime.Today, _features.ExpiringDays);
        if (message is null)
        {
            return;
        }

        _lastBalloonIsExpiring = true;
        _trayIcon.ShowBalloonTip(10000, "序號管理", message, ToolTipIcon.Warning);
    }

    public void SetCloseToTray(bool value)
    {
        CloseToTray = value;
        _settings.Set(CloseToTrayKey, value ? "true" : "false");
    }

    /// <summary>
    /// 重新從資料庫讀取視窗設定（讀取備份後使用）。
    /// </summary>
    public void LoadWindowSettings()
    {
        CloseToTray = _settings.Get(CloseToTrayKey) != "false";   // 沒設定過就是收進系統匣
    }

    /// <summary>
    /// 真的結束程式（不管按 X 的設定）。
    /// </summary>
    public void ExitApplication()
    {
        _exiting = true;
        Close();
    }

    /// <summary>
    /// 把視窗叫回來（從系統匣、最小化，或再開一次程式時）。
    /// </summary>
    public void RestoreFromTray()
    {
        Show();
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // 使用者按 X（或 Alt+F4）而且設定為收進系統匣：只隱藏視窗，程式繼續在背景執行
        if (CloseToTray && !_exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            ShowTrayHintOnce();
            return;
        }

        _trayIcon.Visible = false;
        base.OnFormClosing(e);
    }

    private void ShowTrayHintOnce()
    {
        if (_settings.Get(TrayHintShownKey) == "true")
        {
            return;
        }

        _lastBalloonIsExpiring = false;
        _trayIcon.ShowBalloonTip(5000, "序號管理", "程式仍在背景執行，可從右下角圖示開啟或結束。", ToolTipIcon.Info);
        _settings.Set(TrayHintShownKey, "true");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Dispose();
            _notifyTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// 開始拖曳視窗：讓 Windows 以為使用者按住了原本的標題列。
    /// </summary>
    public void BeginDrag()
    {
        // 畫面的事件是非同步送過來的，滑鼠可能已經放開；放開了就不拖，避免視窗黏在游標上
        if ((MouseButtons & MouseButtons.Left) == 0)
        {
            return;
        }

        ReleaseCapture();
        SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
    }

    public void ToggleMaximize()
    {
        if (WindowState == FormWindowState.Maximized)
        {
            WindowState = FormWindowState.Normal;
            return;
        }

        // 沒有邊框的視窗最大化時會蓋住工作列，所以限制在工作區域內（座標以螢幕左上角為準）
        var screen = Screen.FromHandle(Handle);
        var area = screen.WorkingArea;
        MaximizedBounds = new Rectangle(area.X - screen.Bounds.X, area.Y - screen.Bounds.Y, area.Width, area.Height);
        WindowState = FormWindowState.Maximized;
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.Style |= WS_MINIMIZEBOX;       // 讓點工作列圖示可以最小化
            cp.ClassStyle |= CS_DROPSHADOW;   // 視窗陰影
            return cp;
        }
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateShape();
        WindowStateChanged?.Invoke();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (WindowState == FormWindowState.Maximized)
        {
            return;
        }

        // 沿著圓角畫一圈細框線
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(_borderColor, 1);
        using var path = CreateRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        e.Graphics.DrawPath(pen, path);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // 滑鼠在視窗邊緣時，告訴 Windows 這裡是邊框，就能拖曳調整大小
        if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
        {
            var lParam = m.LParam.ToInt64();
            var point = PointToClient(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));

            var left = point.X < ResizeBorder;
            var right = point.X >= ClientSize.Width - ResizeBorder;
            var top = point.Y < ResizeBorder;
            var bottom = point.Y >= ClientSize.Height - ResizeBorder;

            var hit = (top, bottom, left, right) switch
            {
                (true, _, true, _) => HTTOPLEFT,
                (true, _, _, true) => HTTOPRIGHT,
                (_, true, true, _) => HTBOTTOMLEFT,
                (_, true, _, true) => HTBOTTOMRIGHT,
                (true, _, _, _) => HTTOP,
                (_, true, _, _) => HTBOTTOM,
                (_, _, true, _) => HTLEFT,
                (_, _, _, true) => HTRIGHT,
                _ => 0,
            };

            if (hit != 0)
            {
                m.Result = (IntPtr)hit;
            }
        }
    }

    private void UpdateShape()
    {
        var oldRegion = Region;

        if (WindowState == FormWindowState.Maximized)
        {
            Padding = new Padding(0);
            Region = null;
        }
        else
        {
            Padding = new Padding(ResizeBorder);
            var handle = CreateRoundRectRgn(0, 0, Width + 1, Height + 1, CornerRadius * 2, CornerRadius * 2);
            Region = Region.FromHrgn(handle);
            DeleteObject(handle);
        }

        oldRegion?.Dispose();
        Invalidate();
    }

    private void ApplyThemeColors()
    {
        var palette = _appearance.Palette;
        BackColor = ColorTranslator.FromHtml(palette.Bg);
        _borderColor = ColorTranslator.FromHtml(palette.WindowBorder);
        _blazorWebView.WebView.DefaultBackgroundColor = BackColor;
        Invalidate();
    }

    private static GraphicsPath CreateRoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    // ── Windows API ──
    private const int WM_NCHITTEST = 0x84;
    private const int WM_NCLBUTTONDOWN = 0xA1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15, HTBOTTOMLEFT = 16, HTBOTTOMRIGHT = 17;
    private const int WS_MINIMIZEBOX = 0x00020000;
    private const int CS_DROPSHADOW = 0x00020000;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int widthEllipse, int heightEllipse);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
