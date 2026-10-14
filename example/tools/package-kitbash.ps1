$ErrorActionPreference = 'Stop'
$modelProject = Split-Path $PSScriptRoot -Parent
$modelRoot = (Resolve-Path -LiteralPath (Join-Path $modelProject 'assets\kitbash\khaaneph-assault-cruiser')).Path
$modelEvidence = Join-Path $modelProject 'evidence\models'
$modelPackage = Join-Path (Split-Path $modelRoot -Parent) 'Khaaneph_Assault_Cruiser_Model.zip'
$modelCheck = Get-Content -LiteralPath (Join-Path $modelEvidence 'kitbash-verification.json') -Raw | ConvertFrom-Json
if ($modelCheck.source.parts -ne 5 -or $modelCheck.source.bones -ne 57) { throw 'Unexpected verified model' }
foreach ($modelFormat in @('glb','fbx')) {
    if ($modelCheck.roundtrips.$modelFormat.bones -ne 57 -or $modelCheck.roundtrips.$modelFormat.max_bounds_delta -ge .01) { throw "Unverified export: $modelFormat" }
}
Copy-Item -LiteralPath (Join-Path $modelEvidence 'kitbash-verification.json') -Destination (Join-Path $modelRoot 'export-verification.json') -Force
$modelNames = @('README.md','preview.html','kitbash-manifest.json','Khaaneph_Assault_Cruiser.blend','Khaaneph_Assault_Cruiser.glb','Khaaneph_Assault_Cruiser.fbx','front.png','rear.png','top.png','jaw-detail.png','bridge-detail.png','missile-recess-detail.png','original-comparison.png','khaaneph_production_reference.png','khaaneph_siege_reference.png','khaaneph_baserunner_reference.png','K_AssaultCruiser_HullPaint.png','export-verification.json')
$modelPaths = @($modelNames | ForEach-Object { Join-Path $modelRoot $_ })
$modelFiles = @($modelPaths | ForEach-Object {
    $modelItem = Get-Item -LiteralPath $_
    if ($modelItem.Length -le 0) { throw "Empty model file: $_" }
    [ordered]@{name=$modelItem.Name;bytes=$modelItem.Length;sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()}
})
$modelHtml = Get-Content -LiteralPath (Join-Path $modelRoot 'preview.html') -Raw
foreach ($modelReference in [regex]::Matches($modelHtml,'(?:src|href|data-file)="([^"]+)"')) {
    if (-not (Test-Path -LiteralPath (Join-Path $modelRoot $modelReference.Groups[1].Value))) { throw "Missing gallery file: $($modelReference.Groups[1].Value)" }
}
Compress-Archive -LiteralPath $modelPaths -DestinationPath $modelPackage -CompressionLevel Optimal -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$modelZip = [System.IO.Compression.ZipFile]::OpenRead($modelPackage)
try {
    if ($modelZip.Entries.Count -ne $modelNames.Count) { throw 'Package entry count mismatch' }
    foreach ($modelEntry in $modelZip.Entries) {
        $modelExpected = @($modelFiles | Where-Object { $_.name -eq $modelEntry.Name })
        if ($modelExpected.Count -ne 1) { throw "Unexpected package entry: $($modelEntry.FullName)" }
        $modelStream = $modelEntry.Open()
        $modelSha = [System.Security.Cryptography.SHA256]::Create()
        try { $modelActualHash = [System.BitConverter]::ToString($modelSha.ComputeHash($modelStream)).Replace('-','').ToLowerInvariant() }
        finally { $modelStream.Dispose(); $modelSha.Dispose() }
        if ($modelActualHash -ne $modelExpected[0].sha256) { throw "Package content mismatch: $($modelEntry.Name)" }
    }
} finally { $modelZip.Dispose() }
$modelReceipt = [ordered]@{
    verified_at=(Get-Date).ToString('o');package=$modelPackage
    package_bytes=(Get-Item -LiteralPath $modelPackage).Length
    package_sha256=(Get-FileHash -LiteralPath $modelPackage -Algorithm SHA256).Hash.ToLowerInvariant()
    entry_count=$modelNames.Count;all_zip_entry_hashes_match=$true;files=$modelFiles
    game_model_installed=(Test-Path -LiteralPath (Join-Path $modelProject 'evidence\runtime\kitbash-native-verification.json'))
    html_browser_check='Unavailable: browser policy blocks local file URLs; gallery file references checked and rendered PNG views visually inspected'
}
$modelReceipt | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $modelEvidence 'deliverable-integrity.json') -Encoding UTF8
"MODEL_PACKAGE_PASS: $($modelNames.Count) files; $([math]::Round($modelReceipt.package_bytes / 1MB, 1)) MB; each archived file matches its source hash"
