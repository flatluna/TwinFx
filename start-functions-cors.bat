@echo off
echo ?? Starting Azure Functions with CORS enabled...
echo.
echo ?? Available endpoints will be:
echo    - POST   http://localhost:7011/api/foods
echo    - GET    http://localhost:7011/api/foods/{twinId}
echo    - GET    http://localhost:7011/api/foods/{twinId}/{foodId}
echo    - PUT    http://localhost:7011/api/foods/{twinId}/{foodId}
echo    - DELETE http://localhost:7011/api/foods/{twinId}/{foodId}
echo    - GET    http://localhost:7011/api/foods/{twinId}/filter
echo    - GET    http://localhost:7011/api/foods/{twinId}/stats
echo    - GET    http://localhost:7011/api/foods/{twinId}/search
echo.
echo ?? CORS enabled for: http://localhost:5173
echo.

REM Start Azure Functions with explicit CORS settings
func start --cors "http://localhost:5173,http://localhost:3000,*" --port 7011

pause