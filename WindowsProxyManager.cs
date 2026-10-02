using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace QuickBypass;

public static class WindowsProxyManager
{
    private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
    private const int INTERNET_OPTION_REFRESH = 37;

    private const int HWND_BROADCAST = 0xFFFF;
    private const int WM_SETTINGCHANGE = 0x001A;

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

    // Non-blocking broadcast: posts the message and returns immediately
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool SendNotifyMessage(
        IntPtr hWnd,
        uint Msg,
        UIntPtr wParam,
        string lParam);

    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache")]
    private static extern int DnsFlushResolverCache();

    private delegate bool ConsoleCtrlDelegate(int sig);
    private static ConsoleCtrlDelegate? _ctrlHandler;

    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>
    /// Registers Win32 console control handler so when the user clicks 'X' on the console window,
    /// proxy is guaranteed to be disabled immediately before the process is killed by Windows.
    /// </summary>
    public static void RegisterCloseHandler(Action onExit)
    {
        _ctrlHandler = sig =>
        {
            try
            {
                onExit();
            }
            catch { }
            return false;
        };
        SetConsoleCtrlHandler(_ctrlHandler, true);
    }

    public static void EnableProxy(string serverAddress)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
            if (key == null) return;

            key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
            key.SetValue("ProxyServer", serverAddress, RegistryValueKind.String);
            key.SetValue("ProxyOverride", "<local>;localhost;127.0.0.1;*.local", RegistryValueKind.String);

            RefreshWinINet();

            try { DnsFlushResolverCache(); } catch { }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsProxyManager] Error enabling proxy: {ex.Message}");
        }
    }

    /// <summary>
    /// Unconditionally disables Windows proxy and removes ProxyServer to guarantee internet is restored.
    /// </summary>
    public static void RestoreProxy()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(SubKey, writable: true);
            if (key != null)
            {
                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.DeleteValue("ProxyServer", false);
                RefreshWinINet();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WindowsProxyManager] Error disabling proxy: {ex.Message}");
        }
    }

    private static void RefreshWinINet()
    {
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);

            // Non-blocking broadcast: fire-and-forget, 0ms wait
            SendNotifyMessage(
                (IntPtr)HWND_BROADCAST,
                WM_SETTINGCHANGE,
                UIntPtr.Zero,
                "Internet Settings");
        }
        catch { }
    }
}

