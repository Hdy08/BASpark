param(
    [string]$SourcePath,
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$workspace = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($SourcePath)) {
    $SourcePath = Join-Path $workspace "src\Web\index.html"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $workspace "preview\index.html"
}

$sourceFullPath = [System.IO.Path]::GetFullPath($SourcePath)
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
$marker = "<!-- BASPARK_ASSET_BOOTSTRAP -->"
$html = [System.IO.File]::ReadAllText($sourceFullPath, [System.Text.Encoding]::UTF8)
if (($html.Split($marker).Length - 1) -ne 1) {
    throw "Expected exactly one touch-effect asset bootstrap marker."
}

$assetDirectory = Join-Path $workspace "src\Web\Assets"
$assetFiles = [ordered]@{
    circle = "FX_TEX_Circle_01.png"
    ring = "FX_TEX_Grad_Ring3.png"
    trail = "FX_TEX_Trail_03.png"
    triangle = "FX_TEX_Triangle_02_1.png"
}
$assets = [ordered]@{}
foreach ($entry in $assetFiles.GetEnumerator()) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $assetDirectory $entry.Value))
    $assets[$entry.Key] = "data:image/png;base64,$([Convert]::ToBase64String($bytes))"
}

$assetJson = ConvertTo-Json $assets -Compress
$bootstrap = "<script>window.__BASPARK_STANDALONE_PREVIEW__=true;window.__BASPARK_ASSETS=$assetJson;</script>"
$standalone = $html.Replace($marker, $bootstrap)
$outputDirectory = Split-Path -Parent $outputFullPath
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
[System.IO.File]::WriteAllText(
    $outputFullPath,
    $standalone,
    [System.Text.UTF8Encoding]::new($false)
)

Write-Output $outputFullPath
