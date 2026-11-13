# Derive package PNGs from the original PaneSail master illustration.
# Run with PowerShell on Windows after updating panesail-icon.png.
[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$sizes = @(
    @{ Name = 'StoreLogo.png'; Width = 50; Height = 50 },
    @{ Name = 'Square44x44Logo.scale-200.png'; Width = 88; Height = 88 },
    @{ Name = 'Square44x44Logo.targetsize-24_altform-unplated.png'; Width = 24; Height = 24 },
    @{ Name = 'Square150x150Logo.scale-200.png'; Width = 300; Height = 300 },
    @{ Name = 'Wide310x150Logo.scale-200.png'; Width = 620; Height = 300 },
    @{ Name = 'SplashScreen.scale-200.png'; Width = 1240; Height = 600 },
    @{ Name = 'LockScreenLogo.scale-200.png'; Width = 48; Height = 48 }
)

$sourcePath = Join-Path $PSScriptRoot 'panesail-icon.png'
$source = [System.Drawing.Image]::FromFile($sourcePath)
try {
    foreach ($size in $sizes) {
        $bitmap = [System.Drawing.Bitmap]::new($size.Width, $size.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $dimension = [Math]::Min($size.Width, $size.Height)
            $left = [int](($size.Width - $dimension) / 2)
            $top = [int](($size.Height - $dimension) / 2)
            $graphics.DrawImage($source, $left, $top, $dimension, $dimension)
            $bitmap.Save((Join-Path $PSScriptRoot $size.Name), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }

Write-Output 'Generated seven PaneSail package assets from the original master.'
