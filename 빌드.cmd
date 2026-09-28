@echo off
setlocal
set "compiler=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%compiler%" set "compiler=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%compiler%" exit /b 1
"%compiler%" /nologo /target:winexe /r:System.Core.dll /r:System.Web.Extensions.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /out:"%~dp0CodexTaskClockKR.exe" "%~dp0SessionData.cs" "%~dp0SessionStore.cs" "%~dp0Program.cs"
exit /b %errorlevel%
