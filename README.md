# 🚀 QuickBypass - Profesyonel DPI Evasion & Discord VIP Hızlandırıcı

GoodbyeDPI mantığıyla çalışan, internet hızını ve ping değerini (gecikme) **kesinlikle düşürmeyen**, Windows için geliştirilmiş ultra hafif, tek dosya (portable) DPI atlatma motoru.

Özellikle **Discord**, **Roblox**, **Steam Community** gibi Türkiye'de engellenen platformlara 0ms gecikmeyle, yayın ve oyun kalitesini %100 koruyarak erişmek için optimize edilmiştir.

---

## ⚡ Neden İnternet Hızını ve Ping'i Etkilemez?

* **VPN Değildir:** Trafiğiniz uzak bir ülkedeki sunucuya yönlendirilmez. Tamamen kendi bilgisayarınızın RAM'inde (`127.0.0.1`) çalışır.
* **Cerrahi TCP Müdahalesi:** Yalnızca bağlantının ilk 1 milisaniyesindeki **TLS ClientHello (SNI)** el sıkışma paketini tam ortasından ikiye böler (TCP Fragmentation). ISS'nin DPI filtreleri alan adını okuyamaz.
* **Doğrudan ve Kayıpsız Akış:** Bağlantı kurulduktan sonraki gigabaytlarca veri (ses, video, dosya indirme, OBS yayını) doğrudan orijinal sunucularla sizin aranızda **donanım hızında** akar.
* **Oyun Ping'i (UDP) Korunur:** Valorant, CS2, LoL, R6 gibi online oyunların maç trafiği UDP'dir; proxy sistemine hiç uğramaz. Ping 1 ms dahi oynamaz.

---

## 🌟 Öne Çıkan Özellikler

1. **Discord VIP Motoru:**
   * `gateway.discord.gg` WebSocket bağlantısı için optimize edilmiş Anycast IP önbelleği. Açılışta dönen yükleme ekranı beklemesi yaşanmaz.
   * Windows işletim sistemi düzeyinde `Discord.exe` süreçlerine yüksek CPU ve G/Ç önceliği atar.
   * Eğer Discord açılışında güncelleme sayacı ("Update failed — retrying...") takılırsa otomatik algılayıp anında çözer.
2. **Sistem Tepsisi (System Tray) Entegrasyonu:**
   * Klavyeden **`H`** tuşuna basarak siyah konsol penceresini gizleyebilir, programı sağ alttaki sistem tepsisinde sessizce çalıştırabilirsiniz.
   * Tepsideki simgeye tıklayarak konsolu geri açabilir veya sağ tıklayıp tek tıkla kapatabilirsiniz.
3. **Akıllı Aç/Kapa Şalteri (Smart Toggle):**
   * Program açıkken `QuickBypass.exe` dosyasına tekrar çift tıklarsanız, çalışan kopya otomatik kapanır ve Windows internet ayarları anında sıfırlanır.
4. **Gömülü DoH (DNS over HTTPS):**
   * Cloudflare (`1.1.1.1`) ve Google (`8.8.8.8`) üzerinden şifreli DNS sorguları yapar. ISS'nin DNS zehirlemelerini tamamen etkisiz kılar.
5. **Anti-Cheat Uyumlu:**
   * Kernel sürücüsü (`.sys`) veya WinDivert gerektirmez. Vanguard, EasyAntiCheat, BattlEye gibi hile koruma sistemleriyle çakışmaz, ban riski yoktur.

---

## 🛠️ Nasıl Kullanılır? (Tek Dosya, Kurulumsuz)

1. **[QuickBypass.exe](https://github.com/mamievetya-debug/dns/releases)** dosyasını indirin ve çift tıklayın.
2. Program açılır, sistem proxy'sini otomatik bağlar ve Discord anında çalışır!
3. **Kısayollar:**
   * **`H`** : Konsolu sağ alt sistem tepsisine gizler.
   * **`Q`** veya **`Enter`** : Programı kapatır ve internet ayarlarını sıfırlar.
   * **`X`** (Çarpı butonu) : Pencereyi kapatır, proxy'yi otomatik temizler.
   * Program açıkken tekrar `.exe`ye basmak da güvenle kapatır.

---

## 🎧 Discord Ses Kanalları İçin Tavsiye (Gerekirse)

Discord'da ses bağlantısında nadiren takılma yaşarsanız:
* Discord Ayarları -> **Ses ve Görüntü (Voice & Video)** sekmesine gidin.
* Sayfayı aşağı kaydırıp **"Paket Önceliği için Hizmet Kalitesini Etkinleştir" (Enable Quality of Service High Packet Priority)** seçeneğini kapatın.

---

## 🔒 Güvenlik ve Gizlilik (Security & Privacy)

* **Sıfır Günlük Tutma (No Logs):** Bu yazılım hiçbir kullanıcı verisini, geçmişini veya IP adresini bir sunucuya göndermez. Tamamen yerel (local) çalışır.
* **Yalnızca Yerel Bağlantı (Localhost Only):** Proxy servisi `127.0.0.1` arayüzüne bağlıdır; aynı yerel ağdaki (Wi-Fi vb.) diğer cihazlar bu porta erişemez.
* **Açık Kaynak:** Kodların tamamı şeffaf ve denetlenebilir durumdadır.

---

## 💻 Kaynak Koddan Derleme (Build from Source)

```bash
# Projeyi derleyin
dotnet build -c Release

# Tek dosya olarak yayınlayın
dotnet publish -c Release -r win-x64 --self-contained false -o publish
```

---

## ⚖️ Yasal Uyarı (Disclaimer)

Bu yazılım yalnızca eğitim, ağ araştırması ve sansür direnci testleri amacıyla geliştirilmiştir. Yazılımın kullanımından doğabilecek tüm hukuki ve cezai sorumluluk son kullanıcıya aittir. Geliştirici hiçbir sorumluluk kabul etmez.
