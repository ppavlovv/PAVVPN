# PAVVPN mimarisi

PAVVPN iki kullanıcı modu sürecinden oluşur. `PAVVPN.exe` yalnız arayüzdür. `PAVVPN.Native.v5.exe` ağ motorudur. İkisi de yönetici yetkisi olmadan çalışır.

```text
Discord / Chrome
       |
 Kullanıcı PAC seçimi (yalnız Discord alan adları)
       |
127.0.0.1:1088  PAVVPN Native
       |         - HTTP CONNECT ve SOCKS5
       |         - Discord alan adı allowlist
       |         - TLS ClientHello kayıt bölme
       v
Discord HTTPS uç noktaları
```

PAC dosyası Discord kök alan adları için yerel proxy döndürür, diğer bütün hedefleri doğrudan bağlantıya bırakır. Motor gelen hedefi ayrıca doğrular; PAC tek güvenlik katmanı değildir. Kontrol uç noktaları rastgele üretilmiş 256 bit oturum tokeni ister.

Motor açılışta bağlantı kontrollerini tamamlayıp PAC ayarını uyguladıktan sonra `/pavdns-ready` uç noktasını hazır hale getirir. Arayüz ve kurucu yalnız bu durumdan sonra bağlantıyı açık kabul eder.

Kapatma sırasında önceki PAC değeri yedekten geri yüklenir. Yedek bulunamazsa kaldırıcı yalnız PAVVPN'in tam PAC adresini temizler; başka bir proxy değerini silmez.
