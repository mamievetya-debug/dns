using System.Diagnostics;

namespace QuickBypass;

public static class DiscordHelper
{
    private static readonly string DiscordLocalDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Discord"
    );

    /// <summary>
    /// Eğer kullanıcı Discord'u QuickBypass'tan önce açtıysa, Discord Updater ("Update failed - retrying in 15 sec")
    /// şeklinde 15 saniyelik uyku döngüsüne girer. Bu metod o takılı kalan updater'ı anında sonlandırıp
    /// QuickBypass üzerinden 0 saniye beklemeyle temiz bir şekilde başlatır.
    /// </summary>
    public static bool FixStuckUpdater()
    {
        try
        {
            var updateProcs = Process.GetProcessesByName("Update");
            bool fixedAny = false;

            foreach (var proc in updateProcs)
            {
                try
                {
                    string? path = null;
                    try { path = proc.MainModule?.FileName; } catch { }

                    if (path != null && path.Contains("Discord", StringComparison.OrdinalIgnoreCase))
                    {
                        proc.Kill();
                        proc.WaitForExit(1000);
                        fixedAny = true;
                    }
                }
                catch { }
            }

            if (fixedAny)
            {
                LaunchDiscordDirectly();
                return true;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Discord'u Squirrel Update.exe beklemesine girmeden doğrudan en güncel app-1.0.xxxx klasöründen
    /// 1 saniyede açar veya yeniden başlatır.
    /// </summary>
    public static void LaunchDiscordDirectly(bool forceRestart = false)
    {
        try
        {
            if (forceRestart)
            {
                // Discord ve Update işlemlerini temizle
                foreach (var proc in Process.GetProcessesByName("Discord"))
                {
                    try { proc.Kill(); proc.WaitForExit(1000); } catch { }
                }
                foreach (var proc in Process.GetProcessesByName("Update"))
                {
                    try
                    {
                        string? path = proc.MainModule?.FileName;
                        if (path != null && path.Contains("Discord", StringComparison.OrdinalIgnoreCase))
                        {
                            proc.Kill();
                            proc.WaitForExit(1000);
                        }
                    }
                    catch { }
                }
            }

            if (!Directory.Exists(DiscordLocalDir)) return;

            // En son sürüm klasörünü bul (örn: app-1.0.9260)
            var appDirs = Directory.GetDirectories(DiscordLocalDir, "app-*")
                                   .OrderByDescending(d => d)
                                   .ToList();

            if (appDirs.Count > 0)
            {
                string exePath = Path.Combine(appDirs[0], "Discord.exe");
                if (File.Exists(exePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true
                    });
                    return;
                }
            }

            // Yedek başlatıcı: Update.exe
            string updateExe = Path.Combine(DiscordLocalDir, "Update.exe");
            if (File.Exists(updateExe))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = updateExe,
                    Arguments = "--processStart Discord.exe",
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// Discord işlemlerine Windows işletim sistemi düzeyinde Yüksek (High) CPU ve G/Ç önceliği atar.
    /// </summary>
    public static void BoostDiscordPriority()
    {
        try
        {
            foreach (var proc in Process.GetProcessesByName("Discord"))
            {
                try
                {
                    if (proc.PriorityClass != ProcessPriorityClass.High)
                    {
                        proc.PriorityClass = ProcessPriorityClass.High;
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}
