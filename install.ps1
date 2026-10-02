param([switch]$VerifyOnly)

$ErrorActionPreference = 'Stop'
function Get-PavSha256([string]$Path) {
    $sha=[Security.Cryptography.SHA256]::Create()
    $stream=[IO.File]::OpenRead([IO.Path]::GetFullPath($Path))
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant() }
    finally { $stream.Dispose(); $sha.Dispose() }
}
$sourceRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$localBase = [IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData')).TrimEnd('\')
$installRoot = [IO.Path]::GetFullPath((Join-Path $localBase 'PAVVPN'))
if ([IO.Path]::GetDirectoryName($installRoot) -ne $localBase -or [IO.Path]::GetFileName($installRoot) -ne 'PAVVPN') { throw 'Guvenli kurulum yolu dogrulanamadi.' }
$exeName = 'PAVVPN.Native.v5.exe'
$sourceExe = Join-Path $sourceRoot $exeName
$hashFile = Join-Path $sourceRoot 'PAVVPN.Native.v5.sha256'
$guiName = 'PAVVPN.exe'
$sourceGui = Join-Path $sourceRoot $guiName
$guiHashFile = Join-Path $sourceRoot 'PAVVPN.GUI.sha256'
$sourceIcon = Join-Path $sourceRoot 'PAVVPN.ico'
if (-not (Test-Path -LiteralPath $sourceIcon -PathType Leaf)) { $sourceIcon = Join-Path $sourceRoot 'Assets\pavlovbaba.ico' }
foreach ($file in @($sourceExe,$hashFile,$sourceGui,$guiHashFile,$sourceIcon,(Join-Path $sourceRoot 'LICENSE'),(Join-Path $sourceRoot 'README.md'),(Join-Path $sourceRoot 'SECURITY.md'),(Join-Path $sourceRoot 'uninstall.ps1'),(Join-Path $sourceRoot 'service_remove.bat'))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Eksik paket dosyasi: $file" }
}
$expectedHash = ((Get-Content -LiteralPath $hashFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($expectedHash -notmatch '^[0-9a-f]{64}$' -or (Get-PavSha256 $sourceExe) -ne $expectedHash) { throw 'Native EXE SHA256 dogrulamasi basarisiz.' }
$expectedGuiHash = ((Get-Content -LiteralPath $guiHashFile -Raw).Trim() -split '\s+')[0].ToLowerInvariant()
if ($expectedGuiHash -notmatch '^[0-9a-f]{64}$' -or (Get-PavSha256 $sourceGui) -ne $expectedGuiHash) { throw 'PAVVPN arayuz SHA256 dogrulamasi basarisiz.' }
$manifest = Join-Path $sourceRoot 'SHA256SUMS.txt'
if (Test-Path -LiteralPath $manifest) {
    foreach ($line in Get-Content -LiteralPath $manifest) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = $line -split '  ',2
        if ($parts.Count -ne 2 -or $parts[0] -notmatch '^[0-9a-fA-F]{64}$') { throw 'Gecersiz SHA256 manifesti.' }
        $file = [IO.Path]::GetFullPath((Join-Path $sourceRoot $parts[1].Replace('/','\')))
        if (-not $file.StartsWith($sourceRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or -not (Test-Path -LiteralPath $file -PathType Leaf)) { throw 'Manifest yolu paket disina cikiyor veya eksik.' }
        if ((Get-PavSha256 $file) -ne $parts[0].ToLowerInvariant()) { throw "Paket butunluk hatasi: $($parts[1])" }
    }
}
$selfTest = Start-Process -FilePath $sourceExe -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($selfTest.ExitCode -ne 0) { throw 'Native motor self-test basarisiz.' }
$guiSelfTest = Start-Process -FilePath $sourceGui -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
if ($guiSelfTest.ExitCode -ne 0) { throw 'PAVVPN arayuz self-test basarisiz.' }
if ($VerifyOnly) { Write-Output '[OK] Motor, arayuz, hedef yol, SHA256 ve self-testler dogrulandi.'; return }

try {
    Write-Output '[1/5] Onceki PAVVPN oturumu durduruluyor...'
    foreach ($candidate in @((Join-Path $installRoot 'PAVVPN.Native.v5.exe'),(Join-Path $installRoot 'PavDiscord.v4.5.exe'),(Join-Path $installRoot 'PavDiscord.v4.4.exe'))) {
        if (Test-Path -LiteralPath $candidate) { try { Start-Process -FilePath $candidate -ArgumentList '--stop' -WindowStyle Hidden -Wait | Out-Null } catch { } }
    }
    # ExecutablePath may be hidden by Windows when an older copy was launched
    # elevated. Exact product process names are used as a bounded fallback so a
    # stale GUI cannot restart the engine while files are being replaced.
    Get-Process -Name 'PAVVPN','PAVVPN.Native.v5' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
    for ($stopAttempt = 0; $stopAttempt -lt 20; $stopAttempt++) {
        $pavStillRunning = @(Get-Process -Name 'PAVVPN','PAVVPN.Native.v5' -ErrorAction SilentlyContinue).Count -gt 0
        $pavPortBusy = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
            Where-Object { $_.Address.Equals([Net.IPAddress]::Loopback) -and $_.Port -eq 1088 } |
            Select-Object -First 1
        if (-not $pavStillRunning -and -not $pavPortBusy) { break }
        Start-Sleep -Milliseconds 250
    }
    if (@(Get-Process -Name 'PAVVPN','PAVVPN.Native.v5' -ErrorAction SilentlyContinue).Count -gt 0) {
        throw 'Onceki PAVVPN islemi durdurulamadi. Uygulamayi yonetici olarak actiysaniz once normal kullanici olarak kapatin.'
    }
    $pavPortBusy = [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object { $_.Address.Equals([Net.IPAddress]::Loopback) -and $_.Port -eq 1088 } |
        Select-Object -First 1
    if ($pavPortBusy) { throw '127.0.0.1:1088 portu baska bir uygulama tarafindan kullaniliyor.' }

    Write-Output '[2/5] PAVVPN uygulamasi ve native motor kuruluyor...'
    [void][IO.Directory]::CreateDirectory($installRoot)
    $legacyEngine = [IO.Path]::GetFullPath((Join-Path $installRoot 'third_party'))
    if ($legacyEngine.StartsWith($installRoot+'\',[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $legacyEngine)) { Remove-Item -LiteralPath $legacyEngine -Recurse -Force }
    Get-ChildItem -LiteralPath $installRoot -Filter 'PavDiscord.v4*.exe' -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Copy-Item -LiteralPath $sourceExe -Destination (Join-Path $installRoot $exeName) -Force
    Copy-Item -LiteralPath $sourceGui -Destination (Join-Path $installRoot $guiName) -Force
    Copy-Item -LiteralPath $sourceIcon -Destination (Join-Path $installRoot 'PAVVPN.ico') -Force
    foreach ($name in @('LICENSE','README.md','SECURITY.md','uninstall.ps1','service_remove.bat')) { Copy-Item -LiteralPath (Join-Path $sourceRoot $name) -Destination (Join-Path $installRoot $name) -Force }
    if ((Get-PavSha256 (Join-Path $installRoot $exeName)) -ne $expectedHash) { throw 'Kurulan EXE hash degeri degisti.' }
    if ((Get-PavSha256 (Join-Path $installRoot $guiName)) -ne $expectedGuiHash) { throw 'Kurulan arayuz hash degeri degisti.' }

    Write-Output '[3/5] Kisayollar ve kullanici Baslangic girdisi olusturuluyor...'
    $desktop = [Environment]::GetFolderPath('DesktopDirectory')
    $shell = New-Object -ComObject WScript.Shell
    foreach ($oldLinkName in @('PAVVPN - Discord.lnk','PAVVPN - Discord Web (Chrome).lnk','PAVVPN - Kapat.lnk','PAVVPN - Kaldir.lnk')) {
        $oldLink=Join-Path $desktop $oldLinkName
        if(Test-Path -LiteralPath $oldLink){Remove-Item -LiteralPath $oldLink -Force}
    }
    $appLink=$shell.CreateShortcut((Join-Path $desktop 'PAVVPN.lnk')); $appLink.TargetPath=Join-Path $installRoot $guiName; $appLink.WorkingDirectory=$installRoot; $appLink.Description='PAVVPN - Discord baglanti yoneticisi'; $appLink.IconLocation=(Join-Path $installRoot 'PAVVPN.ico')+',0'; $appLink.Save()
    if (-not ('PavVpn.ShellRefresh' -as [type])) {
        Add-Type -Namespace PavVpn -Name ShellRefresh -MemberDefinition '[DllImport("shell32.dll")] public static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);'
    }
    [PavVpn.ShellRefresh]::SHChangeNotify(0x08000000,0,[IntPtr]::Zero,[IntPtr]::Zero)
    $startupFile=Join-Path ([Environment]::GetFolderPath('Startup')) 'PAVVPN_AutoStart.vbs'
    $q=[string][char]34
    [IO.File]::WriteAllLines($startupFile,[string[]]@('Option Explicit','Dim shell',('Set shell = CreateObject('+$q+'WScript.Shell'+$q+')'),'WScript.Sleep 8000',('shell.Run '+$q+$q+$q+(Join-Path $installRoot $guiName)+$q+$q+' --startup'+$q+', 0, False')),[Text.Encoding]::ASCII)

    Write-Output '[4/5] Native motor baslatiliyor ve dogrulaniyor...'
    $installedExe=Join-Path $installRoot $exeName
    $launch=Start-Process -FilePath $installedExe -ArgumentList '--proxy-only' -WorkingDirectory $installRoot -WindowStyle Hidden -PassThru
    $ready=$false
    for($i=0;$i -lt 80;$i++){Start-Sleep -Milliseconds 250;try{if((Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:1088/pavdns-ready' -TimeoutSec 1).Content -eq 'PAVVPN/5.0-native'){$ready=$true;break}}catch{};if($launch.HasExited){break}}
    if(-not $ready){throw 'Native motor canli baglanti dogrulamasini gecemedi.'}
    if($launch.HasExited){throw "Native motor baslangictan sonra kapandi: $($launch.ExitCode)"}
    $pac=$null
    for($i=0;$i -lt 20;$i++){$pac=(Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -Name AutoConfigURL -ErrorAction SilentlyContinue).AutoConfigURL;if($pac -eq 'http://127.0.0.1:1088/pavdns.pac'){break};Start-Sleep -Milliseconds 250}
    if($pac -ne 'http://127.0.0.1:1088/pavdns.pac'){throw 'Discord PAC ayari hazir durumundan sonra dogrulanamadi.'}
    Write-Output '[5/5] PAVVPN uygulamasi, tek kisayol ve canli kontrol tamamlandi.'
    Start-Process -FilePath (Join-Path $installRoot $guiName) -WorkingDirectory $installRoot | Out-Null
} catch {
    $failure=$_
    Write-Warning 'Kurulum tamamlanamadi; degisiklikler geri aliniyor.'
    try { & (Join-Path $sourceRoot 'uninstall.ps1') } catch { Write-Warning ('Geri alma hatasi: '+$_.Exception.Message) }
    throw $failure
}
