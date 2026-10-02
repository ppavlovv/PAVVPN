# Bağımlılıklar

## Çalışma zamanı

PAVVPN üçüncü taraf EXE, DLL, kernel sürücüsü veya Windows servisi dağıtmaz.

- Windows 10 veya Windows 11
- Windows ile gelen .NET Framework 4.x çalışma zamanı
- Windows WinINet kullanıcı PAC ayarı
- Discord HTTPS altyapısı
- DNS-over-HTTPS için Cloudflare `https://1.1.1.1/dns-query`, başarısız olursa Google `https://dns.google/resolve`

Cloudflare ve Google uygulama kodu değildir; yalnız DNS çözümleme hizmetidir. Kaynakta paket yöneticisi, NuGet paketi ve otomatik internetten kod indirme adımı bulunmaz.

## Derleme

`build.ps1`, Windows .NET Framework ile gelen `csc.exe` derleyicisini kullanır. Arayüz yalnız `System.Windows.Forms` ve `System.Drawing`; motor yalnız .NET Framework standart kütüphaneleri ve `System.Web.Extensions` ile derlenir.

## Lisans

Depodaki PAVVPN kaynak kodu ve proje varlıkları `LICENSE` dosyasındaki MIT lisansı altında yayımlanır. Windows, .NET Framework, Discord, Cloudflare ve Google hizmetleri bu lisansın parçası değildir.
