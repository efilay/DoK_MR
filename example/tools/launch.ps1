param([string]$GamePath = 'C:\Program Files (x86)\Steam\steamapps\common\Deserts of Kharak', [switch]$Windowed)
$ErrorActionPreference = 'Stop'
if (Get-CimInstance Win32_Process -Filter "Name='DesertsOfKharak64.exe'") { throw 'Deserts of Kharak is already running.' }
$options = @()
if ($Windowed) { $options = @('-screen-fullscreen','0','-screen-width','1280','-screen-height','720') }
if ($options.Count -gt 0) { Start-Process -FilePath (Join-Path $GamePath 'DesertsOfKharak64.exe') -WorkingDirectory $GamePath -ArgumentList $options }
else { Start-Process -FilePath (Join-Path $GamePath 'DesertsOfKharak64.exe') -WorkingDirectory $GamePath }
