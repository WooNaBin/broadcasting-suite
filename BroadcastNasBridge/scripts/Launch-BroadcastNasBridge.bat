@echo off
setlocal
cd /d "%~dp0.."
dotnet run --project BroadcastNasBridge.csproj --configuration Release -- %*
