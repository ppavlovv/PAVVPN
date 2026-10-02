param([switch]$VerifyOnly,[switch]$Confirm)

$ErrorActionPreference = 'Stop'

$pavLocalBase = [IO.Path]::GetFullPath([Environment]::GetFolderPath('LocalApplicationData')).TrimEnd('\')
$pavInstallRoot = [IO.Path]::GetFullPath((Join-Path $pavLocalBase 'PAVVPN'))
$pavExpectedRoot = Join-Path $pavLocalBase 'PAVVPN'
if ($pavInstallRoot -ne $pavExpectedRoot -or [IO.Path]::GetDirectoryName($pavInstallRoot) -ne $pavLocalBase -or [IO.Path]::GetFileName($pavInstallRoot) -ne 'PAVVPN') {
    throw 'Guvenli kaldirma yolu dogrulanamadi; hicbir dosya silinmedi.'
}
if ($Confirm) {
    $host.UI.RawUI.WindowTitle = 'PAVVPN - Guvenli Kaldirma'
    Write-Output '================================================================'
    Write-Output 'PAVVPN - KALDIRMA'
    Write-Output '================================================================'
    Write-Output ''
    Write-Output 'Bu islem PAVVPN islemlerini durdurur; PAC ayarini, Baslangic girdisini,'
    Write-Output 'masaustu kisayolunu ve LOCALAPPDATA\PAVVPN klasorunu kaldirir.'
    Write-Output 'GitHub kaynak klasoru ve indirdiginiz ZIP silinmez.'
    Write-Output ''
    $pavConfirmText = Read-Host 'Devam etmek icin KALDIR yazip ENTER tusuna basin'
    if ($pavConfirmText -ine 'KALDIR') {
        Write-Output '[BILGI] Kaldirma iptal edildi; hicbir sey silinmedi.'
        [void](Read-Host 'Kapatmak icin ENTER tusuna basin')
        if ($PSCommandPath.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue }
        return
    }
    Write-Output ''
}
if ($VerifyOnly) {
    Write-Output "[OK] Kaldirma hedefi kesin olarak dogrulandi: $pavInstallRoot"
    Write-Output '[OK] Kaynak/ZIP klasoru kaldirma kapsaminda degil.'
    return
}

Write-Output '[1/7] Gecici Discord ayari geri aliniyor ve PAVVPN durduruluyor...'
$pavCandidates = @(
    (Join-Path $pavInstallRoot 'PAVVPN.Native.v5.exe'),
    (Join-Path $pavInstallRoot 'PavDiscord.v4.5.exe'),
    (Join-Path $pavInstallRoot 'PavDiscord.v4.4.exe'),
    (Join-Path $pavInstallRoot 'PavDiscord.v4.3.exe'),
    (Join-Path $PSScriptRoot 'PAVVPN.Native.v5.exe'),
    (Join-Path $PSScriptRoot 'PavDiscord.v4.5.exe'),
    (Join-Path $PSScriptRoot 'PavDiscord.v4.4.exe'),
    (Join-Path $PSScriptRoot 'PavDiscord.v4.3.exe')
) | Select-Object -Unique
foreach ($pavExe in $pavCandidates) {
    if (-not (Test-Path -LiteralPath $pavExe -PathType Leaf)) { continue }
    try {
        $pavStop = Start-Process -FilePath $pavExe -ArgumentList '--stop' -WindowStyle Hidden -Wait -PassThru
        if ($pavStop.ExitCode -eq 0) { break }
    } catch { }
}
Start-Sleep -Milliseconds 800

Write-Output '[2/7] Kullanici PAC ayari dogrudan dogrulaniyor ve gerekirse geri aliniyor...'
$pavNetworkBackup = Join-Path $pavInstallRoot 'pavvpn-network-settings.json'
$pavInternetKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings'
$pavCurrent = (Get-ItemProperty -LiteralPath $pavInternetKey -Name AutoConfigURL -ErrorAction SilentlyContinue).AutoConfigURL
$pavPacChanged = $false
if (Test-Path -LiteralPath $pavNetworkBackup -PathType Leaf) {
    $pavBackup = Get-Content -LiteralPath $pavNetworkBackup -Raw | ConvertFrom-Json
    if ([string]$pavCurrent -eq [string]$pavBackup.endpoint) {
        if ([bool]$pavBackup.hadValue) {
            New-ItemProperty -LiteralPath $pavInternetKey -Name AutoConfigURL -Value ([string]$pavBackup.oldValue) -PropertyType String -Force | Out-Null
        } else {
            Remove-ItemProperty -LiteralPath $pavInternetKey -Name AutoConfigURL -ErrorAction SilentlyContinue
        }
        $pavPacChanged = $true
    }
    Remove-Item -LiteralPath $pavNetworkBackup -Force
} elseif ([string]$pavCurrent -eq 'http://127.0.0.1:1088/pavdns.pac') {
    Remove-ItemProperty -LiteralPath $pavInternetKey -Name AutoConfigURL -ErrorAction SilentlyContinue
    $pavPacChanged = $true
}
if ($pavPacChanged) {
    if (-not ('PavVpn.WinInet' -as [type])) {
        $pavWinInetDefinition = '[DllImport("wininet.dll", SetLastError=true)] public static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);'
        Add-Type -Namespace PavVpn -Name WinInet -MemberDefinition $pavWinInetDefinition.Replace([string][char]92, '')
    }
    [void][PavVpn.WinInet]::InternetSetOption([IntPtr]::Zero, 39, [IntPtr]::Zero, 0)
    [void][PavVpn.WinInet]::InternetSetOption([IntPtr]::Zero, 37, [IntPtr]::Zero, 0)
}

Write-Output '[3/7] PAVVPN ile acilan Discord ve eski ayrilmis Chrome islemleri kapatiliyor...'
$pavProxyArgument = '--proxy-server=http://127.0.0.1:1088'
$pavProfileMarker = (Join-Path $pavInstallRoot 'ChromeProfile')
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    ($_.Name -eq 'chrome.exe' -and $_.CommandLine -and $_.CommandLine.IndexOf($pavProfileMarker, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
    ($_.Name -eq 'Discord.exe' -and $_.CommandLine -and $_.CommandLine.IndexOf($pavProxyArgument, [StringComparison]::OrdinalIgnoreCase) -ge 0)
} | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}

Write-Output '[4/7] Yalnizca kurulum klasorundeki kalan PAVVPN islemleri sonlandiriliyor...'
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object {
    $_.ExecutablePath -and $_.ExecutablePath.StartsWith($pavInstallRoot + '\', [StringComparison]::OrdinalIgnoreCase)
} | ForEach-Object {
    Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
}
# Windows can hide ExecutablePath for a copy previously launched elevated.
# Exact product names keep this fallback narrow while ensuring removal is complete.
Get-Process -Name 'PAVVPN','PAVVPN.Native.v5' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
for ($pavStopAttempt = 0; $pavStopAttempt -lt 20; $pavStopAttempt++) {
    if (@(Get-Process -Name 'PAVVPN','PAVVPN.Native.v5' -ErrorAction SilentlyContinue).Count -eq 0) { break }
    Start-Sleep -Milliseconds 250
}
if (@(Get-Process -Name 'PAVVPN','PAVVPN.Native.v5' -ErrorAction SilentlyContinue).Count -gt 0) {
    throw 'PAVVPN islemleri durdurulamadi; kurulum klasoru guvenle kaldirilamiyor.'
}

Write-Output '[5/7] Masaustu kisayollari ve kullanici Baslangic girdisi kaldiriliyor...'
$pavDesktop = [Environment]::GetFolderPath('DesktopDirectory')
foreach ($pavLinkName in @('PAVVPN.lnk','PAVVPN - Discord.lnk','PAVVPN - Discord Web (Chrome).lnk','PAVVPN - Kapat.lnk','PAVVPN - Kaldir.lnk')) {
    $pavLink = Join-Path $pavDesktop $pavLinkName
    if (Test-Path -LiteralPath $pavLink) { Remove-Item -LiteralPath $pavLink -Force }
}
foreach ($pavStartupName in @('PavDiscord_Auto.vbs','PAVVPN_AutoStart.vbs')) {
    $pavStartup = Join-Path ([Environment]::GetFolderPath('Startup')) $pavStartupName
    if (Test-Path -LiteralPath $pavStartup) { Remove-Item -LiteralPath $pavStartup -Force }
}
$pavRunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$pavRunValue = (Get-ItemProperty -LiteralPath $pavRunKey -Name 'PAVVPN' -ErrorAction SilentlyContinue).PAVVPN
$pavExpectedGui = Join-Path $pavInstallRoot 'PAVVPN.exe'
if ($pavRunValue -and $pavRunValue.IndexOf($pavExpectedGui, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
    Remove-ItemProperty -LiteralPath $pavRunKey -Name 'PAVVPN' -ErrorAction SilentlyContinue
}

Write-Output '[6/7] Eski ayrilmis web profilleri varsa kaldiriliyor...'
$pavLegacyRoot = [IO.Path]::GetFullPath((Join-Path $pavLocalBase 'PAVDNS'))
if ([IO.Path]::GetDirectoryName($pavLegacyRoot) -eq $pavLocalBase -and [IO.Path]::GetFileName($pavLegacyRoot) -eq 'PAVDNS') {
    $pavLegacyProfile = Join-Path $pavLegacyRoot 'WebProfile'
    if (Test-Path -LiteralPath $pavLegacyProfile) { Remove-Item -LiteralPath $pavLegacyProfile -Recurse -Force }
    if (Test-Path -LiteralPath $pavLegacyRoot) {
        $pavLegacyItems = @(Get-ChildItem -LiteralPath $pavLegacyRoot -Force)
        if ($pavLegacyItems.Count -eq 0) { Remove-Item -LiteralPath $pavLegacyRoot -Force }
    }
}

Write-Output '[7/7] LOCALAPPDATA\PAVVPN kurulumu kaldiriliyor...'
for ($pavRemoveAttempt = 0; $pavRemoveAttempt -lt 20 -and (Test-Path -LiteralPath $pavInstallRoot); $pavRemoveAttempt++) {
    try { Remove-Item -LiteralPath $pavInstallRoot -Recurse -Force -ErrorAction Stop }
    catch {
        if ($pavRemoveAttempt -eq 19) { throw }
        Start-Sleep -Milliseconds 250
    }
}
if (Test-Path -LiteralPath $pavInstallRoot) { throw 'Kurulum klasoru tamamen kaldirilamadi.' }
Write-Output 'PAVVPN, PAC ayari, baslangic girdisi, kurulum dosyalari ve kisayollar kaldirildi.'
if ($Confirm) {
    Write-Output '[OK] Bilgisayar normal ag ayarlarina geri getirildi.'
    [void](Read-Host 'Kapatmak icin ENTER tusuna basin')
    if ($PSCommandPath.StartsWith([IO.Path]::GetFullPath($env:TEMP).TrimEnd('\')+'\',[StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue }
}
