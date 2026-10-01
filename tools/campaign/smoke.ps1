param([string[]]$Missions = @(), [int]$Seconds = 40)
# Runs each campaign mission (all, or those named) briefly and reports crashes, trigger activity and a screenshot,
# into tools/campaign/out. About 50 seconds a mission.
$out = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $out | Out-Null
if ($Missions.Count -eq 0) {
    $Missions = Get-ChildItem "$env:APPDATA\OpenRA\maps\dr\campaign" -Directory | Where-Object Name -match '^m\d' | ForEach-Object Name
}
foreach ($m in $Missions) {
    $r = pwsh -NoProfile -File "$PSScriptRoot\run-game.ps1" -Mission $m -Seconds $Seconds -Ticks "500" 2>&1
    $exc = ($r | Select-String "^Exception of type" | Select-Object -First 1)
    $shot = ($r | Select-String "^shot " | Select-Object -Last 1)
    $lines = ($r | Select-String "^\s+\d+ " ).Count
    if ($shot) { Copy-Item ($shot.Line.Substring(5)) "$out\$m.png" -Force }
    "{0}: {1}; {2} scenario lines; shot {3}" -f $m, ($(if ($exc) { $exc.Line } else { "ok" })), $lines, [bool]$shot
    $r | Set-Content "$out\$m.log"
}
