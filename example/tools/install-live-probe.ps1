param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak', [switch]$Remove)
$ErrorActionPreference='Stop'
if(Get-CimInstance Win32_Process -Filter "Name='DesertsOfKharak64.exe'"){throw 'Close the test game first'}
$probePath=Join-Path (Split-Path -Parent $PSScriptRoot) 'work\live-probe'
$recordPath=Join-Path $probePath 'installed-probe.json'
$prior=@{}
if(Test-Path -LiteralPath $recordPath){foreach($file in (Get-Content -Raw -LiteralPath $recordPath | ConvertFrom-Json)){$prior[$file.relativePath]=$file.sha256}}
$files=@('BepInEx\core\KhaanephCruiser.LiveProbe.dll','BepInEx\patchers\KhaanephCruiser.LiveProbe.Patcher.dll')
foreach($relativePath in $files){
    $targetPath=Join-Path $GamePath $relativePath
    if(Test-Path -LiteralPath $targetPath){
        if(-not $prior.ContainsKey($relativePath) -or (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash -ne $prior[$relativePath]){throw "Unowned probe file: $targetPath"}
    }
}
if($Remove){
    foreach($relativePath in $files){$targetPath=Join-Path $GamePath $relativePath; if(Test-Path -LiteralPath $targetPath){Remove-Item -LiteralPath $targetPath}}
    Write-Output 'Removed both temporary probe DLLs; logs retained.'
}else{
    $record=@()
    foreach($relativePath in $files){
        $sourcePath=Join-Path $probePath (Split-Path -Leaf $relativePath)
        Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $GamePath $relativePath) -Force
        $record += [pscustomobject]@{relativePath=$relativePath; sha256=(Get-FileHash -Algorithm SHA256 -LiteralPath $sourcePath).Hash}
    }
    $record | ConvertTo-Json | Set-Content -LiteralPath $recordPath -Encoding utf8
    Write-Output 'Installed temporary native live probe (excluded from release package).'
}
