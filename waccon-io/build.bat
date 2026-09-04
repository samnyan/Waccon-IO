@echo off
setlocal EnableExtensions EnableDelayedExpansion

set "SCRIPT_DIR=%~dp0"
set "BUILD_DIR=%SCRIPT_DIR%build-ninja"
set "PROJECT_BUILD_DIR=%SCRIPT_DIR%build-vs"
set "MESON_BACKEND=ninja"
set "BUILD_TYPE=release"
set "VS_INSTALLATION=D:\Program Files\Microsoft Visual Studio\18\Community"

if /I "%~1"=="" goto :usage
if /I "%~1"=="build" goto :build
if /I "%~1"=="test" goto :test
if /I "%~1"=="clean" goto :clean
if /I "%~1"=="project" goto :project

goto :usage

:build
call :detect_vs
if errorlevel 1 exit /b 1
call :detect_meson
if errorlevel 1 exit /b 1
call "%VSVARSALL%" x64
if errorlevel 1 (
    echo Failed to initialize the Visual C++ x64 environment.
    exit /b 1
)

if exist "%BUILD_DIR%" (
    "%MESON%" setup "%BUILD_DIR%" --backend "%MESON_BACKEND%" --buildtype "%BUILD_TYPE%" --reconfigure
) else (
    "%MESON%" setup "%BUILD_DIR%" --backend "%MESON_BACKEND%" --buildtype "%BUILD_TYPE%"
)
if errorlevel 1 exit /b 1

"%MESON%" compile -C "%BUILD_DIR%"
if errorlevel 1 exit /b 1

echo.
echo Build complete.
echo DLL: "%BUILD_DIR%\waccon_io.dll"
exit /b 0

:test
call :build
if errorlevel 1 exit /b 1
"%MESON%" test -C "%BUILD_DIR%" --print-errorlogs
exit /b %errorlevel%

:project
call :detect_vs
if errorlevel 1 exit /b 1
call :detect_meson
if errorlevel 1 exit /b 1
call "%VSVARSALL%" x64
if errorlevel 1 exit /b 1
if exist "%PROJECT_BUILD_DIR%" (
    "%MESON%" setup "%PROJECT_BUILD_DIR%" --backend vs2022 --buildtype "%BUILD_TYPE%" --reconfigure
) else (
    "%MESON%" setup "%PROJECT_BUILD_DIR%" --backend vs2022 --buildtype "%BUILD_TYPE%"
)
exit /b %errorlevel%

:clean
if exist "%BUILD_DIR%" (
    rmdir /s /q "%BUILD_DIR%"
    echo Removed "%BUILD_DIR%"
) else (
    echo Build directory does not exist.
)
exit /b 0

:detect_vs
set "VSVARSALL="
set "VSWHERE=%ProgramFiles%\Microsoft Visual Studio\Installer\vswhere.exe"
if not exist "%VSWHERE%" set "VSWHERE=%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe"
if exist "%VSWHERE%" (
    for /f "usebackq tokens=*" %%i in (`"%VSWHERE%" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find VC\Auxiliary\Build\vcvarsall.bat`) do if not defined VSVARSALL set "VSVARSALL=%%i"
)
if not defined VSVARSALL if exist "%VS_INSTALLATION%\VC\Auxiliary\Build\vcvarsall.bat" set "VSVARSALL=%VS_INSTALLATION%\VC\Auxiliary\Build\vcvarsall.bat"
if not defined VSVARSALL (
    echo Could not find vcvarsall.bat.
    echo Install the Visual C++ desktop workload or edit VS_INSTALLATION in this file.
    exit /b 1
)
echo Using Visual Studio environment: "%VSVARSALL%"
exit /b 0

:detect_meson
set "MESON="
for /f "usebackq tokens=*" %%i in (`where meson 2^>nul`) do if not defined MESON set "MESON=%%i"
if not defined MESON (
    echo Meson was not found in PATH.
    echo Install it with: python -m pip install meson ninja
    exit /b 1
)
echo Using Meson: "%MESON%"
exit /b 0

:usage
echo Usage: %~nx0 ^<action^>
echo.
echo   build    Configure and build the release DLL with MSVC/Ninja
 echo   test     Build and run the Meson test suite
 echo   project  Generate a Visual Studio 2022 solution without building
 echo   clean    Remove the generated build directory
exit /b 2
