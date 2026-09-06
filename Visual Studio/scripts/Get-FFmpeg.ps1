$ErrorActionPreference = 'Stop'
$destination = Join-Path $PSScriptRoot '..\LocalLink\ffmpeg'
$archive = Join-Path ([System.IO.Path]::GetTempPath()) ('locallink-ffmpeg-' + [Guid]::NewGuid() + '.zip')
$url = 'https://www.gyan.dev/ffmpeg/builds/packages/ffmpeg-9.0.1-essentials_build.zip'
$expectedHash = 'FEC81AE03971D9DD4BE3EBE02E263BD2EC1D789483F931BDBA5F5715E65DA2E9'
try {
    Invoke-WebRequest -Uri $url -OutFile $archive
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw 'FFmpeg download checksum mismatch.'
    }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($archive)
    try {
        foreach ($name in @('ffmpeg.exe', 'LICENSE')) {
            $entry = $zip.Entries | Where-Object Name -EQ $name | Select-Object -First 1
            if (-not $entry) { throw "Missing $name in FFmpeg archive." }
            [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $destination $name), $true)
        }
    } finally { $zip.Dispose() }
    Write-Output "FFmpeg 9.0.1 installed in $destination"
} finally {
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive }
}
