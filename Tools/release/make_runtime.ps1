# Fabrique le paquet runtime d'AkiBot (ce que le lanceur télécharge à l'installation) à partir du paquet
# runtime d'origine 1.2.1, en remplaçant tout ce qu'AkiBot recompile lui-même :
#   - kyty_gpu.dll (Kyty + Source/kyty-hybrid, avec Streamline et FidelityFX) ;
#   - module hôte akibot_host.dll (AnyPS5 + Source/anyps5) et relais Kyty (make_shims.py) ; les autres
#     bibliothèques PS5 et le relinker restent ceux d'origine (correctifs absents des sources publiées, issue #99) ;
#   - amd_fidelityfx_vk.dll (AMD FidelityFX SDK 1.1.4, licence MIT).
# Aucun script de lancement : AkiBot.exe démarre le jeu lui-même.
# Usage : powershell -NoProfile -ExecutionPolicy Bypass -File Tools\release\make_runtime.ps1 -Version 0.3.0
param(
    [string]$Version = '0.3.0',
    [string]$Upstream = (Join-Path $PSScriptRoot '..\..\..\Release\amont\runtime-amont-1.2.1.zip'),
    [string]$KytyDll = (Join-Path $PSScriptRoot '..\..\..\kyty-gpu\kyty_gpu.dll'),
    [string]$KytyNids = (Join-Path $PSScriptRoot '..\..\..\kyty\src\hybrid\kytyGpuNids.inc'),
    [string]$AnyPS5Build =(Join-Path $PSScriptRoot '..\..\..\anyps5-build'),
    [string]$WinLibs = (Join-Path $PSScriptRoot '..\..\..\outils\winlibs\mingw64\bin'),
    [string]$Ffx = (Join-Path $PSScriptRoot '..\..\..\sdk\fidelityfx-1.1.4'),
    # Liste de shaders 01.007 (décalages et hash seulement, aucun code du jeu) : préparation des shaders avant de jouer.
    [string]$PipelineList = (Join-Path $PSScriptRoot '..\..\..\Mesures\liste-0107\PPSA21567.kyplst'),
    [string]$PipelineListTool = (Join-Path $PSScriptRoot '..\..\..\kyty-build-int22\kyty_pipeline_list.exe'),
    # Le jeu converti (app0 d'une installation) : « verify » cherche le code du jeu dans la liste pour prouver qu'il n'y est pas.
    [string]$GameForVerify = 'C:\AkiBot\Build\RunHybrid\app0',
    [string]$Out = (Join-Path $PSScriptRoot '..\..\..\Release')
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$name = "AkiBot-runtime-$Version"
$stage = [IO.Path]::GetFullPath((Join-Path $Out $name))
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

# 1. Le paquet d'origine, sans son lanceur, ses scripts ni ses fichiers portant l'ancien nom.
$zip = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Upstream))
try {
    foreach ($e in $zip.Entries) {
        $path = $e.FullName -replace '/', '\'
        $leaf = Split-Path $path -Leaf
        $top = -not $path.Contains('\')
        if (-not $e.Name) { continue }  # dossier
        if ($leaf -match '\.(bat|cmd|sh)$') { continue }
        if ($top -and ($leaf -match '\.(exe|config|dll)$' -or $leaf -eq 'README.txt')) { continue }
        if ($path -match '^Build\\RunHybrid\\(kyty_gpu\.dll|[^\\]*_host\.dll)$') { continue }
        # Bibliothèques et relinker : ceux d'origine (les binaires contiennent des correctifs absents des sources
        # publiées, issue #99), sauf l'ancien module hôte et les trois relais, refaits plus bas.
        if ($path -match '^Build\\RunHybrid\\libs\\(lib[^\\]*host\.prx|libSceAgc\.prx|libSceAgcDriver\.prx|libSceVideoOut\.prx)$') { continue }
        $dest = Join-Path $stage $path
        New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
        [IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true)
    }
} finally { $zip.Dispose() }

# 2. Ce qu'AkiBot compile.
$run = Join-Path $stage 'Build\RunHybrid'
$libs = Join-Path $run 'libs'
New-Item -ItemType Directory -Force $libs, (Join-Path $stage 'tools') | Out-Null
Copy-Item $KytyDll $run
$built = Join-Path $AnyPS5Build 'core\libs\libs'
Copy-Item (Join-Path $built 'libAkiBotHost.prx') $libs
Copy-Item (Join-Path $built 'libAkiBotHost.prx') (Join-Path $run 'akibot_host.dll')
# libc.prx d'origine lit la variable de la fenêtre CUDA sous l'ancien nom : même texte au nom AkiBot, complété par des zéros.
$libc = Join-Path $libs 'libc.prx'
$bytes = [IO.File]::ReadAllBytes($libc)
$from = [Text.Encoding]::ASCII.GetBytes(('ASTRO' + 'HYBRID') + '_CUDA_HOLE')
$to = [Text.Encoding]::ASCII.GetBytes('AKIBOT_CUDA_HOLE')
$hits = @()
for ($i = 0; $i -le $bytes.Length - $from.Length - 1; $i++) {
    if ($bytes[$i] -ne $from[0]) { continue }
    $ok = $true
    for ($j = 1; $j -lt $from.Length; $j++) { if ($bytes[$i + $j] -ne $from[$j]) { $ok = $false; break } }
    if ($ok -and $bytes[$i + $from.Length] -eq 0) { $hits += $i }
}
if ($hits.Count -ne 1) { throw "libc.prx : $($hits.Count) occurrence(s) de la variable CUDA au lieu d'une" }
for ($j = 0; $j -lt $from.Length; $j++) { $bytes[$hits[0] + $j] = if ($j -lt $to.Length) { $to[$j] } else { 0 } }
[IO.File]::WriteAllBytes($libc, $bytes)
# libSceAgc, libSceAgcDriver et libSceVideoOut : relais vers kyty_gpu.dll (remplacent ceux d'AnyPS5).
python -I (Join-Path $PSScriptRoot '..\hybrid\make_shims.py') $KytyNids $libs
if ($LASTEXITCODE -ne 0) { throw 'make_shims.py a échoué' }
Copy-Item (Join-Path $Ffx 'PrebuiltSignedDLL\amd_fidelityfx_vk.dll') $run
Copy-Item (Join-Path $Ffx 'sdk\LICENSE.txt') (Join-Path $stage 'licenses\fidelityfx.license.txt')
if ($PipelineList -and (Test-Path $PipelineList)) {
    # « verify » refuse une liste qui contiendrait du code du jeu.
    & $PipelineListTool verify --game $GameForVerify $PipelineList
    if ($LASTEXITCODE -ne 0) { throw "la liste de shaders contient du code du jeu ou est invalide : $PipelineList" }
    $lists = Join-Path $run 'PipelineLists'
    New-Item -ItemType Directory -Force $lists | Out-Null
    Copy-Item $PipelineList (Join-Path $lists 'PPSA21567.kyplst')
}

@"
AkiBot runtime $Version
=======================
Fichiers que le lanceur AkiBot installe à côté du jeu : moteur graphique (kyty_gpu.dll), bibliothèques
système (Build\RunHybrid\libs), module hôte (akibot_host.dll), convertisseur (tools\relinker.exe),
fichiers NVIDIA (DLSS, Frame Generation, Reflex) et AMD (FSR 3), polices Noto. Aucun fichier du jeu.
Lancer le jeu : AkiBot.exe (aucun script). Licences : dossier licenses.
"@ | Set-Content -Encoding UTF8 (Join-Path $stage 'README.txt')

# 3. Contrôles : ni script, ni ancien nom, ni fichier de jeu.
$bad = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Extension -in '.bat', '.cmd', '.sh' }
if ($bad) { $bad.FullName; throw 'scripts trouvés dans le paquet' }
$old = 'astro' + 'hybrid'
$named = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -match $old }
if ($named) { $named.FullName; throw 'ancien nom dans un nom de fichier' }
foreach ($f in Get-ChildItem $stage -Recurse -File) {
    if ([Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($f.FullName)) -match "(?i)$old") { throw "ancien nom dans $($f.FullName)" }
}
$game = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Name -match '^AstroBot.*\.exe$|\.elf$|eboot|param\.(json|sfo)$' }
if ($game) { $game.FullName; throw 'fichiers du jeu trouvés' }

$zipOut = Join-Path ([IO.Path]::GetFullPath($Out)) "$name.zip"
if (Test-Path $zipOut) { Remove-Item $zipOut }
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $zipOut, [IO.Compression.CompressionLevel]::Optimal, $false)
$hash = (Get-FileHash $zipOut -Algorithm SHA256).Hash.ToLowerInvariant()
"{0} : {1:N0} fichiers, {2:N0} octets, SHA-256 {3}" -f $zipOut, (Get-ChildItem $stage -Recurse -File).Count, (Get-Item $zipOut).Length, $hash
