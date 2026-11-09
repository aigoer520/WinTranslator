@echo off
rem WinTranslator 命令行构建（.NET Framework 自带 csc，无需打开 Visual Studio）
setlocal
set CSC=
if exist "%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if "%CSC%"=="" if exist "%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if "%CSC%"=="" (echo 未找到 .NET Framework 4.x 的 csc.exe，请先安装 .NET Framework 4.8 开发者包。& exit /b 1)
"%CSC%" /nologo /target:winexe /platform:anycpu /out:WinTranslator.exe ^
  /r:System.Web.Extensions.dll /r:System.Net.Http.dll /r:System.Security.dll ^
  src\*.cs
if errorlevel 1 (echo 构建失败。& exit /b 1)
echo 构建完成：WinTranslator.exe
echo 提醒：运行还需 exe 同目录有 tesseract\ 文件夹（见 README「运行依赖」一节）。
endlocal
