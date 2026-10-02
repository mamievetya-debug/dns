using System.Diagnostics;
using Spectre.Console;
using QuickBypass;

// Safe title & High Process Priority for 0ms scheduling
try 
{ 
    Console.Title = "QuickBypass - Discord VIP Edition"; 
    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;
} 
catch { }

// --- AKILLI AÇ/KAPA (SMART TOGGLE) KONTROLÜ ---
using var mutex = new Mutex(true, "QuickBypass_SingleInstance_Mutex_Global", out bool isNewInstance);
if (!isNewInstance)
{
    AnsiConsole.MarkupLine("\n[bold yellow]⚠ QuickBypass zaten çalışıyor! Kapatılıyor...[/]");
    int currentId = Environment.ProcessId;
    foreach (var proc in Process.GetProcessesByName("QuickBypass"))
    {
        if (proc.Id != currentId)
        {
            try { proc.Kill(); proc.WaitForExit(500); } catch { }
        }
    }

    WindowsProxyManager.RestoreProxy();
    AnsiConsole.MarkupLine("[bold green]✓ QuickBypass kapatıldı ve internet sıfırlandı.[/]\n");
    Thread.Sleep(1000);
    return;
}

// --- PARSE ARGS ---
int port = 8080;
bool setSysProxy = true;
bool startMinimized = false;
foreach (var arg in args)
{
    if (int.TryParse(arg, out var p)) port = p;
    if (arg.Equals("--no-sys-proxy", StringComparison.OrdinalIgnoreCase)) setSysProxy = false;
    if (arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) ||
        arg.Equals("--hide", StringComparison.OrdinalIgnoreCase)) startMinimized = true;
}

var server = new ProxyServer(port);
TrayIconManager? trayManager = null;

bool isCleanedUp = false;
void Cleanup()
{
    if (isCleanedUp) return;
    isCleanedUp = true;
    try { trayManager?.Dispose(); } catch { }
    if (setSysProxy) WindowsProxyManager.RestoreProxy();
    server.Stop();
}

WindowsProxyManager.RegisterCloseHandler(Cleanup);
AppDomain.CurrentDomain.ProcessExit += (_, _) => Cleanup();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; Cleanup(); Environment.Exit(0); };

try
{
    trayManager = new TrayIconManager(() => { Cleanup(); Environment.Exit(0); });
}
catch { }

try
{
    // 1. Start proxy server FIRST (with built-in socket retry)
    server.Start();

    // 2. Enable system proxy
    if (setSysProxy)
    {
        WindowsProxyManager.EnableProxy($"127.0.0.1:{port}");
    }

    // 3. Start tray icon in background
    _ = Task.Run(() =>
    {
        try { trayManager?.Start(startMinimized); } catch { }
    });

    // 4. Boost Discord priority and fix stuck updater in background
    _ = Task.Run(() =>
    {
        try
        {
            DiscordHelper.FixStuckUpdater();
            DiscordHelper.BoostDiscordPriority();
        }
        catch { }
    });

    // Banner (Instant display)
    AnsiConsole.Write(new FigletText("QuickBypass").Centered().Color(Spectre.Console.Color.DeepSkyBlue1));
    AnsiConsole.MarkupLine($"[green]Engine Active[/] | Port [cyan]{port}[/] | DoH [green]0ms Gateway Cache[/] | Tray [green]Aktif[/]");
    AnsiConsole.MarkupLine("[bold magenta]★ VIP ÖNCELİK:[/] [bold white]Discord (Yüksek CPU/Ağ Önceliği + 0ms Gateway)[/]");
    AnsiConsole.MarkupLine("[grey]'H' Gizle | 'Q' / 'Enter' Kapat | 'X' Pencere Kapat[/]\n");

    var cts = new CancellationTokenSource();

    // Key listener
    if (!Console.IsInputRedirected)
    {
        _ = Task.Run(() =>
        {
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable)
                    {
                        var k = Console.ReadKey(true);
                        if (k.Key == ConsoleKey.Q || k.Key == ConsoleKey.Enter)
                        {
                            cts.Cancel();
                            break;
                        }
                        else if (k.Key == ConsoleKey.H)
                        {
                            trayManager?.HideConsole();
                        }
                    }
                    Thread.Sleep(100);
                }
            }
            catch { }
        });
    }

    // Dashboard loop
    long lastBytes = 0, lastConn = 0, lastBypass = 0;

    while (!cts.Token.IsCancellationRequested)
    {
        while (server.RecentLogs.TryDequeue(out var log))
        {
            string safeDetails = Markup.Escape(log.Details);
            string safeTarget = Markup.Escape(log.Target);
            bool isDiscord = safeTarget.Contains("discord", StringComparison.OrdinalIgnoreCase);
            string prefix = isDiscord ? "[bold magenta]★ [[DISCORD VIP]][/] " : "";
            string status = log.Bypassed
                ? (isDiscord ? $"[bold green]BYPASS (0ms)[/] [grey]({safeDetails})[/]" : $"[green]BYPASS[/] [grey]({safeDetails})[/]")
                : $"[grey]DIRECT[/]";
            AnsiConsole.MarkupLine($"{prefix}[grey]{log.Time:HH:mm:ss}[/] [cyan]{safeTarget}[/] -> {status}");
        }

        if (server.TotalConnections != lastConn || server.TotalBytesTransferred != lastBytes || server.TotalBypassed != lastBypass)
        {
            lastConn = server.TotalConnections;
            lastBytes = server.TotalBytesTransferred;
            lastBypass = server.TotalBypassed;

            DiscordHelper.BoostDiscordPriority();

            AnsiConsole.MarkupLine($"[grey][[STATS]][/] Aktif: [yellow]{server.ActiveConnections}[/] | Toplam: [white]{server.TotalConnections}[/] | Bypass: [green]{server.TotalBypassed}[/] | Veri: [cyan]{FormatBytes(server.TotalBytesTransferred)}[/]");
        }

        await Task.Delay(500, cts.Token).ContinueWith(_ => { });
    }
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"\n[bold red]HATA:[/] {Markup.Escape(ex.Message)}");
    AnsiConsole.MarkupLine("[grey]" + Markup.Escape(ex.ToString()) + "[/]");
    AnsiConsole.MarkupLine("\n[yellow]Pencereyi kapatmak için bir tuşa basın...[/]");
    try { Console.ReadKey(); } catch { }
}
finally
{
    Cleanup();
    AnsiConsole.MarkupLine("\n[yellow]Proxy kapatıldı. Görüşürüz![/]");
}

static string FormatBytes(long bytes)
{
    if (bytes < 1024) return $"{bytes} B";
    if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
    if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
    return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
}
