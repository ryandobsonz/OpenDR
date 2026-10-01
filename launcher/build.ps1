# Builds the launcher, DarkReign.exe, into the root of the game folder (beside engine/ and mods/).
#   pwsh -File launcher/build.ps1
# It needs the .NET 8 SDK to build, and the .NET 8 Desktop Runtime to run.
$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$out = Join-Path $PSScriptRoot "bin\publish"

Get-Process DarkReign -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish (Join-Path $PSScriptRoot "DarkReignLauncher.csproj") -c Release -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "The launcher build failed." }

Copy-Item (Join-Path $out "DarkReign.exe") (Join-Path $root "DarkReign.exe") -Force
"Built $(Join-Path $root 'DarkReign.exe')"
