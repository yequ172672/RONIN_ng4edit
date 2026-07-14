@echo off
setlocal ENABLEDELAYEDEXPANSION

echo ========================================
echo   RONIN Release Build Script
echo ========================================
echo.

set PROJECT_DIR=D:\code\Game\RONIN_ng4edit-main
set RELEASE_DIR=%PROJECT_DIR%\ronin_release
set BUILD_DIR=%PROJECT_DIR%\RONIN\bin\Release\net8.0-windows

echo Step 1: Build RONIN (Release)...
echo.
dotnet build "%PROJECT_DIR%\RONIN\RONIN.csproj" -c Release --nologo
if %ERRORLEVEL% neq 0 (
    echo.
    echo [ERROR] Build failed!
    pause
    exit /b 1
)
echo Build successful!
echo.

echo Step 2: Clean release directory...
if exist "%RELEASE_DIR%" rmdir /s /q "%RELEASE_DIR%"
mkdir "%RELEASE_DIR%"
echo.

echo Step 3: Copy all build outputs...
copy "%BUILD_DIR%\RONIN.exe" "%RELEASE_DIR%\RONIN.exe" >nul

REM Copy all DLLs from build output (excluding system RTL DLLs)
for %%f in ("%BUILD_DIR%\*.dll") do (
    set "skip=0"
    if /i "%%~nxf"=="clr.dll" set skip=1
    if /i "%%~nxf"=="mscordaccore.dll" set skip=1
    if /i "%%~nxf"=="mscordbi.dll" set skip=1
    if /i "%%~nxf"=="mscorrc.dll" set skip=1
    if /i "%%~nxf"=="mscorrc.debug.dll" set skip=1
    if /i "%%~nxf"=="sos.dll" set skip=1
    if /i "%%~nxf"=="sos.netcore.dll" set skip=1
    if !skip! equ 0 copy "%%f" "%RELEASE_DIR%\%%~nxf" >nul
)

copy "%BUILD_DIR%\*.deps.json" "%RELEASE_DIR%\" >nul 2>&1
copy "%BUILD_DIR%\*.runtimeconfig.json" "%RELEASE_DIR%\" >nul 2>&1

echo.
echo Step 4: Verify release...
if exist "%RELEASE_DIR%\RONIN.exe" (echo [OK] RONIN.exe) else (echo [MISS] RONIN.exe)
if exist "%RELEASE_DIR%\YakumoLib.dll" (echo [OK] YakumoLib.dll) else (echo [MISS] YakumoLib.dll)
if exist "%RELEASE_DIR%\HelixToolkit.SharpDX.dll" (echo [OK] HelixToolkit.SharpDX.dll) else (echo [MISS] HelixToolkit.SharpDX.dll)
if exist "%RELEASE_DIR%\DeflateSharp.dll" (echo [OK] DeflateSharp.dll) else (echo [MISS] DeflateSharp.dll)

echo.
echo ========================================
echo   Release ready: %RELEASE_DIR%
echo ========================================
pause
