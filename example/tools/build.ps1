param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$managedPath = Join-Path $GamePath 'Data\Managed'
$corePath = Join-Path $PSScriptRoot 'bepinex\5.4.23.4\BepInEx\core'
$compilerPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$buildPath = Join-Path $projectPath 'dist'
New-Item -ItemType Directory -Path $buildPath -Force | Out-Null
$standardReferences = @('mscorlib.dll', 'System.dll', 'System.Core.dll') | ForEach-Object { '/reference:' + (Join-Path $managedPath $_) }
$runtimeReferences = @('UnityEngine.dll', 'BBI.Core.dll', 'BBI.Game.Data.dll', 'BBI.Unity.Core.dll', 'BBI.Unity.Core.Data.dll', 'BBI.Unity.Game.Data.dll', 'BBI.Unity.Game.dll') | ForEach-Object { '/reference:' + (Join-Path $managedPath $_) }
& $compilerPath /nologo /utf8output /noconfig /nostdlib+ /target:library /optimize+ /warn:4 /warnaserror+ @standardReferences @runtimeReferences ('/out:' + (Join-Path $buildPath 'KhaanephCruiser.Runtime.dll')) (Join-Path $projectPath 'src\Runtime.cs') (Join-Path $projectPath 'src\ModelAssets.cs') (Join-Path $projectPath 'src\NativeVoices.cs')
if ($LASTEXITCODE -ne 0) { throw 'Runtime DLL compilation failed.' }
& $compilerPath /nologo /utf8output /noconfig /nostdlib+ /target:library /optimize+ /warn:4 /warnaserror+ @standardReferences ('/reference:' + (Join-Path $corePath 'Mono.Cecil.dll')) ('/reference:' + (Join-Path $corePath 'BepInEx.dll')) ('/reference:' + (Join-Path $corePath 'BepInEx.Preloader.dll')) ('/out:' + (Join-Path $buildPath 'KhaanephCruiser.Patcher.dll')) (Join-Path $projectPath 'src\Patcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Preloader DLL compilation failed.' }
Get-Item -LiteralPath (Join-Path $buildPath 'KhaanephCruiser.Runtime.dll'), (Join-Path $buildPath 'KhaanephCruiser.Patcher.dll') | Select-Object Name, Length
