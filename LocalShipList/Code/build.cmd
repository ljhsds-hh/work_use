@echo off
rem LocalShipList 编译脚本(无需安装 VS,使用系统自带 .NET Framework 编译器)
rem 产物输出到 publish\LocalShipList.exe
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe

"%CSC%" /nologo /target:winexe /codepage:65001 ^
  /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Xml.dll ^
  /out:"%~dp0publish\LocalShipList.exe" "%~dp0LocalShipList.cs"

if errorlevel 1 (
  echo 编译失败
  exit /b 1
)
echo 编译完成:publish\LocalShipList.exe
