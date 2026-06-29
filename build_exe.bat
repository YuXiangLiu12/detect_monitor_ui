@echo off
echo ========================================
echo   LeakMonitor Build Script
echo ========================================
echo.

where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [ERROR] dotnet not found. Please install .NET 8.0 SDK
    pause
    exit /b 1
)

echo [1/2] Cleaning old build...
if exist "publish\" rmdir /s /q "publish"
if exist "bin\Release\" rmdir /s /q "bin\Release"

echo [2/2] Publishing self-contained app...
echo This may take a few minutes...

dotnet publish -c Release -r win-x64 --self-contained true -p:DebugType=none -p:DebugSymbols=false -o publish

if %errorlevel% neq 0 (
    echo [ERROR] Build failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo   Build Complete!
echo   Output: publish\LeakMonitor.exe
echo ========================================
echo.
echo Copy the entire "publish" folder to target PC.
echo No .NET runtime required (self-contained).
echo.

pause
