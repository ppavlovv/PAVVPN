$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$source=Get-Content -LiteralPath (Join-Path $root 'PavVpnNative.cs') -Raw
$guiSource=Get-Content -LiteralPath (Join-Path $root 'PavVpnControl.cs') -Raw
foreach($forbidden in @('ciadpi','WinDivert','goodbyedpi','third_party')){if(($source+$guiSource).IndexOf($forbidden,[StringComparison]::OrdinalIgnoreCase)-ge 0){throw "Harici motor referansi bulundu: $forbidden"}}
if($source -notmatch 'new TcpListener\(IPAddress\.Loopback, Port\)'){throw 'Dinleyici loopback ile sinirli degil.'}
if($source -notmatch 'const int MaxConnections = 256'){throw 'Baglanti siniri bulunamadi.'}
if($source -notmatch 'discord-attachments-uploads-prd\.storage\.googleapis\.com'){throw 'Discord dosya yukleme hedefi bulunamadi.'}
if($source -match "var roots = \[[^\r\n]*googleapis"){throw 'Genis Google API alan adi hedefi kullaniliyor.'}
if($source -notmatch 'GET /pavdns-ready HTTP/' -or $source -notmatch 'static volatile bool Ready'){throw 'Motor hazirlik kapisi bulunamadi.'}
if($source -match 'if \(launch && !LaunchDiscord\(\)\) \{ listener\.Stop\(\); return 1; \}'){throw 'Discord otomatik acma hatasi motoru kapatiyor.'}
if($source -notmatch 'System\.ComponentModel\.Win32Exception'){throw 'Yabanci/yuksek yetkili Update sureci toleransi bulunamadi.'}
if($guiSource -notmatch 'Local\\PAVVPN\.Native\.UI'){throw 'Arayuz tek-ornek kilidi bulunamadi.'}
if($guiSource -notmatch 'PAVVPN\.Logo'){throw 'Gomulu logo kaynagi bulunamadi.'}
if($guiSource -notmatch 'CurrentVersion\\Run' -or $guiSource -notmatch 'StartupValueName = "PAVVPN"'){throw 'Arayuz kullanici Run baslangic yonetimi bulunamadi.'}
if($guiSource -notmatch 'automatic \? 8 : 1' -or $guiSource -notmatch '!automatic'){throw 'Sessiz ve yeniden deneyen otomatik baslangic akisi bulunamadi.'}
$uninstall=Get-Content -LiteralPath (Join-Path $root 'uninstall.ps1') -Raw
if($uninstall -notmatch "elseif \(\[string\]\`$pavCurrent -eq 'http://127\.0\.0\.1:1088/pavdns\.pac'\)"){throw 'Yedeksiz PAC geri donusu bulunamadi.'}
foreach($script in @('build.ps1','install.ps1','uninstall.ps1','prepare-release.ps1','tests\stress.ps1')){$tokens=$null;$errors=$null;[void][Management.Automation.Language.Parser]::ParseFile((Join-Path $root $script),[ref]$tokens,[ref]$errors);if($errors.Count){throw "$script PowerShell syntax error: $($errors[0].Message)"}}
Write-Output '[OK] Native kaynak kapsami, loopback, baglanti siniri ve betik sozdizimi dogrulandi.'
