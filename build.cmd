@echo off

echo download nuget from https://www.nuget.org/downloads
echo Restoring NuGet packages

nuget restore rabbit-rtd.sln

@if ERRORLEVEL 1 PAUSE

echo Building release
%SystemRoot%\Microsoft.NET\Framework\v4.0.30319\MSBuild.exe /p:Configuration=Release /v:minimal  .\rabbit-rtd.sln

@if ERRORLEVEL 1 PAUSE
