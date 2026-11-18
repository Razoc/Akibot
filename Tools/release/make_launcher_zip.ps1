# Fabrique le zip de release pour RecompLauncher : AkiBot.exe et recomplauncher.json, à la racine du zip.
# Le nom dit le système et le processeur, comme RecompLauncher le demande : AkiBot-v<version>-Windows-x64.zip.
# AkiBot.exe reste aussi publié seul (la mise à jour automatique d'AkiBot cherche cet asset).
# Usage : powershell -NoProfile -ExecutionPolicy Bypass -File Tools\release\make_launcher_zip.ps1 -Version 0.3.0
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$Out = (Join-Path $PSScriptRoot '..\..\..\Release')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$exe = Join-Path $root 'AkiBot.App\bin\Release\AkiBot.exe'
$manifest = Join-Path $root 'recomplauncher.json'

# Garde-fous : l'exe est bien celui de cette version, et recomplauncher.json suit le runtime épinglé
# (prepare.version = version du runtime, pour que RecompLauncher refasse la préparation quand il change).
$exeVersion = (Get-Item $exe).VersionInfo.FileVersion
if (-not $exeVersion.StartsWith($Version)) { throw "AkiBot.exe est en $exeVersion, pas en $Version (recompiler en Release)" }
$info = Get-Content (Join-Path $root 'AkiBot.App\Services\AkiBotInfo.cs') -Raw
$runtime = [regex]::Match($info, 'RuntimeVersion = "([^"]+)"').Groups[1].Value
$json = Get-Content $manifest -Raw -Encoding UTF8 | ConvertFrom-Json
if ($json.prepare.version -ne $runtime) { throw "recomplauncher.json : prepare.version = $($json.prepare.version), runtime épinglé = $runtime" }

New-Item -ItemType Directory -Force $Out | Out-Null
$zip = Join-Path (Resolve-Path $Out) "AkiBot-v$Version-Windows-x64.zip"
if (Test-Path $zip) { Remove-Item $zip }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($f in $exe, $manifest) {
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f, (Split-Path $f -Leaf), 'Optimal')
    }
}
finally { $archive.Dispose() }
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
"$zip`n$((Get-Item $zip).Length) octets, SHA-256 $hash"
