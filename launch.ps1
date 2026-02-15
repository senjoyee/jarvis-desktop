param(
    [switch]$Dev
)

# Get the root directory
$RootDir = $PSScriptRoot
if (-not $RootDir) { $RootDir = Get-Location }

if ($Dev) {
    Write-Host "==========================================" -ForegroundColor Magenta
    Write-Host "   Starting Jarvis in DEVELOPMENT Mode    " -ForegroundColor Magenta
    Write-Host "==========================================" -ForegroundColor Magenta
    
    # Start UI dev server in a new window
    Write-Host "[1/2] Starting React Dev Server (Vite)..." -ForegroundColor Yellow
    Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$RootDir\ui'; Write-Host 'Starting Vite Dev Server...' -ForegroundColor Cyan; npm run dev"
    
    # Wait a bit for the dev server to initialize
    Write-Host "Waiting for UI server to start..." -ForegroundColor Gray
    Start-Sleep -Seconds 3
    
    # Run WPF app with Dev environment variable
    Write-Host "[2/2] Launching WPF App with Dev Bridge..." -ForegroundColor Green
    $env:CHLOYE_DEV_MODE="1"
    Set-Location "$RootDir\src\ChloyeDesktop"
    dotnet run
} else {
    Write-Host "==========================================" -ForegroundColor Cyan
    Write-Host "      Starting Jarvis Desktop             " -ForegroundColor Cyan
    Write-Host "==========================================" -ForegroundColor Cyan
    
    # 1. Prepare React UI
    Write-Host "[1/2] Preparing UI..." -ForegroundColor Yellow
    Set-Location "$RootDir\ui"
    if (-not (Test-Path "node_modules")) {
        Write-Host "Installing npm packages (this may take a minute)..." -ForegroundColor Gray
        npm install
    }

    Write-Host "Building UI production bundle..." -ForegroundColor Gray
    npm run build

    # 2. Launch WPF Application
    Write-Host "[2/2] Running WPF Application..." -ForegroundColor Green
    Set-Location "$RootDir\src\ChloyeDesktop"
    dotnet run
}
