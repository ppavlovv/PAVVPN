# PAVVPN 5

PAVVPN, yalnız Discord masaüstü ve web trafiği için çalışan, yönetici yetkisi ve kernel sürücüsü kullanmayan yerel bir HTTP CONNECT/SOCKS5 proxy'sidir. Tek masaüstü uygulamasındaki anahtar bağlantıyı açıp kapatır; pencere kapatıldığında uygulama sistem tepsisinde kalır. TLS ClientHello kaydını standart TLS kayıtları halinde yeniden kodlayan motor doğrudan `PavVpnNative.cs` içindedir. Dağıtımda ByeDPI, GoodbyeDPI, WinDivert veya başka bir proxy EXE'si bulunmaz.

> **Yayın durumu:** Windows 10/11 için release candidate. Kod imzası yoktur. Genel dağıtımdan önce farklı bilgisayar ve internet sağlayıcılarında saha testi gerekir.

## Kullanım

1. ZIP'i normal bir klasöre çıkarın ve `pavvpn.bat` dosyasını çalıştırın.
2. Masaüstündeki tek `PAVVPN` kısayolunu açın.
3. Büyük anahtarla Discord bağlantısını açıp kapatın.
4. İsterseniz `Windows ile başlat` seçeneğini kullanın.
5. Tam kaldırma için uygulamadaki bağlantıyı açın ve konsolda `KALDIR` yazın.

Arayüz `PAVVPN.exe`, bağlantı motoru `PAVVPN.Native.v5.exe` dosyasıdır. Motor açıkken konsol penceresi göstermez. İkisi de bu kaynaklardan Windows'un .NET Framework derleyicisiyle oluşturulur.

## Güvenlik sınırı

- Dinleyici yalnız `127.0.0.1:1088` adresine bağlanır.
- Yalnız Discord alan adı kökleri ve tanımlı HTTPS/ses kontrol portları kabul edilir.
- En fazla 256 eşzamanlı yerel bağlantı işlenir.
- Başlat/durdur kontrol istekleri her çalıştırmada üretilen 256 bit token ister.
- HTTPS sertifika doğrulaması kapatılmaz ve sisteme sertifika kurulmaz.
- PAC diğer bütün alan adları için `DIRECT` döndürür.
- Sistem DNS'i, hosts dosyası, kernel sürücüsü, servis ve zamanlanmış görev kullanılmaz.
- Windows başlangıcı yalnız geçerli kullanıcının `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` altındaki `PAVVPN` girdisidir ve uygulamadan kapatılabilir. Açılışta ağ henüz hazır değilse uygulama hata penceresi göstermeden sınırlı olarak yeniden dener.
- Kaldırıcı yalnız `%LOCALAPPDATA%\PAVVPN`, PAVVPN masaüstü kısayolu, başlangıç girdisi ve PAVVPN'in kesin PAC adresini temizler.

Bu sınırlar sıfır hata veya sıfır açık garantisi değildir. Genel sürümden önce farklı Windows 10/11 makineleri ve farklı internet sağlayıcılarında saha testi yapılmalıdır.

## Gizlilik ve ağ bağlantıları

PAVVPN telemetri, reklam, analiz, hesap veya uzaktan yönetim içermez. Günlükler yalnız `%LOCALAPPDATA%\PAVVPN\pav_debug.log` dosyasına yazılır ve 2 MiB'de döndürülür. Discord hedeflerinin IPv4 çözümlemesi için sırasıyla Cloudflare `1.1.1.1` ve Google Public DNS'in HTTPS DNS uç noktaları kullanılır. Bu sağlayıcılar sorgulanan Discord alan adını görebilir. Discord içeriğinin TLS sertifika doğrulaması kapatılmaz; PAVVPN içerik şifresini çözmez ve sisteme sertifika eklemez.

## Bileşenler

| Dosya | Görev |
| --- | --- |
| `PavVpnControl.cs` | Masaüstü arayüzü, tepsi simgesi ve başlangıç ayarı |
| `PavVpnNative.cs` | Loopback proxy, Discord allowlist, TLS kayıt bölme ve PAC sunucusu |
| `install.ps1` | Kullanıcı kapsamlı, hash doğrulamalı kurulum |
| `uninstall.ps1` | PAC geri yükleme ve tam kaldırma |
| `tests/` | Statik, parser ve canlı bağlantı testleri |

Ayrıntılar için [ARCHITECTURE.md](ARCHITECTURE.md) ve [DEPENDENCIES.md](DEPENDENCIES.md) dosyalarına bakın.

## Derleme

Windows PowerShell'de:

```powershell
.\build.ps1
```

Derleme yalnız Windows ile gelen .NET Framework C# derleyicisini kullanır. `--self-test`, allowlist, PAC, TLS kayıt bütünlüğü, 20.000 rastgele parser girdisi ve arayüz kaynaklarını test eder.

Yerel stres testi:

```powershell
.\tests\stress.ps1 -Requests 200 -RestartCycles 8
```

Test; gerçek Discord TLS isteklerini, SOCKS5 el sıkışmasını, Discord dışı hedef reddini, loopback sınırını ve art arda durdur/başlat çevrimlerini denetler.

## Yayın paketi

`prepare-release.ps1` kaynakları derler, statik denetimi çalıştırır ve SHA-256 manifestli Windows paketi üretir. Kod imzalama sertifikası bulunmadığı sürece yayın `unsigned` olarak belirtilmelidir; kullanıcılar ZIP ve EXE özetlerini `SHA256SUMS.txt` ile doğrulayabilir.

## Lisans

Bu dizindeki özgün PAVVPN Native kaynakları `LICENSE` dosyasındaki MIT lisansı altındadır. MIT lisansı ücretsizdir; kullanım, değiştirme, dağıtma ve satmaya izin verir. Windows ve .NET proje tarafından dağıtılan bileşenler değildir.
