param([string]$Mission = "", [string]$Map = "", [int]$Seconds = 40, [string]$Ticks = "", [string]$Test = "", [switch]$Keep)
# Launches OpenDR straight into a campaign map, waits, then closes it and prints exceptions, the debug log,
# screenshots and drscenario.log. Screenshots come from the game itself, so they work with the screen locked.
#   pwsh -File tools/campaign/run-game.ps1 -Mission m01f -Seconds 40 [-Ticks "100,500"] [-Test "100:cash 0 6000;145:shot"] [-Keep]
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:MOD_SEARCH_PATHS = "$root\mods"
$env:ENGINE_DIR = ".."
Set-Location "$root\engine"
Get-Process OpenRA -ErrorAction SilentlyContinue | Stop-Process -Force
if ($Mission) {
    $Map = (dotnet bin\OpenRA.Utility.dll dr --map-hash "$env:APPDATA\OpenRA\maps\dr\campaign\$Mission" | Select-Object -Last 1).Trim()
    "map $Mission = $Map"
}
$log = "$env:APPDATA\OpenRA\Logs"
if (Test-Path $log) { Remove-Item "$log\*" -Force -ErrorAction SilentlyContinue }
$shots = "$env:APPDATA\OpenRA\Screenshots"
if (Test-Path $shots) { Remove-Item "$shots\*" -Recurse -Force -ErrorAction SilentlyContinue }
$env:OPENDR_SCREENSHOT_TICKS = $Ticks
$env:OPENDR_TEST = $Test
$launchArgs = @("Game.Mod=dr", "Engine.EngineDir=..", "Engine.ModSearchPaths=$root\mods",
    "Graphics.Mode=Windowed", "Graphics.WindowedSize=1600,900", "Launch.Map=$Map")
$p = Start-Process -FilePath bin\OpenRA.exe -ArgumentList $launchArgs -PassThru
Start-Sleep -Seconds $Seconds
if (-not $Keep) { $p | Stop-Process -Force -ErrorAction SilentlyContinue }
Get-ChildItem $log -Filter "exception*" -ErrorAction SilentlyContinue | ForEach-Object { "--- $($_.Name)"; Get-Content $_.FullName | Select-Object -First 25 }
foreach ($f in "debug.log", "server.log") {
    $c = Get-Content "$log\$f" -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch "Taking screenshot|Initial m|Accepted connection|has joined|is Ready" }
    if ($c) { "--- $f"; $c | Select-Object -Last 25 }
}
Get-ChildItem $shots -Recurse -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { "shot " + $_.FullName }
"--- drscenario.log"
Get-Content "$log\drscenario.log" -ErrorAction SilentlyContinue | Select-Object -First 80
