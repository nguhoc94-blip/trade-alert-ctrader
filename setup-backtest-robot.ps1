# Build + deploy Loop6BacktestTradingBot vao cTrader (chay 1 lan hoac sau khi sua code).
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host ""
Write-Host "=== LOOP6 Backtest cBot - Setup ===" -ForegroundColor Cyan
Write-Host ""

Write-Host "[1/3] Build Release..." -ForegroundColor Yellow
dotnet build "TradeAlert.sln" -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "[2/3] Chay unit tests..." -ForegroundColor Yellow
dotnet test "tests\TradeAlert.BacktestRobot.Tests\TradeAlert.BacktestRobot.Tests.csproj" -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

$robotsDir = Join-Path $env:USERPROFILE "Documents\cAlgo\Sources\Robots"
$algo = Join-Path $robotsDir "TradeAlert.BacktestRobot.algo"
$meta = Join-Path $robotsDir "TradeAlert.BacktestRobot.algo.metadata"

Write-Host ""
Write-Host "[3/3] Kiem tra deploy..." -ForegroundColor Yellow
if (-not (Test-Path $algo)) {
    throw "Khong thay file algo - build khong deploy duoc: $algo"
}

Write-Host ""
Write-Host "OK - San sang vao cTrader:" -ForegroundColor Green
Write-Host "  File: $algo"
Write-Host "  Size: $((Get-Item $algo).Length) bytes"
Write-Host "  Meta: $(Test-Path $meta)"
Write-Host ""
Write-Host "Mo cTrader -> Automate -> cBots -> Loop6BacktestTradingBot" -ForegroundColor Green
Write-Host "Chart M15, gan bot, kiem tra params (Enable Trading = true mac dinh)." -ForegroundColor Green
Write-Host ""
Write-Host "Chi tiet: HUONG-DAN-CHAY-BACKTEST.md" -ForegroundColor DarkGray
Write-Host ""
