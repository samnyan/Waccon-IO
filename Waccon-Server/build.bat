@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PROJECT=%SCRIPT_DIR%Waccon.Server.csproj"
if /I "%~1"=="publish" goto publish
if /I "%~1"=="test" goto test
:build
dotnet build "%PROJECT%" -c Release
exit /b %errorlevel%
:publish
dotnet publish "%PROJECT%" -c Release -r win-x64 --self-contained true /p:PublishAot=true /p:StripSymbols=true
exit /b %errorlevel%
:test
call "%~f0" build
if errorlevel 1 exit /b 1
dotnet test "%PROJECT%" -c Release
exit /b %errorlevel%
