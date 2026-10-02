using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickBypass;

public class TrayIconManager : IDisposable
{
    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private readonly Action _onExitRequested;
    private NotifyIcon? _notifyIcon;
    private Icon? _trayIcon;
    private Thread? _trayThread;
    private bool _isDisposed;
    private readonly IntPtr _consoleHwnd;

    public TrayIconManager(Action onExitRequested)
    {
        _onExitRequested = onExitRequested;
        _consoleHwnd = GetConsoleWindow();
    }

    public void Start(bool startMinimized = false)
    {
        var readyEvent = new ManualResetEventSlim(false);

        _trayThread = new Thread(() =>
        {
            try
            {
                try { Application.SetHighDpiMode(HighDpiMode.SystemAware); } catch { }

                _trayIcon = CreateCustomTrayIcon();
                _notifyIcon = new NotifyIcon
                {
                    Icon = _trayIcon,
                    Text = "QuickBypass - Discord VIP Bypass",
                    Visible = true
                };

                // Context Menu
                var contextMenu = new ContextMenuStrip();
                
                var titleItem = new ToolStripMenuItem("QuickBypass (Discord VIP)")
                {
                    Enabled = false,
                    Font = new Font(Control.DefaultFont, FontStyle.Bold)
                };
                contextMenu.Items.Add(titleItem);

                var statusItem = new ToolStripMenuItem("Proxy: 127.0.0.1:8080 (0ms DoH)")
                {
                    Enabled = false
                };
                contextMenu.Items.Add(statusItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                var toggleItem = new ToolStripMenuItem("Konsolu Göster / Gizle", null, (_, _) =>
                {
                    ToggleConsole();
                });
                contextMenu.Items.Add(toggleItem);

                contextMenu.Items.Add(new ToolStripSeparator());

                var exitItem = new ToolStripMenuItem("Çıkış (Kapat ve Sıfırla)", null, (_, _) =>
                {
                    _onExitRequested();
                })
                {
                    ForeColor = System.Drawing.Color.DarkRed,
                    Font = new Font(Control.DefaultFont, FontStyle.Bold)
                };
                contextMenu.Items.Add(exitItem);

                _notifyIcon.ContextMenuStrip = contextMenu;

                // Tıklama ile konsol aç / kapa
                _notifyIcon.MouseClick += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        ToggleConsole();
                    }
                };

                _notifyIcon.DoubleClick += (_, _) =>
                {
                    ToggleConsole();
                };

                if (startMinimized)
                {
                    HideConsole();
                }

                readyEvent.Set();
                Application.Run();
            }
            catch
            {
                readyEvent.Set();
            }
        });

        _trayThread.SetApartmentState(ApartmentState.STA);
        _trayThread.IsBackground = true;
        _trayThread.Start();

        readyEvent.Wait(500);
    }

    public void ToggleConsole()
    {
        if (_consoleHwnd == IntPtr.Zero) return;

        if (IsWindowVisible(_consoleHwnd))
        {
            HideConsole();
        }
        else
        {
            ShowConsole();
        }
    }

    public void HideConsole()
    {
        if (_consoleHwnd != IntPtr.Zero)
        {
            ShowWindow(_consoleHwnd, SW_HIDE);
            try
            {
                _notifyIcon?.ShowBalloonTip(1500, "QuickBypass", "QuickBypass sistem tepsisinde çalışıyor. Açmak için simgeye tıklayın.", ToolTipIcon.Info);
            }
            catch { }
        }
    }

    public void ShowConsole()
    {
        if (_consoleHwnd != IntPtr.Zero)
        {
            ShowWindow(_consoleHwnd, SW_RESTORE);
            ShowWindow(_consoleHwnd, SW_SHOW);
            SetForegroundWindow(_consoleHwnd);
        }
    }

    private static Icon CreateCustomTrayIcon()
    {
        try
        {
            // 32x32 keskin, şık, neon mavi bypass kalkan ikonu
            using var bmp = new Bitmap(32, 32);
            using var g = Graphics.FromImage(bmp);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // Koyu yuvarlak zemin
            using var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(20, 24, 34));
            g.FillEllipse(bgBrush, 1, 1, 30, 30);

            // Neon Mavi Çerçeve
            using var borderPen = new Pen(System.Drawing.Color.FromArgb(0, 195, 255), 2f);
            g.DrawEllipse(borderPen, 2, 2, 28, 28);

            // Merkezde Şimşek / Hız / Bypass Sembolü
            using var boltBrush = new SolidBrush(System.Drawing.Color.FromArgb(0, 230, 255));
            PointF[] boltPoints =
            [
                new PointF(17, 6),
                new PointF(10, 16),
                new PointF(16, 16),
                new PointF(14, 25),
                new PointF(22, 14),
                new PointF(16, 14)
            ];
            g.FillPolygon(boltBrush, boltPoints);

            // Sağ altta yeşil "Aktif" noktası
            using var greenBrush = new SolidBrush(System.Drawing.Color.FromArgb(50, 220, 90));
            g.FillEllipse(greenBrush, 21, 21, 8, 8);
            using var greenBorder = new Pen(System.Drawing.Color.FromArgb(20, 24, 34), 1.5f);
            g.DrawEllipse(greenBorder, 21, 21, 8, 8);

            IntPtr hIcon = bmp.GetHicon();
            var icon = (Icon)Icon.FromHandle(hIcon).Clone();
            DestroyIcon(hIcon);
            return icon;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
        catch { }

        try
        {
            _trayIcon?.Dispose();
            _trayIcon = null;
        }
        catch { }
    }
}
