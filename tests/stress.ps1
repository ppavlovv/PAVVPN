param(
    [ValidateRange(10,500)][int]$Requests = 100,
    [ValidateRange(1,20)][int]$RestartCycles = 5
)

$ErrorActionPreference = 'Stop'
$sourceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installed = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PAVVPN\PAVVPN.Native.v5.exe'
$engine = if (Test-Path -LiteralPath $installed -PathType Leaf) { $installed } else { Join-Path $sourceRoot 'PAVVPN.Native.v5.exe' }
if (-not (Test-Path -LiteralPath $engine -PathType Leaf)) { throw 'PAVVPN motoru bulunamadi.' }

function Test-Ready {
    try { return (Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:1088/pavdns-ready' -TimeoutSec 1).Content -eq 'PAVVPN/5.0-native' }
    catch { return $false }
}
function Test-Listening {
    try { return (Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:1088/pavdns-version' -TimeoutSec 1).Content -eq 'PAVVPN/5.0-native' }
    catch { return $false }
}
function Wait-Ready([bool]$Expected) {
    $attempts=if($Expected){160}else{40}
    for ($i=0; $i -lt $attempts; $i++) {
        if ((Test-Ready) -eq $Expected) { return }
        Start-Sleep -Milliseconds 125
    }
    throw "Motor durumu zaman asimina ugradi (beklenen=$Expected)."
}
function Wait-Stopped {
    for($i=0;$i -lt 80;$i++){
        if(-not (Test-Listening)){return}
        Start-Sleep -Milliseconds 125
    }
    throw 'Motor portu zamaninda kapanmadi.'
}

if (-not (Test-Ready)) {
    Start-Process -FilePath $engine -ArgumentList '--proxy-only' -WorkingDirectory ([IO.Path]::GetDirectoryName($engine)) -WindowStyle Hidden | Out-Null
    Wait-Ready $true
}

$listener = Get-NetTCPConnection -State Listen -LocalPort 1088 -ErrorAction Stop
if (@($listener | Where-Object LocalAddress -ne '127.0.0.1').Count -ne 0) { throw 'Proxy loopback disinda bir adreste dinliyor.' }

$tcp = New-Object Net.Sockets.TcpClient
try {
    $tcp.Connect('127.0.0.1',1088)
    $stream=$tcp.GetStream(); $hello=[byte[]](5,1,0); $stream.Write($hello,0,$hello.Length)
    $reply=New-Object byte[] 2; if($stream.Read($reply,0,2)-ne 2 -or $reply[0]-ne 5 -or $reply[1]-ne 0){throw 'SOCKS5 el sikisma testi basarisiz.'}
} finally { $tcp.Dispose() }

$blocked = & curl.exe --silent --output NUL --write-out '%{http_connect}' --max-time 10 --proxy 'http://127.0.0.1:1088' 'https://example.com/'
if ([string]$blocked -ne '403') { throw "Discord disi HTTPS CONNECT reddedilmedi (HTTP $blocked)." }

$curlArgs = @('--parallel','--parallel-immediate','--parallel-max',[string][Math]::Min($Requests,128),'--silent','--show-error','--output','NUL','--write-out','%{http_code}\n','--max-time','25','--proxy','http://127.0.0.1:1088')
for($i=0;$i -lt $Requests;$i++){ $curlArgs += 'https://discord.com/api/v9/gateway' }
$process = Get-Process -Name 'PAVVPN.Native.v5' -ErrorAction Stop | Select-Object -First 1
$cpuBefore = $process.CPU
$watch=[Diagnostics.Stopwatch]::StartNew()
$codes = @(& curl.exe @curlArgs)
$curlExit=$LASTEXITCODE
$watch.Stop()
if($curlExit -ne 0){throw "curl yuk testi hata kodu: $curlExit"}
$ok=@($codes | Where-Object { $_ -eq '200' }).Count
if($ok -ne $Requests){throw "TLS yuk testi eksik: $ok/$Requests HTTP 200"}
$process.Refresh(); $cpuDelta=[Math]::Round($process.CPU-$cpuBefore,3); $ram=[Math]::Round($process.WorkingSet64/1MB,1)

for($cycle=1;$cycle -le $RestartCycles;$cycle++){
    $stop=Start-Process -FilePath $engine -ArgumentList '--stop' -WindowStyle Hidden -Wait -PassThru
    if($stop.ExitCode -ne 0){throw "Durdurma cevrimi $cycle basarisiz."}
    Wait-Stopped
    Start-Process -FilePath $engine -ArgumentList '--proxy-only' -WorkingDirectory ([IO.Path]::GetDirectoryName($engine)) -WindowStyle Hidden | Out-Null
    Wait-Ready $true
}

Write-Output "[OK] $Requests/$Requests gercek Discord TLS istegi HTTP 200 ($([Math]::Round($watch.Elapsed.TotalSeconds,2)) sn)."
Write-Output "[OK] SOCKS5, dis alan 403 ve yalniz 127.0.0.1 dinleme testleri gecti."
Write-Output "[OK] $RestartCycles/$RestartCycles durdur/baslat cevrimi gecti."
Write-Output "[BILGI] Yuk sirasinda motor CPU farki ${cpuDelta} sn; son RAM ${ram} MiB."
