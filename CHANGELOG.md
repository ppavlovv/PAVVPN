# Değişiklik günlüğü

## 5.0.2 — 2026-10-03

- Discord ekran görüntüsü ve dosya yüklemelerinde kullanılan `discord-attachments-uploads-prd.storage.googleapis.com` hedefi masaüstü/web PAC ve proxy izin listesine tam eşleşmeyle eklendi.
- Genel `googleapis.com` trafiği kapalı tutuldu; öz test ve canlı stres testi bu güvenlik sınırını doğrulayacak şekilde genişletildi.

## 5.0.1 — 2026-10-03

- Windows/VDS açılışında ağın geç hazırlanması için sessiz, sınırlı yeniden deneme eklendi.
- Başlangıç yöntemi VBS dosyasından kullanıcı kapsamlı `HKCU\...\Run` girdisine taşındı.
- Motor başlatma kontrolü, süreç erken kapanırsa 30 saniye boşuna beklemeyecek şekilde düzeltildi.
- Erişilemeyen yabancı `Update.exe` süreçleri Discord süreci sayılmıyor; Discord otomatik açılamasa bile yerel motor açık kalıyor.

## 5.0.0-rc — 2026-10-02

- Üçüncü taraf proxy motorları ve kernel sürücüsü bağımlılığı kaldırıldı.
- PAVVPN'e ait kullanıcı modu HTTP CONNECT/SOCKS5 motoru eklendi.
- Yalnız loopback dinleme, Discord hedef allowlist'i, bağlantı sınırı ve tokenli kontrol eklendi.
- Discord masaüstü, web, güncelleme ve ses alan adları desteklendi.
- Tek kısayollu, tepsi destekli ve animasyonlu masaüstü arayüzü eklendi.
- Windows kullanıcı başlangıcı ve tam geri yüklemeli kaldırıcı eklendi.
- Hazır durumu PAC uygulamasından sonra doğrulanacak şekilde ayrıldı.
- Kaldırıcının kendi klasörünü silerken verdiği son CMD hatası giderildi.
- Yedek bulunmadığında kalan PAVVPN PAC değerini güvenle temizleyen geri dönüş eklendi.
- SHA-256 manifesti, CI, statik kontroller, parser fuzz testi ve canlı stres testi eklendi.
