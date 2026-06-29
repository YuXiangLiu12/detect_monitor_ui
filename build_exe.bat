@echo off
chcp 65001 >nul
echo ========================================
echo   核电站泄漏监测系统 - 打包发布
echo ========================================
echo.

REM 检查 dotnet 是否可用
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo [错误] 找不到 dotnet 命令，请安装 .NET 8.0 SDK
    pause
    exit /b 1
)

echo [1/2] 清理旧的构建产物...
if exist "publish\" rmdir /s /q "publish"
if exist "bin\Release\" rmdir /s /q "bin\Release"

echo [2/2] 发布为独立可执行文件（自包含部署）...
echo   正在编译打包，请耐心等待...

dotnet publish -c Release -o publish

if %errorlevel% neq 0 (
    echo.
    echo [错误] 打包失败！
    pause
    exit /b 1
)

echo.
echo ========================================
echo   打包完成！
echo   输出文件: publish\LeakMonitor.exe
echo ========================================
echo.
echo 注意事项:
echo   1. LeakMonitor.exe 是自包含单文件程序，无需安装 .NET 运行时
echo   2. 配置文件 config.json 需与 exe 放在同一目录
echo   3. 首次运行可能需要 Windows 防火墙授权（串口访问）
echo.
pause
