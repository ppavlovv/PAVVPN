$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($PSScriptRoot)
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework C# compiler bulunamadi.' }
$engineOutput = Join-Path $root 'PAVVPN.Native.v5.exe'
$guiOutput = Join-Path $root 'PAVVPN.exe'
$logo = Join-Path $root 'Assets\pavlov_logo_ui.png'
$icon = Join-Path $root 'Assets\pavlovbaba.ico'
foreach ($asset in @($logo,$icon)) { if (-not (Test-Path -LiteralPath $asset -PathType Leaf)) { throw "Eksik arayuz varligi: $asset" } }

& $compiler /nologo /warnaserror+ /target:winexe /reference:System.Web.Extensions.dll /optimize+ "/out:$engineOutput" (Join-Path $root 'PavVpnNative.cs')
if ($LASTEXITCODE -ne 0) { throw 'Native motor derlemesi basarisiz.' }

& $compiler /nologo /warnaserror+ /target:winexe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /optimize+ "/resource:$logo,PAVVPN.Logo" "/win32icon:$icon" "/out:$guiOutput" (Join-Path $root 'PavVpnControl.cs')
if ($LASTEXITCODE -ne 0) { throw 'PAVVPN arayuz derlemesi basarisiz.' }

foreach ($binary in @($engineOutput,$guiOutput)) {
    $test = Start-Process -FilePath $binary -ArgumentList '--self-test' -WindowStyle Hidden -Wait -PassThru
    if ($test.ExitCode -ne 0) { throw "Self-test basarisiz ($([IO.Path]::GetFileName($binary))): $($test.ExitCode)" }
}

$engineHash = (Get-FileHash -LiteralPath $engineOutput -Algorithm SHA256).Hash.ToLowerInvariant()
$guiHash = (Get-FileHash -LiteralPath $guiOutput -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $root 'PAVVPN.Native.v5.sha256'), $engineHash + '  PAVVPN.Native.v5.exe' + [Environment]::NewLine, [Text.Encoding]::ASCII)
[IO.File]::WriteAllText((Join-Path $root 'PAVVPN.GUI.sha256'), $guiHash + '  PAVVPN.exe' + [Environment]::NewLine, [Text.Encoding]::ASCII)
Write-Output "[OK] Motor ve PAVVPN arayuzu derlendi; iki self-test gecti."
Write-Output "Engine SHA256: $engineHash"
Write-Output "GUI SHA256:    $guiHash"
