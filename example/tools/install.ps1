param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak', [string]$PackagePath = $PSScriptRoot)
$ErrorActionPreference = 'Stop'
$GamePath = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
$PackagePath = [IO.Path]::GetFullPath($PackagePath)
if (Get-CimInstance Win32_Process -Filter "Name='DesertsOfKharak64.exe'") { throw 'Close Deserts of Kharak before installing or updating the patch.' }
$supported = @{
    'DesertsOfKharak64.exe' = '44085bc0c6d4cd59e9ce681160784d7bd024cd1c85e43edc9ab68b3c3d925e0c'
    'Data\Managed\BBI.Unity.Game.dll' = 'b4c2c7bdbf9737140ac18d6191e50ddd27030a669f050374e8f7e3da8c6ec0e5'
    'Data\Managed\BBI.Game.Data.dll' = '24a3fbf19e5435ec24295554e12f0ad2451d722623daaccc03bea280b33e9d40'
    'Data\Managed\BBI.Unity.Game.Data.dll' = 'f3a60df1ab8377492451181a957d1f1df7e3d47265bc0221e1dee83107e14c40'
    'Bundles\dlc\dlc0001' = '2d9e465475df7afbba01458e1f7330e13bcb3a032b2fda74ea6b5c434edb4217'
    'Bundles\dlc\dlc0002' = '459a264b3b094a16fd1613d463546834628249511ba051a9fc21598bb2a6cc3a'
}
foreach ($relativePath in $supported.Keys) {
    $path = Join-Path $GamePath $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Supported game file missing: $relativePath" }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant() -ne $supported[$relativePath]) { throw "Unsupported game file: $relativePath" }
}
$manifestPath = Join-Path $GamePath 'BepInEx\KhaanephCruiser.install.json'
$prior = @{}
if (Test-Path -LiteralPath $manifestPath) {
    $oldManifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    foreach ($file in $oldManifest.files) { $prior[$file.path] = $file.sha256 }
}
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PackagePath 'manifest.json') | ConvertFrom-Json
$payloadPath = Join-Path $PackagePath 'payload'
foreach ($file in $manifest.files) {
    $source = [IO.Path]::GetFullPath((Join-Path $payloadPath $file.path))
    $target = [IO.Path]::GetFullPath((Join-Path $GamePath $file.path))
    if (-not $source.StartsWith($payloadPath.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase) -or -not $target.StartsWith($GamePath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package path.' }
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $source).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Package hash mismatch: $($file.path)" }
    if (Test-Path -LiteralPath $target) {
        $current = (Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant()
        if (-not $prior.ContainsKey($file.path) -or $current -ne $prior[$file.path]) { throw "Existing unowned or modified loader file: $($file.path). Installation has not changed any files." }
    }
}
foreach ($file in $manifest.files) {
    $target = Join-Path $GamePath $file.path
    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $payloadPath $file.path) -Destination $target -Force
    if ((Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Installed file verification failed: $($file.path)" }
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 -LiteralPath $manifestPath
Write-Output "Installed Khaaneph Assault Cruiser DLL patch at $GamePath. Original executables, assemblies and assets were preserved."
