# Güvenlik politikası

PAVVPN Native kullanıcı modunda ve normal kullanıcı yetkisiyle çalışacak şekilde tasarlanır. İnternet üzerinden erişilebilir bir port açmaz; iki yerel arayüz yerine yalnız IPv4 loopback `127.0.0.1` kullanır.

## Tehdit modeli

Korunan alanlar: uzaktaki istemcilerin proxy'ye erişememesi, Discord dışı hedeflerin reddedilmesi, bozuk proxy/TLS girdilerinin süreç sınırları içinde kalması, geçici PAC ve Discord ayarlarının geri yüklenmesi.

Kapsam dışı: aynı Windows hesabında zaten kod çalıştırabilen zararlı yazılım, yönetici/SYSTEM yetkili süreçler, ele geçirilmiş Windows sertifika deposu, ISS'nin bütün TLS/UDP trafiğini kesmesi ve donanım/kernel sürücüsü arızaları.

## Veri ve yetki özeti

- Yönetici izni, Windows servisi, kernel sürücüsü ve güvenlik duvarı kuralı istemez.
- Yalnız geçerli kullanıcı altında çalışır.
- Yalnız `127.0.0.1:1088` üzerinde dinler; LAN veya internet arayüzüne bağlanmaz.
- Telemetri göndermez. Yerel hata günlüğü en fazla iki dosya halinde tutulur.
- Sertifika deposunu, sistem DNS'ini ve `hosts` dosyasını değiştirmez.
- Discord alan adları ile yalnız Discord'un kullandığı tam eşleşmeli dosya yükleme sunucusuna izin verir; genel Google API ve diğer proxy hedeflerini reddeder.

## Açık bildirme

Bir güvenlik açığı bulursanız herkese açık issue açmadan önce GitHub deposundaki **Security → Report a vulnerability** özelliğini kullanın. Rapora etkilenen sürümü, yeniden üretme adımlarını ve mümkünse kavram kanıtını ekleyin. Token, kişisel veri veya başka kullanıcıların trafiğini paylaşmayın.

## Yayın şartları

Bir sürüm `stable` olarak işaretlenmeden önce derleme öz testi, gerçek HTTP CONNECT ve SOCKS5 TLS testi, Discord uygulama/web/ses testi, bozuk girdi fuzzing'i, eşzamanlı bağlantı testi, kaldırma/geri yükleme testi ve temiz Windows 10/11 testi geçmelidir. İmzalanmamış yayınlar açıkça `unsigned` olarak belirtilmelidir.
