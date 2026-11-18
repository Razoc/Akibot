# Compilation PGO de kyty_emulator puis liaison de kyty_gpu.dll (version AkiBot, sans chemins privés).
# Usage : powershell -NoProfile -ExecutionPolicy Bypass -File Tools\hybrid\build-kyty-pgo.ps1 [configure|build|dll|all]
# Paramètres (ou variables d'environnement du même nom) :
#   -KytySrc   sources Kyty patchées      (défaut : <dépôt>\..\kyty)
#   -KytyOut   dossier de build            (défaut : <dépôt>\..\kyty-build)
#   -KytyDll   dossier de sortie de la DLL (défaut : <dépôt>\..\kyty-gpu)
#   -LlvmDir   dossier bin de LLVM 22.1.3  (défaut : C:\Program Files\LLVM\bin)
#   -StreamlineDir  SDK Streamline 2.14.16 (optionnel : DLSS + Frame Generation)
#   -FfxDir    SDK AMD FidelityFX 1.1.4 (optionnel : FSR 3.1)
#   -Jobs      tâches en parallèle (défaut : nombre de cœurs logiques)
#   -Diag      compteurs internes du command processor (version de diagnostic, plus lente)
param(
    [ValidateSet('all', 'configure', 'build', 'dll')] [string]$Step = 'all',
    [string]$KytySrc = $env:KYTY_SRC,
    [string]$KytyOut = $env:KYTY_OUT,
    [string]$KytyDll = $env:KYTY_DLL,
    [string]$LlvmDir = $env:LLVM_DIR,
    [string]$StreamlineDir = $env:KYTY_STREAMLINE_DIR,
    [string]$FfxDir = $env:KYTY_FFX_DIR,
    [int]$Jobs = 0,
    [switch]$Diag
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $KytySrc) { $KytySrc = Join-Path $repo '..\kyty' }
if (-not $KytyOut) { $KytyOut = Join-Path $repo '..\kyty-build' }
if (-not $KytyDll) { $KytyDll = Join-Path $repo '..\kyty-gpu' }
if (-not $LlvmDir) { $LlvmDir = 'C:\Program Files\LLVM\bin' }
if (-not $env:VULKAN_SDK) { $env:VULKAN_SDK = 'C:\VulkanSDK\1.4.363.0' }
if ($Jobs -le 0) { $Jobs = if ($env:KYTY_JOBS) { [int]$env:KYTY_JOBS } else { [Environment]::ProcessorCount } }
if ($env:KYTY_DIAG -eq '1') { $Diag = $true }
$KytySrc = [IO.Path]::GetFullPath($KytySrc)
$KytyOut = [IO.Path]::GetFullPath($KytyOut)
$KytyDll = [IO.Path]::GetFullPath($KytyDll)
$llvm = $LlvmDir -replace '\\', '/'
$pgoProfile = (Join-Path $KytySrc 'tools\pgo\u59-int21-1.profdata') -replace '\\', '/'

if (-not (Test-Path (Join-Path $KytySrc 'CMakeLists.txt'))) { throw "Sources Kyty introuvables : $KytySrc" }
if (-not (Test-Path (Join-Path $LlvmDir 'clang-cl.exe'))) { throw "clang-cl introuvable dans $LlvmDir" }
if (-not (Test-Path $pgoProfile)) { throw "Profil PGO introuvable : $pgoProfile" }

# Environnement Visual Studio 2022 x64 (module DevShell de Visual Studio, sans script batch).
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsDir = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vsDir) { throw 'Visual Studio 2022 Build Tools introuvable' }
Import-Module (Join-Path $vsDir 'Common7\Tools\Microsoft.VisualStudio.DevShell.dll')
Enter-VsDevShell -VsInstallPath $vsDir -SkipAutomaticLocation -DevCmdArguments '-arch=x64 -host_arch=x64' | Out-Null
$env:PATH = "$LlvmDir;$env:VULKAN_SDK\Bin;$env:PATH"

function Invoke-Step([string]$what, [scriptblock]$cmd) {
    Write-Host "=== $what"
    & $cmd
    if ($LASTEXITCODE -ne 0) { throw "$what : échec (code $LASTEXITCODE)" }
}

if ($Step -in 'all', 'configure') {
    $cmakeArgs = @('-S', $KytySrc, '-B', $KytyOut, '-G', 'Ninja',
        '-DCMAKE_BUILD_TYPE=Release',
        "-DCMAKE_C_COMPILER=$llvm/clang-cl.exe", "-DCMAKE_CXX_COMPILER=$llvm/clang-cl.exe",
        "-DCMAKE_LINKER=$llvm/lld-link.exe", "-DCMAKE_AR=$llvm/llvm-lib.exe",
        '-DKYTY_BUILD_LAUNCHER=OFF', '-DKYTY_EMULATOR_IPO=ON', "-DKYTY_CP_DIAGNOSTICS=$(if ($Diag) { 'ON' } else { 'OFF' })",
        "-DKYTY_PGO_USE=$pgoProfile",
        '-DKYTY_BUILD_ORIGIN=Source', '-DKYTY_MARCH=')
    if ($StreamlineDir) {
        $sl = $StreamlineDir -replace '\\', '/'
        $cmakeArgs += "-DKYTY_STREAMLINE_DIR=$sl", "-DKYTY_NGX_SDK_DIR=$sl/external/ngx-sdk"
    }
    if ($FfxDir) { $cmakeArgs += "-DKYTY_FFX_SDK_DIR=$($FfxDir -replace '\\', '/')" }
    Invoke-Step "Configuration ($KytyOut)" { cmake @cmakeArgs }
}
if ($Step -in 'all', 'build') {
    Invoke-Step "Compilation de kyty_emulator ($Jobs tâches)" { cmake --build $KytyOut --target kyty_emulator --parallel $Jobs }
}
if ($Step -in 'all', 'dll') {
    Invoke-Step "Liaison de kyty_gpu.dll ($KytyDll)" { python -I (Join-Path $KytySrc 'tools\hybrid\build_kyty_gpu.py') $KytyOut $KytyDll }
    Write-Host "OK : $KytyDll\kyty_gpu.dll"
}
