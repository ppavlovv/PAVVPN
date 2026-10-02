# Katkıda bulunma

1. Değişiklikleri normal kullanıcı yetkisiyle geliştirin; projeye yönetici veya kernel gereksinimi eklemeyin.
2. `build.ps1` ve `tests/static.ps1` kontrollerini çalıştırın.
3. Ağ davranışı değiştiyse `tests/stress.ps1 -Requests 200 -RestartCycles 8` çalıştırın.
4. Yeni hedef alan adı eklerken hem PAC listesini hem motor allowlist'ini güncelleyin ve gerekçesini PR açıklamasına yazın.
5. Telemetri, reklam, gizli indirme veya sertifika doğrulamasını kapatan değişiklikler kabul edilmez.

PR açıklamasında davranış değişikliğini, güvenlik etkisini ve çalıştırılan testleri belirtin. Güvenlik açıklarını herkese açık issue yerine `SECURITY.md` içindeki yöntemle bildirin.
