@echo off
setlocal
set "SOURCE=%~dp0RAWMate.cs"
set "OUTPUT=%~dp0RAWMate.exe"
set "BUILD_OUTPUT=%~dp0RAWMate-App.exe"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if not exist "%CSC%" (
  echo Cannot find the Windows .NET Framework compiler.
  pause
  exit /b 1
)

"%CSC%" /nologo /target:winexe /optimize+ /out:"%BUILD_OUTPUT%" /win32icon:"%~dp0RAWMate.ico" /resource:"%~dp0RAWMateHeader.png",RAWMateHeader.png "%SOURCE%" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll" /reference:"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll" /reference:System.Windows.Forms.dll
if errorlevel 1 (
  echo.
  echo Build failed.
  pause
  exit /b 1
)

move /Y "%BUILD_OUTPUT%" "%OUTPUT%" >nul
if errorlevel 1 (
  echo.
  echo Cannot replace the formal RAWMate.exe.
  pause
  exit /b 1
)
echo.
echo Built: %OUTPUT%
endlocal
