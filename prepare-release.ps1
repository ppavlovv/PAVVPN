$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath($PSScriptRoot)
& (Join-Path $root 'build.ps1')
& (Join-Path $root 'tests\static.ps1')

$out=Join-Path $root 'releases'
[void][IO.Directory]::CreateDirectory($out)
$stamp=[DateTime]::Now.ToString('yyyyMMdd-HHmmss')
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

function New-PavArchive([string]$Path,[object[]]$Items,[string]$Manifest) {
    if(Test-Path -LiteralPath $Path){throw "Yayin dosyasi zaten var: $Path"}
    $stream=[IO.File]::Open($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $archive=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$false)
        try {
            foreach($item in $Items){[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive,[string]$item.Source,[string]$item.Entry,[IO.Compression.CompressionLevel]::Optimal)}
            if($Manifest){
                $entry=$archive.CreateEntry('SHA256SUMS.txt',[IO.Compression.CompressionLevel]::Optimal)
                $writer=[IO.StreamWriter]::new($entry.Open(),[Text.Encoding]::ASCII)
                try{$writer.Write($Manifest)}finally{$writer.Dispose()}
            }
        } finally {$archive.Dispose()}
    } finally {$stream.Dispose()}
}

$binaryNames=@('PAVVPN.exe','PAVVPN.GUI.sha256','PAVVPN.Native.v5.exe','PAVVPN.Native.v5.sha256','LICENSE','README.md','SECURITY.md','ARCHITECTURE.md','DEPENDENCIES.md','CHANGELOG.md','install.ps1','uninstall.ps1','pavvpn.bat','service_remove.bat')
$binaryItems=@($binaryNames|ForEach-Object{[pscustomobject]@{Source=(Join-Path $root $_);Entry=$_}})
$binaryItems += [pscustomobject]@{Source=(Join-Path $root 'Assets\pavlovbaba.ico');Entry='PAVVPN.ico'}
$manifest=($binaryItems|Sort-Object Entry|ForEach-Object{(Get-FileHash -LiteralPath $_.Source -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Entry}) -join [Environment]::NewLine
$manifest += [Environment]::NewLine
$binaryZip=Join-Path $out ('PAVVPN-v5-Windows-unsigned-'+$stamp+'.zip')
New-PavArchive $binaryZip $binaryItems $manifest

$sourceItems=New-Object Collections.Generic.List[object]
$sourceFiles=@('.gitignore','.gitattributes','.editorconfig','LICENSE','README.md','SECURITY.md','ARCHITECTURE.md','DEPENDENCIES.md','CHANGELOG.md','CONTRIBUTING.md','PavVpnControl.cs','PavVpnNative.cs','build.ps1','install.ps1','uninstall.ps1','prepare-release.ps1','pavvpn.bat','service_remove.bat')
foreach($name in $sourceFiles){$sourceItems.Add([pscustomobject]@{Source=(Join-Path $root $name);Entry=$name})}
foreach($directory in @('.github','Assets','tests')){
    Get-ChildItem -LiteralPath (Join-Path $root $directory) -Recurse -File | ForEach-Object {
        $entry=$_.FullName.Substring($root.Length+1).Replace('\','/')
        $sourceItems.Add([pscustomobject]@{Source=$_.FullName;Entry=$entry})
    }
}
$sourceZip=Join-Path $out ('PAVVPN-v5-source-'+$stamp+'.zip')
New-PavArchive $sourceZip $sourceItems.ToArray() $null

Write-Output ('Windows paketi: '+$binaryZip)
Write-Output ('Windows SHA256: '+(Get-FileHash -LiteralPath $binaryZip -Algorithm SHA256).Hash.ToLowerInvariant())
Write-Output ('Kaynak paketi:  '+$sourceZip)
Write-Output ('Kaynak SHA256:  '+(Get-FileHash -LiteralPath $sourceZip -Algorithm SHA256).Hash.ToLowerInvariant())
