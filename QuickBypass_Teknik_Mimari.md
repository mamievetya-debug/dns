# QuickBypass: Derinlemesine Mimari ve Teknik İnceleme Raporu

Bu doküman, **QuickBypass** motorunun ağ seviyesinde ne yaptığını, veriyi nereden ve nasıl aldığını, neden internet hızını düşürmediğini ve yayıncıların/profesyonel oyuncuların neden güvenle kullanabileceğini en alt seviye ağ protokolleri üzerinden açıklar.

---

## 1. Temel Felsefe: Neden VPN Değildir?

Piyasadaki standart çözümler (VPN'ler) tüm internet trafiğini (oyunlar, OBS canlı yayını, Discord, YouTube) şifreleyip yurt dışındaki (Almanya, Hollanda vb.) bir sunucuya basar.
* **Sonuç:** +40-80ms ping artışı, yayın bitrate dalgalanması, ISS hızının %50 düşmesi.

**QuickBypass'ın Çalışma Modeli:**
QuickBypass bir tünel ya da uzak sunucu değildir. Bilgisayarınızın kendi içinde (Loopback - `127.0.0.1`) çalışan cerrahi bir **ağ yönlendiricisi ve paket düzenleyicisidir**.
* Bağlantı kurulduktan sonraki **gigabaytlarca veri (OBS yayını, Discord sesi, oyun paketleri)** hiçbir filtreye uğramadan doğrudan orijinal Discord veya oyun sunucularıyla donanım hızında akar.
* Ping artışı: **0 ms**. Hız kaybı: **%0**.

---

## 2. Adım Adım Veri Akışı (İnciği Cıncığına)

Bir yayıncı Discord'u açtığında veya Chrome'dan bir siteye girmeye çalıştığında sırasıyla şu 5 adım gerçekleşir:

```
[Discord / Tarayıcı]
       │
       ▼ (1. Adım: Yerel İstek)
[QuickBypass Dinleyicisi (127.0.0.1:8080)]
       │
       ▼ (2. Adım: DoH Şifreli DNS Çözümleme)
[Cloudflare / Google DoH (1.1.1.1 / 8.8.8.8)] ──> Gerçek IP Alınır
       │
       ▼ (3. Adım: Orijinal Sunucuyla TCP Bağlantısı)
[Discord Cloudflare Sunucusu (162.159.x.x:443)]
       │
       ▼ (4. Adım: Cerrahi Müdahale - TLS SNI Parçalama)
[ISS Derin Paket İnceleme (DPI) Filtresi Kör Edilir]
       │
       ▼ (5. Adım: Doğrudan Donanım Hızında Çift Yönlü Köprü)
[Canlı Ses / Görüntü / Veri Akışı]
```

---

### Adım 1: Uygulama İsteği ve Dinleyici (`ProxyServer.cs`)
* Program açıldığında Windows'un `127.0.0.1:8080` yerel portunda asenkron bir TCP soket dinleyicisi başlatır.
* **Güvenlik Kilidi:** Dinleyici kesinlikle `IPAddress.Any` (tüm ağlar) değil, **`IPAddress.Loopback`** olarak ayarlanmıştır. Yani aynı yerel ağdaki (evdeki diğer telefonlar, ortak Wi-Fi'dakiler) hiç kimse yayıncının bilgisayarına sızamaz veya bu portu kullanamaz.

### Adım 2: Şifreli DNS Çözümleme (`DnsResolver.cs`)
Türkiye'deki sansürün 1. ayağı **DNS Zehirlemesidir**.
* Normalde bilgisayar `discord.com` nerede diye sorduğunda Türk Telekom / Superonline DNS'i yalan söyler ve `195.175.254.2` (mahkeme kararı uyarı sayfası) IP'sini döner.
* QuickBypass burada devreye girer: Standart UDP 53 portunu **kullanmaz**.
* Doğrudan **Cloudflare (`https://1.1.1.1/dns-query`)** ve **Google (`https://8.8.8.8/resolve`)** sunucularına **DoH (DNS over HTTPS)** üzerinden 443 portundan şifreli olarak sorar.
* ISS, bu sorunun içeriğini göremez çünkü standart HTTPS trafiği gibi görünür.
* Gerçek IP (`162.159.136.232`) alınır ve RAM önbelleğine (ConcurrentDictionary) kaydedilir. Sonraki isteklerde milisaniyelik gecikme dahi yaşanmaz.

### Adım 3: Hedef Sunucuyla TCP El Sıkışması
* Alınan gerçek IP adresine doğrudan TCP soketi açılır (`targetSocket.ConnectAsync`).
* Discord'a *"Bağlantı kuruldu (HTTP 200 Connection Established)"* cevabı iletilir.

### Adım 4: Cerrahi Müdahale - TLS SNI Parçalama (`DpiEngine.cs` & `TlsParser.cs`)
Türkiye'deki sansürün 2. ayağı **DPI (Derin Paket İnceleme)** cihazlarıdır.
* Discord sunucusuna bağlanırken istemci ilk olarak **TLS ClientHello** paketi yollar. Bu paketin içinde bağlanılmak istenen alan adı açık metin olarak yazar: `SNI: discord.com`.
* ISS'nin kablolarındaki DPI kutusu bu paketi gördüğü anda bağlantıyı keser (TCP Reset).
* **Bizim Müdahalemiz:**
  `TlsParser.cs`, gelen ilk paketin TLS ClientHello olduğunu doğrular ve `SNI` alanının tam bayt koordinatını çıkarır.
  `DpiEngine.cs`, bu paketi tam alan adının ortasından ikiye böler:
  * 1. TCP Parçası: `...updates.di`
  * 2. TCP Parçası: `scord.com...`
* İki parça arasında 2 milisaniyelik bir `Task.Delay` konularak ağ kartından **ayrı iki IP paketi** halinde çıkması sağlanır.
* **Sonuç:** DPI cihazı paketleri tek tek incelediği için `discord.com` kelimesini göremez ve filtreyi tetiklemez. Discord'un Cloudflare sunucusu ise standart TCP kuralları gereği bu iki parçayı hafızasında birleştirir ve el sıkışmayı tamamlar!

### Adım 5: Donanım Hızında Çift Yönlü Köprü (`RelayStreamAsync`)
* El sıkışma başarıyla tamamlandıktan sonra paket parçalama motoru devreden çıkar.
* `.NET 10`'un `ArrayPool<byte>` altyapısı kullanılarak istemci ile sunucu arasında **Zero-Copy (sıfır işlemci yükü)** bir köprü kurulur.
* Ses paketleri, yayın verileri, dosya yüklemeleri doğrudan akar.

---

## 3. Yayıncılar İçin Özel Güvenlik ve Gizlilik Analizi

Yayıncıların en büyük endişeleri şunlardır:

| Endişe | QuickBypass'ta Durum | Teknik Kanıtı |
| :--- | :--- | :--- |
| **OBS Yayın Bitrate'i Düşer mi?** | **HAYIR** | Twitch/YouTube RTMP trafiği proxy üzerinden değil, doğrudan ağ kartından akar. |
| **Valorant / CS2 Pingim Artar mı?** | **HAYIR** | Oyunlar UDP portlarını kullanır. QuickBypass yalnızca TCP (HTTP/HTTPS) portlarını kapsar; oyun trafiğine dokunmaz. |
| **Vanguard / EAC Ban Sebebi midir?** | **HAYIR** | Kernel sürücüsü (`.sys`), DLL injection veya bellek modifikasyonu yapmaz. Standart bir Windows konsol uygulamasıdır. |
| **Kişisel Verilerim Nereye Gidiyor?** | **HİÇBİR YERE** | Kaynak kodda herhangi bir uzak analitik, telemetri veya kayıt sunucusu bulunmaz. Tamamen yerel çalışır. |
| **Token / Hesap Çalınma Riski Var mı?** | **YOK** | QuickBypass HTTPS paketlerinin içini açmaz (SSL Decryption yapmaz, Root CA sertifikası yükletmez). Şifreli veriyi sadece ortadan ikiye bölüp iletir. İletilen şifreli içeriği QuickBypass dahi göremez. |

---

## 4. Dosya Mimarisi ve Görev Dağılımı

* **`Program.cs`:** Ana giriş noktası. Windows kapanma sinyallerini (`WM_SETTINGCHANGE`, `SetConsoleCtrlHandler`) yönetir, proxy'yi açar/kapatır ve canlı metrikleri basar.
* **`ProxyServer.cs`:** Asenkron, çift protokollü (HTTP CONNECT + SOCKS5) yerel sunucu motoru.
* **`DpiEngine.cs`:** DPI cihazlarını atlatan akıllı bayt parçalama algoritması.
* **`TlsParser.cs`:** TLS ClientHello paketini sıfır bellek tahsisiyle (`Span<byte>`) parse eden yüksek performanslı protokol çözümleyici.
* **`DnsResolver.cs`:** Cloudflare ve Google DoH üzerinden çalışan, bellek önbellekli DNS modülü.
* **`WindowsProxyManager.cs`:** Windows kayıt defterini (`Registry`) ve `wininet.dll` API'sini yöneten, program kapandığında internetin açık kalmasını engelleyen emniyet sistemi.
