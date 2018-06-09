@echo off

echo Registering RTD Server
%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\RegAsm.exe RabbitRtd\bin\Release\RabbitRTD.dll /codebase

if errorlevel 1 pause
