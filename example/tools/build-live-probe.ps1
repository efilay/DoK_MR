param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$managedPath = Join-Path $GamePath 'Data\Managed'
$corePath = Join-Path $PSScriptRoot 'bepinex\5.4.23.4\BepInEx\core'
$compilerPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$outputPath = Join-Path $projectPath 'work\live-probe'
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$references = @('mscorlib.dll','System.dll','System.Core.dll','UnityEngine.dll','NGUI.dll','BBI.Core.dll','BBI.Game.dll','BBI.Game.Data.dll','BBI.Unity.Core.dll','BBI.Unity.Core.Data.dll','BBI.Unity.Game.Data.dll','BBI.Unity.Game.dll') | ForEach-Object { '/reference:' + (Join-Path $managedPath $_) }
& $compilerPath /nologo /noconfig /nostdlib+ /target:library /optimize+ /warn:4 /warnaserror+ @references ('/reference:' + (Join-Path $projectPath 'dist\KhaanephCruiser.Runtime.dll')) ('/out:' + (Join-Path $outputPath 'KhaanephCruiser.LiveProbe.dll')) (Join-Path $projectPath 'tests\LiveProbe.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe compilation failed' }
$standardReferences = @('mscorlib.dll','System.dll','System.Core.dll') | ForEach-Object { '/reference:' + (Join-Path $managedPath $_) }
& $compilerPath /nologo /noconfig /nostdlib+ /target:library /optimize+ /warn:4 /warnaserror+ @standardReferences ('/reference:' + (Join-Path $corePath 'Mono.Cecil.dll')) ('/reference:' + (Join-Path $corePath 'BepInEx.dll')) ('/reference:' + (Join-Path $corePath 'BepInEx.Preloader.dll')) ('/out:' + (Join-Path $outputPath 'KhaanephCruiser.LiveProbe.Patcher.dll')) (Join-Path $projectPath 'tests\LiveProbePatcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Probe preloader compilation failed' }
