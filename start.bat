@echo off
rem Single-process mode on Windows: API with in-process workers (SQLite, in-memory bus) + Vite dev server.
setlocal
cd /d "%~dp0"

where dotnet >nul 2>nul || (
  echo .NET 10 SDK not found. Install it with:  winget install Microsoft.DotNet.SDK.10
  pause & exit /b 1
)
where npm >nul 2>nul || (
  echo Node.js not found. Install it with:  winget install OpenJS.NodeJS.LTS
  pause & exit /b 1
)

if not exist web\node_modules (
  echo Installing frontend packages...
  pushd web & call npm install & popd
)

start "rent-radar api" /d "%~dp0src\RentRadar.Api" cmd /k dotnet run
start "rent-radar web" /d "%~dp0web" cmd /k npm run dev
echo Waiting for the services to start...
timeout /t 12 /nobreak >nul
start "" http://localhost:5173
