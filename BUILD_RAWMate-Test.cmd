@echo off
setlocal
set "SOURCE=%~dp0RAWMate.cs"
set "OUTPUT=%~dp0RAWMate-Test.exe"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo Cannot find the Windows .NET Framework compiler.
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:"%OUTPUT%" /win32icon:"%~dp0RAWMate.ico" "%SOURCE%" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" /reference:System.Windows.Forms.dll
if errorlevel 1 (
  echo.
  echo Test build failed.
  pause
  exit /b 1
)
echo.
echo Built test: %OUTPUT%
endlocal

