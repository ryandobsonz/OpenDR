# Builds the launcher, DarkReign.exe, into the root of the game folder (beside engine/ and mods/),
# and the game host, engine/bin/DarkReignGame.exe: OpenRA's own Windows launcher built for the mod,
# so the game runs as Dark Reign with its icon rather than as OpenRA.exe.
#   pwsh -File launcher/build.ps1
# It needs the .NET 8 SDK and a built engine (make.cmd all), and the .NET 8 Desktop Runtime to run.
$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$out = Join-Path $PSScriptRoot "bin\publish"
$icon = Join-Path $root "packaging\dark-reign\out\icon.ico"

Get-Process DarkReign, DarkReignGame -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet publish (Join-Path $PSScriptRoot "DarkReignLauncher.csproj") -c Release -o $out --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "The launcher build failed." }
Copy-Item (Join-Path $out "DarkReign.exe") (Join-Path $root "DarkReign.exe") -Force
"Built $(Join-Path $root 'DarkReign.exe')"

# The engine's projects are already built; building their references again would rebuild
# OpenRA.Game with these properties.
$hostProject = Join-Path $root "engine\OpenRA.WindowsLauncher\OpenRA.WindowsLauncher.csproj"
Remove-Item (Join-Path $root "engine\OpenRA.WindowsLauncher\obj") -Recurse -Force -ErrorAction SilentlyContinue
dotnet build $hostProject -c Release --nologo -v quiet -p:BuildProjectReferences=false `
    -p:LauncherName=DarkReignGame "-p:LauncherIcon=$icon" -p:ModID=dr "-p:DisplayName=Dark Reign" `
    -p:FaqUrl=https://github.com/ryandobsonz/OpenDR "-p:Product=Dark Reign" "-p:AssemblyTitle=Dark Reign"
if ($LASTEXITCODE -ne 0) { throw "The game host build failed." }
"Built $(Join-Path $root 'engine\bin\DarkReignGame.exe')"
