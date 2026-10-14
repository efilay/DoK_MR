$ErrorActionPreference = 'Stop'
$projectPath = Split-Path -Parent $PSScriptRoot
$packagePath = Join-Path $projectPath 'release\KhaanephAssaultCruiser'
$payloadPath = Join-Path $packagePath 'payload'
$dependencyPath = Join-Path $PSScriptRoot 'bepinex\5.4.23.4'
New-Item -ItemType Directory -Path $payloadPath, (Join-Path $payloadPath 'BepInEx\core'), (Join-Path $payloadPath 'BepInEx\patchers'), (Join-Path $payloadPath 'BepInEx\config') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $dependencyPath 'winhttp.dll'), (Join-Path $dependencyPath '.doorstop_version'), (Join-Path $dependencyPath 'doorstop_config.ini') -Destination $payloadPath -Force
Get-ChildItem -LiteralPath (Join-Path $dependencyPath 'BepInEx\core') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $payloadPath 'BepInEx\core') -Force }
Copy-Item -LiteralPath (Join-Path $projectPath 'dist\KhaanephCruiser.Runtime.dll') -Destination (Join-Path $payloadPath 'BepInEx\core') -Force
Copy-Item -LiteralPath (Join-Path $projectPath 'dist\KhaanephCruiser.Patcher.dll') -Destination (Join-Path $payloadPath 'BepInEx\patchers') -Force
$modelPayloadPath = Join-Path $payloadPath 'BepInEx\assets\KhaanephCruiser'
New-Item -ItemType Directory -Path $modelPayloadPath -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $projectPath 'assets\runtime-model\model.kac'), (Join-Path $projectPath 'assets\runtime-model\model.json') -Destination $modelPayloadPath -Force
$texturePayloadPath = Join-Path $modelPayloadPath 'textures'
New-Item -ItemType Directory -Path $texturePayloadPath -Force | Out-Null
Get-ChildItem -LiteralPath (Join-Path $projectPath 'assets\runtime-model\textures') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $texturePayloadPath -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1'), (Join-Path $PSScriptRoot 'uninstall.ps1'), (Join-Path $PSScriptRoot 'launch.ps1') -Destination $packagePath -Force
Copy-Item -LiteralPath (Join-Path $projectPath 'docs\README.md') -Destination $packagePath -Force
Copy-Item -LiteralPath (Join-Path $projectPath 'docs\runtime-validation.md') -Destination $packagePath -Force
Copy-Item -LiteralPath (Join-Path $projectPath 'docs\model-support.md') -Destination $packagePath -Force
Copy-Item -LiteralPath (Join-Path $projectPath 'docs\INSTALL.txt') -Destination $packagePath -Force
$files = Get-ChildItem -LiteralPath $payloadPath -File -Recurse | ForEach-Object {
    [ordered]@{ path = $_.FullName.Substring($payloadPath.Length + 1).Replace('\','/'); sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant(); size = $_.Length }
}
[ordered]@{ name = 'Khaaneph Assault Cruiser'; version = '1.1.0'; hp = 3650; scope = @('Skirmish','AISkirmish','Multiplayer'); files = @($files) } | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 -LiteralPath (Join-Path $packagePath 'manifest.json')
Write-Output "Package created: $packagePath"
