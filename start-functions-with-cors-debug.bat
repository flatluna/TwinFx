@echo off
echo ?? Starting Azure Functions with Enhanced CORS Debugging...
echo.

echo ?? CORS Configuration Summary:
echo - Middleware: CorsMiddleware (Enhanced)
echo - Allowed Origins: http://localhost:5173, http://localhost:3000, *
echo - Host.json CORS: Configured
echo - Local.settings CORS: Configured
echo.

echo ?? Killing any existing func processes...
taskkill /F /IM func.exe >nul 2>&1
taskkill /F /IM dotnet.exe >nul 2>&1

echo.
echo ?? Cleaning up previous builds...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

echo.
echo ??? Building the project...
dotnet build

echo.
echo ?? Starting Functions Host with CORS debugging...
echo ?? Available endpoints will be:
echo   GET  http://localhost:7011/api/foods/{twinId}
echo   POST http://localhost:7011/api/foods
echo   PUT  http://localhost:7011/api/foods/{foodId}?twinID={twinId}
echo   OPTIONS for all endpoints
echo.
echo ?? Test CORS using: Functions/CORS_Diagnostic_Tool.html
echo.
echo Press Ctrl+C to stop the Functions host
echo.

func start --cors "http://localhost:5173,http://localhost:3000,*" --port 7011 --verbose

echo.
echo ?? Functions host stopped
pause