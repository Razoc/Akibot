@echo off
rem Compilation PGO de kyty_emulator puis liaison de kyty_gpu.dll (version Akibot, sans chemins prives).
rem Usage : build-kyty-pgo.cmd [configure|build|dll|all]   (defaut : all)
rem Variables modifiables avant l'appel :
rem   KYTY_SRC  sources Kyty patchees      (defaut : <depot>\..\kyty)
rem   KYTY_OUT  dossier de build            (defaut : <depot>\..\kyty-build)
rem   KYTY_DLL  dossier de sortie de la DLL (defaut : <depot>\..\kyty-gpu)
rem   LLVM_DIR  dossier bin de LLVM 22.1.3  (defaut : C:\Program Files\LLVM\bin)
rem   KYTY_STREAMLINE_DIR  SDK Streamline 2.14.16 (optionnel : DLSS + Frame Generation)
rem   KYTY_JOBS nombre de taches en parallele (defaut : nombre de coeurs logiques)
setlocal
for %%I in ("%~dp0..\..") do set "REPO=%%~fI"
if not defined KYTY_SRC set "KYTY_SRC=%REPO%\..\kyty"
if not defined KYTY_OUT set "KYTY_OUT=%REPO%\..\kyty-build"
if not defined KYTY_DLL set "KYTY_DLL=%REPO%\..\kyty-gpu"
if not defined LLVM_DIR set "LLVM_DIR=C:\Program Files\LLVM\bin"
if not defined VULKAN_SDK set "VULKAN_SDK=C:\VulkanSDK\1.4.363.0"
if not defined KYTY_JOBS set "KYTY_JOBS=%NUMBER_OF_PROCESSORS%"
for %%I in ("%KYTY_SRC%") do set "KYTY_SRC=%%~fI"
for %%I in ("%KYTY_OUT%") do set "KYTY_OUT=%%~fI"
for %%I in ("%KYTY_DLL%") do set "KYTY_DLL=%%~fI"
set "LLVM=%LLVM_DIR:\=/%"
set "PROFILE=%KYTY_SRC:\=/%/tools/pgo/u59-int21-1.profdata"

if not exist "%KYTY_SRC%\CMakeLists.txt" (echo Sources Kyty introuvables : %KYTY_SRC% & exit /b 1)
if not exist "%LLVM_DIR%\clang-cl.exe" (echo clang-cl introuvable dans %LLVM_DIR% & exit /b 2)
if not exist "%KYTY_SRC%\tools\pgo\u59-int21-1.profdata" (echo Profil PGO introuvable : %PROFILE% & exit /b 3)

set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
for /f "usebackq delims=" %%I in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set "VSDIR=%%I"
if not defined VSDIR (echo Visual Studio 2022 Build Tools introuvable & exit /b 4)
call "%VSDIR%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 10
set "PATH=%LLVM_DIR%;%VULKAN_SDK%\Bin;%PATH%"

set "SL_ARGS="
if defined KYTY_STREAMLINE_DIR set "SL_ARGS=-DKYTY_STREAMLINE_DIR=%KYTY_STREAMLINE_DIR:\=/% -DKYTY_NGX_SDK_DIR=%KYTY_STREAMLINE_DIR:\=/%/external/ngx-sdk"

set "STEP=%~1"
if "%STEP%"=="" set "STEP=all"
if "%STEP%"=="build" goto build
if "%STEP%"=="dll" goto dll

echo === Configuration (%KYTY_OUT%)
cmake -S "%KYTY_SRC%" -B "%KYTY_OUT%" -G Ninja ^
  -DCMAKE_BUILD_TYPE=Release ^
  -DCMAKE_C_COMPILER="%LLVM%/clang-cl.exe" -DCMAKE_CXX_COMPILER="%LLVM%/clang-cl.exe" ^
  -DCMAKE_LINKER="%LLVM%/lld-link.exe" -DCMAKE_AR="%LLVM%/llvm-lib.exe" ^
  -DKYTY_BUILD_LAUNCHER=OFF -DKYTY_EMULATOR_IPO=ON -DKYTY_CP_DIAGNOSTICS=OFF ^
  -DKYTY_PGO_USE="%PROFILE%" ^
  -DKYTY_BUILD_ORIGIN=Source -DKYTY_MARCH= %SL_ARGS% || exit /b 11
if "%STEP%"=="configure" exit /b 0

:build
echo === Compilation de kyty_emulator (%KYTY_JOBS% taches)
cmake --build "%KYTY_OUT%" --target kyty_emulator --parallel %KYTY_JOBS% || exit /b 12
if "%STEP%"=="build" exit /b 0

:dll
echo === Liaison de kyty_gpu.dll (%KYTY_DLL%)
python -I "%KYTY_SRC%\tools\hybrid\build_kyty_gpu.py" "%KYTY_OUT%" "%KYTY_DLL%" || exit /b 13
echo OK : %KYTY_DLL%\kyty_gpu.dll
exit /b 0
