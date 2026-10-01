# Builds the mod (unless -NoBuild) and converts campaign missions, all or those named, into the user map folder.
# Closes OpenRA first: a running game holds the mod DLL and the build cannot replace it.
#   pwsh -File tools/campaign/build-import.ps1 [-NoBuild] [M01F M06F ...]
$NoBuild = $args -contains "-NoBuild"; $Missions = @($args | Where-Object { $_ -ne "-NoBuild" })
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$env:MOD_SEARCH_PATHS = "$root\mods"
$env:ENGINE_DIR = ".."
Get-Process OpenRA -ErrorAction SilentlyContinue | Stop-Process -Force; Start-Sleep -Milliseconds 500
Set-Location $root
if (-not $NoBuild) {
    $b = dotnet build OpenRA.Mods.Dr/OpenRA.Mods.Dr.csproj -c Release --nologo -v q -clp:DisableConsoleColor 2>&1
    $errs = $b | Select-String -Pattern ": error " | Select-Object -Unique
    if ($errs) { $errs | Select-Object -First 30; exit 1 }
}
Set-Location engine
dotnet bin\OpenRA.Utility.dll dr --import-dr-campaign "$root\DrData\dark" "$env:APPDATA\OpenRA\maps\dr\campaign" @Missions 2>&1
