# Sets up the original Dark Reign campaign for OpenDR from a full copy of the game:
# installs the game content OpenDR reads, then converts the campaign missions into
# the user's map folder. See CAMPAIGN.md.
#
#   pwsh -File import-campaign.ps1 -GameDir "C:\Games\Dark Reign"   [missions...]
#
# GameDir is the folder holding the game's 'dark' folder (a 1.8.2 or GOG install).
param(
    [Parameter(Mandatory = $true)][string]$GameDir,
    [Parameter(ValueFromRemainingArguments = $true)][string[]]$Missions = @()
)
$ErrorActionPreference = "Stop"

function Find-Path([string]$root, [string]$relative) {
    # The game's files mix case freely.
    $current = $root
    foreach ($part in $relative -split '[\\/]') {
        $match = Get-ChildItem -LiteralPath $current -Force -ErrorAction SilentlyContinue | Where-Object { $_.Name -ieq $part } | Select-Object -First 1
        if (-not $match) { return $null }
        $current = $match.FullName
    }
    return $current
}

$dark = Find-Path $GameDir "dark"
if (-not $dark) { throw "No 'dark' folder in $GameDir" }

"Copying the game data..."
$content = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "OpenRA\Content\dr"
$files = [ordered]@{
    "SPRITES.FTG" = "graphics/SPRITES.FTG"; "SOUNDS.FTG" = "sndfx/SOUNDS.FTG"; "shell/SOUNDS.FTG" = "shell/SOUNDS.FTG"
    "mouse.crs" = "graphics/INTFACE/MOUSE.CRS"; "spriteEx.ftg" = "graphics/spriteEx.ftg"; "soundsEx.ftg" = "sndfx/soundsEx.ftg"
    "BARREN/BARREN.TIL" = "graphics/BARREN/BARREN.TIL"; "BARREN/BARREN.PAL" = "graphics/BARREN/BARREN.PAL"; "BARREN/SPRITES.FTG" = "graphics/BARREN/SPRITES.FTG"
    "JUNGLE/JUNGLE.TIL" = "graphics/JUNGLE/JUNGLE.TIL"; "JUNGLE/JUNGLE.PAL" = "graphics/JUNGLE/JUNGLE.PAL"; "JUNGLE/SPRITES.FTG" = "graphics/JUNGLE/SPRITES.FTG"
    "SNOW/SNOW.TIL" = "graphics/SNOW/SNOW.TIL"; "SNOW/SNOW.PAL" = "graphics/SNOW/SNOW.PAL"; "SNOW/SPRITES.FTG" = "graphics/SNOW/SPRITES.FTG"
    "ALIEN/ALIEN.TIL" = "graphics/ALIEN/ALIEN.TIL"; "ALIEN/ALIEN.PAL" = "graphics/ALIEN/ALIEN.PAL"; "ALIEN/SPRITEEX.FTG" = "graphics/ALIEN/SPRITEEX.FTG"
    "BARREN/spriteEx.ftg" = "graphics/BARREN/spriteEx.ftg"; "JUNGLE/spriteEx.ftg" = "graphics/JUNGLE/spriteEx.ftg"; "SNOW/spriteEx.ftg" = "graphics/SNOW/spriteEx.ftg"
    # The "Auran extra content" package OpenDR otherwise downloads; the 1.8.2 patch ships it.
    "aust/AUST.FTG" = "graphics/AUST/AUST.FTG"; "volcanic/VOLCANIC.FTG" = "graphics/VOLCANIC/VOLCANIC.FTG"
    "auralien/AURALIEN.FTG" = "graphics/AURALIEN/AURALIEN.FTG"; "asteroid/ASTEROID.FTG" = "graphics/ASTEROID/ASTEROID.FTG"
    "addon/aurunits/aursprite.ftg" = "addon/aurunits/aursprite.ftg"; "addon/aurunits/aursound.ftg" = "addon/aurunits/aursound.ftg"
    "addon/terrorist/terrsprite.ftg" = "addon/terrorist/terrsprite.ftg"; "addon/terrorist/terrsound.ftg" = "addon/terrorist/terrsound.ftg"
}
$missing = @()
foreach ($target in $files.Keys) {
    $source = Find-Path $dark $files[$target]
    if (-not $source) { $missing += $files[$target]; continue }
    $dest = Join-Path $content $target
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    Copy-Item -LiteralPath $source $dest -Force
}
# OpenDR's content check looks for this file from the extra content package.
$legal = Get-ChildItem -LiteralPath $GameDir -Recurse -Filter legal.txt -ErrorAction SilentlyContinue | Select-Object -First 1
if ($legal) { Copy-Item -LiteralPath $legal.FullName (Join-Path $content "legal.txt") -Force }
elseif (-not (Test-Path (Join-Path $content "legal.txt"))) { Set-Content (Join-Path $content "legal.txt") "Dark Reign extra content" }
if ($missing) { Write-Warning ("Not found in the game, so not installed: " + ($missing -join ", ")) }

# The 1.8.2 patch launcher's art, which DarkReign.exe shows; without it, it draws its own.
$art = Find-Path $GameDir "launcher/ldata"
if ($art) {
    New-Item -ItemType Directory -Force (Join-Path $content "launcher") | Out-Null
    foreach ($image in "bg-dkreign.png", "logo-dkreign.png") {
        $source = Find-Path $art $image
        if ($source) { Copy-Item -LiteralPath $source (Join-Path $content "launcher\$image") -Force }
    }
}

# The CD soundtrack, as the 1.8.2 patch and GOG keep it beside the game.
$music = Find-Path $GameDir "music"
if ($music) {
    Get-ChildItem -LiteralPath $music -Filter "Track*.ogg" | ForEach-Object { Copy-Item -LiteralPath $_.FullName (Join-Path $content $_.Name) -Force }
}

# The repository keeps the engine in engine/bin; a package (packaging/windows/package.ps1) beside this script.
"Converting the campaign missions..."
$env:MOD_SEARCH_PATHS = Join-Path $PSScriptRoot "mods"
$utility = Join-Path $PSScriptRoot "engine\bin\OpenRA.Utility.exe"
if (Test-Path $utility) { $env:ENGINE_DIR = ".." } else { $utility = Join-Path $PSScriptRoot "OpenRA.Utility.exe" }
$maps = Join-Path ([Environment]::GetFolderPath("ApplicationData")) "OpenRA\maps\dr\campaign"
Push-Location (Split-Path $utility)
try {
    # Windows PowerShell turns a native command's error output into a terminating error under "Stop".
    $ErrorActionPreference = "Continue"
    & $utility dr --import-dr-campaign $dark $maps @Missions
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}
exit $code
