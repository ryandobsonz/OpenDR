param([string]$Mission = "", [string]$Map = "", [int]$Seconds = 40, [string]$Ticks = "", [string]$Test = "", [string]$Shell = "", [switch]$Keep)
# Launches OpenDR straight into a campaign map, waits, then closes it and prints exceptions, the debug log,
# screenshots and drscenario.log. Screenshots come from the game itself, so they work with the screen locked.
#   pwsh -File tools/campaign/run-game.ps1 -Mission m01f -Seconds 40 [-Ticks "100,500"] [-Test "100:cash 0 6000;145:shot"] [-Keep]
# Without a mission it opens the menus; -Shell "main;cube:3;story:3;briefingf:3" screenshots those shell screens.
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
# Test wins stay out of the player's campaign progress (dr-campaign.yaml).
$env:OPENDR_SCRIPTED = "1"
$env:OPENDR_SHELL = $Shell
$launchArgs = @("Game.Mod=dr", "Engine.EngineDir=..", "Engine.ModSearchPaths=$root\mods",
    "Graphics.Mode=Windowed", "Graphics.WindowedSize=1600,900")
if ($Map) { $launchArgs += "Launch.Map=$Map" }
# The engine saves its command-line settings into settings.yaml as it loads, which would leave the
# player's own game windowed at 1600x900; put their file back once the test game has loaded.
$settings = "$env:APPDATA\OpenRA\settings.yaml"
$saved = if (Test-Path $settings) { [IO.File]::ReadAllBytes($settings) } else { $null }
$p = Start-Process -FilePath bin\OpenRA.exe -ArgumentList $launchArgs -PassThru
try {
    Start-Sleep -Seconds $Seconds
    if (-not $Keep) { $p | Stop-Process -Force -ErrorAction SilentlyContinue; $p.WaitForExit(5000) | Out-Null }
} finally {
    if ($saved) { [IO.File]::WriteAllBytes($settings, $saved) } else { Remove-Item $settings -ErrorAction SilentlyContinue }
}
Get-ChildItem $log -Filter "exception*" -ErrorAction SilentlyContinue | ForEach-Object { "--- $($_.Name)"; Get-Content $_.FullName | Select-Object -First 25 }
foreach ($f in "debug.log", "server.log") {
    $c = Get-Content "$log\$f" -ErrorAction SilentlyContinue | Where-Object { $_ -notmatch "Taking screenshot|Initial m|Accepted connection|has joined|is Ready" }
    if ($c) { "--- $f"; $c | Select-Object -Last 25 }
}
Get-ChildItem $shots -Recurse -File -ErrorAction SilentlyContinue | Sort-Object Name | ForEach-Object { "shot " + $_.FullName }
"--- drscenario.log"
Get-Content "$log\drscenario.log" -ErrorAction SilentlyContinue | Select-Object -First 80
