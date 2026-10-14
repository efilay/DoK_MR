param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak')
$ErrorActionPreference = 'Stop'
$GamePath = [IO.Path]::GetFullPath($GamePath).TrimEnd('\')
if (Get-CimInstance Win32_Process -Filter "Name='DesertsOfKharak64.exe'") { throw 'Close Deserts of Kharak before uninstalling the patch.' }
$manifestPath = Join-Path $GamePath 'BepInEx\KhaanephCruiser.install.json'
if (-not (Test-Path -LiteralPath $manifestPath)) { throw 'No cruiser patch installation manifest found.' }
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
# Validate every resolved absolute path and current hash before any removal. No recursive deletes.
$targets = @()
foreach ($file in $manifest.files) {
    $target = [IO.Path]::GetFullPath((Join-Path $GamePath $file.path))
    if (-not $target.StartsWith($GamePath + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe uninstall path in manifest.' }
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -Algorithm SHA256 -LiteralPath $target).Hash.ToLowerInvariant() -ne $file.sha256) { throw "Installed file changed since installation: $($file.path). No files removed." }
        $targets += $target
    }
}
foreach ($target in $targets) { Remove-Item -LiteralPath $target }
Remove-Item -LiteralPath $manifestPath
Write-Output 'Cruiser DLL patch removed. Original game files and generated logs/configuration were retained.'
