param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$managedPath = Join-Path $GamePath 'Data\Managed'
$corePath = Join-Path $PSScriptRoot 'bepinex\5.4.23.4\BepInEx\core'
$compilerPath = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$distPath = Join-Path $projectPath 'dist'
$testPath = Join-Path $distPath 'tests'
$evidencePath = Join-Path $projectPath 'evidence\code\verification'
New-Item -ItemType Directory -Path $testPath, $evidencePath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $distPath 'KhaanephCruiser.Patcher.dll'), (Join-Path $distPath 'KhaanephCruiser.Runtime.dll'), (Join-Path $corePath 'Mono.Cecil.dll') -Destination $testPath -Force
& $compilerPath /nologo /utf8output /target:exe /optimize+ ('/reference:' + (Join-Path $corePath 'Mono.Cecil.dll')) ('/reference:' + (Join-Path $distPath 'KhaanephCruiser.Patcher.dll')) ('/out:' + (Join-Path $testPath 'VerifyHooks.exe')) (Join-Path $projectPath 'tests\VerifyHooks.cs')
if ($LASTEXITCODE -ne 0) { throw 'Hook verifier compilation failed.' }
& (Join-Path $testPath 'VerifyHooks.exe') (Join-Path $managedPath 'BBI.Unity.Game.dll') $corePath $distPath (Join-Path $evidencePath 'BBI.Unity.Game.hook-test.dll')
if ($LASTEXITCODE -ne 0) { throw 'Hook verification failed.' }
& $compilerPath /nologo /utf8output /target:exe /optimize+ /reference:System.Web.Extensions.dll ('/reference:' + (Join-Path $distPath 'KhaanephCruiser.Runtime.dll')) ('/out:' + (Join-Path $testPath 'VerifyData.exe')) (Join-Path $projectPath 'tests\VerifyData.cs')
if ($LASTEXITCODE -ne 0) { throw 'Data verifier compilation failed.' }
& (Join-Path $testPath 'VerifyData.exe') $managedPath (Join-Path $projectPath 'evidence\resources\cruiser-transfer-evidence.json')
if ($LASTEXITCODE -ne 0) { throw 'Data verification failed.' }
