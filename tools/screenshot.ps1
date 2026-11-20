# Fallback only: grabs the primary screen to a PNG (downscaled) for when the game shows
# something the bridge cannot see (first-run dialogs etc.).
param([string]$Out = "$env:TEMP\cs2-shot.png", [int]$Width = 1720)
Add-Type -AssemblyName System.Drawing, System.Windows.Forms
$b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.CopyFromScreen($b.Location, [System.Drawing.Point]::Empty, $b.Size)
$h = [int]($b.Height * $Width / $b.Width)
$small = New-Object System.Drawing.Bitmap $bmp, $Width, $h
$small.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose(); $small.Dispose()
Write-Output $Out
