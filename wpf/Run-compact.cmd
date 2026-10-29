@echo off
rem Resolve a per-user .NET Desktop Runtime without modifying system settings.
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "DOTNET_ROOT=%LOCALAPPDATA%\Microsoft\dotnet"
start "" "%~dp0MacDock.Wpf.exe" %*
