@echo off
setlocal enabledelayedexpansion
title PSMPE Portal - Dev Launcher
set ROOT=%~dp0

echo ============================================
echo  PSMPE Portal - freeing dev ports
echo ============================================
call :killport 5000
REM Vite silently increments past a busy port instead of failing, so orphaned dev
REM servers from earlier runs pile up on 5174, 5175, etc. Clear a range, not just 5173.
for /L %%p in (5173,1,5182) do call :killport %%p

REM taskkill /F returns before Windows fully releases the killed process's socket,
REM so give the OS a moment before anything tries to rebind these ports.
timeout /t 2 /nobreak >nul

echo.
echo ============================================
echo  Loading .env
echo ============================================
if not exist "%ROOT%.env" (
    echo No .env found - creating one from .env.example with default local dev values.
    copy "%ROOT%.env.example" "%ROOT%.env" >nul
)
for /f "usebackq eol=# tokens=1,* delims==" %%A in ("%ROOT%.env") do (
    if not "%%A"=="" set "%%A=%%B"
)

REM The backend runs natively here (not inside the Docker network), so it must
REM reach Postgres via localhost, not the "postgres" hostname used by docker-compose.
REM Rebuilding the connection string from the .env pieces guarantees the password
REM always matches whatever the postgres container was actually initialized with.
if not defined POSTGRES_PORT set POSTGRES_PORT=5432
set "ConnectionStrings__DefaultConnection=Host=localhost;Port=%POSTGRES_PORT%;Database=%POSTGRES_DB%;Username=%POSTGRES_USER%;Password=%POSTGRES_PASSWORD%"

echo.
echo ============================================
echo  Starting PostgreSQL (Docker)
echo ============================================
set DOCKER_OK=0
set PG_STARTED=0
where docker >nul 2>&1
if errorlevel 1 (
    echo Docker not found on PATH - skipping. Make sure Postgres is reachable
    echo via the connection string in your .env / user-secrets.
) else (
    set DOCKER_OK=1
)
if "%DOCKER_OK%"=="1" (
    docker compose -f "%ROOT%docker-compose.yml" up -d postgres
    if errorlevel 1 (
        echo WARNING: failed to start the postgres container - continuing anyway.
    ) else (
        set PG_STARTED=1
    )
)
if "%PG_STARTED%"=="1" call :waitforpg

echo.
echo ============================================
echo  Building backend
echo ============================================
dotnet build "%ROOT%src\PSMPE.Portal.sln" -c Debug
if errorlevel 1 (
    echo Backend build FAILED. Aborting.
    exit /b 1
)

echo.
echo ============================================
echo  Building frontend
echo ============================================
pushd "%ROOT%apps\web"
call npm install
if errorlevel 1 (
    echo npm install FAILED. Aborting.
    popd
    exit /b 1
)
call npm run build
if errorlevel 1 (
    echo Frontend build FAILED. Aborting.
    popd
    exit /b 1
)
popd

echo.
echo ============================================
echo  Builds OK - launching dev servers
echo ============================================

start "PSMPE Backend (:5000)" cmd /k "cd /d "%ROOT%src\PSMPE.Portal.WebAPI" && dotnet run --urls http://localhost:5000"
start "PSMPE Frontend (:5173)" cmd /k "cd /d "%ROOT%apps\web" && npm run dev"

echo.
echo Backend:  http://localhost:5000/swagger
echo Frontend: http://localhost:5173
echo Each service is running in its own window - close a window to stop it.
goto :eof

:killport
set PORT=%1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":%PORT% " ^| findstr "LISTENING"') do (
    echo Killing process on port %PORT% (PID %%a)
    taskkill /F /PID %%a >nul 2>&1
)
goto :eof

:waitforpg
echo Waiting for postgres to accept connections...
set _tries=0
:waitforpg_loop
docker compose -f "%ROOT%docker-compose.yml" exec -T postgres pg_isready -U "%POSTGRES_USER%" -d "%POSTGRES_DB%" >nul 2>&1
if not errorlevel 1 goto :eof
set /a _tries+=1
if %_tries% GEQ 30 (
    echo WARNING: postgres did not report ready in time - continuing anyway.
    goto :eof
)
timeout /t 1 /nobreak >nul
goto waitforpg_loop
