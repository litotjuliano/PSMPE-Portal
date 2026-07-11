@echo off
REM Delayed expansion deliberately NOT enabled: with it on, "!" in .env values
REM (e.g. a password like "ChangeMe123!") gets silently stripped when read via
REM the for /f loop below, since "!" is delayed expansion's own trigger character.
REM Nothing else in this script needs !VAR! syntax, so plain %VAR% throughout is fine.
setlocal
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
    goto :afterdocker
)

REM "where docker" only confirms the CLI is installed, not that Docker Desktop's engine
REM is actually running - docker info is the real reachability check. Without this, a
REM closed Docker Desktop silently falls through to "continuing anyway" below, the
REM backend starts with no reachable database, and login fails with a confusing
REM "invalid credentials" message that has nothing to do with credentials.
docker info >nul 2>&1
if not errorlevel 1 (
    set DOCKER_OK=1
    goto :afterdocker
)

echo Docker is installed but Docker Desktop does not appear to be running.
set "DOCKER_DESKTOP_EXE=C:\Program Files\Docker\Docker\Docker Desktop.exe"
if exist "%DOCKER_DESKTOP_EXE%" (
    echo Launching Docker Desktop...
    start "" "%DOCKER_DESKTOP_EXE%"
) else (
    echo Could not find Docker Desktop at the default install path.
    echo Please start Docker Desktop manually now.
)
call :waitfordocker
if "%DOCKER_OK%"=="0" (
    echo.
    echo ============================================
    echo  Docker did not start in time - aborting
    echo ============================================
    echo Start Docker Desktop yourself and re-run this script once it's ready.
    echo ^(This check exists so you never hit the confusing "invalid credentials"
    echo  error that happens when the backend runs without a reachable database.^)
    exit /b 1
)

:afterdocker
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

echo.
echo Waiting for the frontend to come up before opening your browser...
call :waitforweb
start "" "http://localhost:5173"
goto :eof

:killport
set PORT=%1
for /f "tokens=5" %%a in ('netstat -aon ^| findstr ":%PORT% " ^| findstr "LISTENING"') do (
    echo Killing process on port %PORT% (PID %%a)
    taskkill /F /PID %%a >nul 2>&1
)
goto :eof

:waitforweb
set _wtries=0
:waitforweb_loop
netstat -aon | findstr ":5173 " | findstr "LISTENING" >nul 2>&1
if not errorlevel 1 goto :eof
set /a _wtries+=1
if %_wtries% GEQ 30 (
    echo WARNING: frontend did not come up in time - open http://localhost:5173 manually.
    goto :eof
)
timeout /t 1 /nobreak >nul
goto waitforweb_loop

:waitfordocker
echo Waiting for Docker Desktop to start...
set _dtries=0
:waitfordocker_loop
docker info >nul 2>&1
if not errorlevel 1 (
    set DOCKER_OK=1
    echo Docker is ready.
    goto :eof
)
set /a _dtries+=1
if %_dtries% GEQ 40 (
    echo WARNING: Docker Desktop did not start within 2 minutes.
    goto :eof
)
set /a _delapsed=_dtries*3
echo   still waiting... ^(%_delapsed%s elapsed^)
timeout /t 3 /nobreak >nul
goto waitfordocker_loop

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
