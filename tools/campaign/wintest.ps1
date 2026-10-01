# For each campaign mission, destroy every team the scenario file makes the player's enemy and report the outcome.
# Missions whose goals are more than destruction, or that change alliances at cycle 0, need their own test:
# see CLAUDE.md for those.
$out = Join-Path $PSScriptRoot "out"
New-Item -ItemType Directory -Force $out | Out-Null
$missions = Get-ChildItem "$env:APPDATA\OpenRA\maps\dr\campaign" -Directory | Where-Object Name -match '^m\d' | ForEach-Object Name
foreach ($m in $missions) {
    $scn = Get-ChildItem "$env:APPDATA\OpenRA\maps\dr\campaign\$m" -Filter *.scn | Select-Object -First 1
    $text = Get-Content $scn.FullName -Raw
    $team0 = [regex]::Match($text, 'SetTeam\(0\)\s*\{(.*?)\}', 'Singleline').Groups[1].Value
    $alliance = [regex]::Match($team0, 'SetAlliance\(([^)]*)\)').Groups[1].Value.Trim() -split '\s+'
    $enemies = @(); for ($i = 1; $i -lt 8; $i++) { if ($alliance[$i] -eq '0') { $enemies += $i } }
    $steps = ($enemies | ForEach-Object { "100:killall $_" }) -join ';'
    $r = pwsh -NoProfile -File "$PSScriptRoot\run-game.ps1" -Mission $m -Seconds 30 -Test $steps 2>&1
    $over = ($r | Select-String "game over, winner team (-?\d+)" | Select-Object -First 1)
    $exc = ($r | Select-String "^Exception of type" | Select-Object -First 1)
    $result = if ($exc) { $exc.Line } elseif ($over) { "winner team " + $over.Matches[0].Groups[1].Value } else { "no result" }
    "{0}: killed {1}: {2}" -f $m, ($enemies -join ','), $result
    $r | Set-Content "$out\$m-win.log"
}
