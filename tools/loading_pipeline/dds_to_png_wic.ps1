# DDS → PNG 批量转换（Windows WIC 系统解码器，零依赖，与引擎读取同源）
# 用途：太阁5 DX (G1T 导出的 .dds，布局非标准) 转标准 PNG 供打包管线使用
# 用法: powershell -ExecutionPolicy Bypass -File dds_to_png_wic.ps1 -SrcDir "E:\...\image_event" -OutDir "..." [-Pattern "*.dds"]
param(
    [string]$SrcDir,
    [string]$OutDir,
    [string]$Pattern = "*.dds"
)
Add-Type -AssemblyName PresentationCore

if (-not $SrcDir) { throw "SrcDir required" }
if (-not $OutDir) { $OutDir = Join-Path $SrcDir "png" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$files = Get-ChildItem -Path $SrcDir -Filter $Pattern -File
$ok = 0; $fail = 0
foreach ($f in $files) {
    $outPath = Join-Path $OutDir ([System.IO.Path]::GetFileNameWithoutExtension($f.Name) + ".png")
    try {
        $stream = [System.IO.File]::OpenRead($f.FullName)
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream, [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames[0]
        $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
        $enc.Frames.Add($frame)
        $fs = [System.IO.File]::Create($outPath)
        $enc.Save($fs)
        $fs.Close(); $stream.Close()
        Write-Output ("OK   {0}  {1}x{2}" -f $f.Name, $frame.PixelWidth, $frame.PixelHeight)
        $ok++
    } catch {
        Write-Output ("FAIL {0}  {1}" -f $f.Name, $_.Exception.Message)
        $fail++
    }
}
Write-Output ("done: {0} ok, {1} fail -> {2}" -f $ok, $fail, $OutDir)
