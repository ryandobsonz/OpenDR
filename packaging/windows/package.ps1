# Builds the self-contained Windows package: build\Dark Reign\ and build\DarkReign-<version>-win-x64.zip.
# Unzipped anywhere, it needs no .NET install: DarkReign.exe asks for the player's copy of Dark Reign
# and installs the game data from it (import-campaign.ps1). No game data is packaged.
#   pwsh -File packaging/windows/package.ps1 [-Version 0.1.0]
# It builds from a copy of the sources in build\src, so the working engine/bin is left alone.
param([string]$Version = "")
$ErrorActionPreference = "Stop"
$env:PATH = "C:\Program Files\dotnet;$env:PATH"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if (-not $Version) { $Version = (git -C $root describe --tags --always --dirty 2>$null) }
if (-not $Version) { $Version = Get-Date -Format "yyyyMMdd" }

$build = Join-Path $root "build"
$src = Join-Path $build "src"
$out = Join-Path $build "Dark Reign"
$zip = Join-Path $build "DarkReign-$Version-win-x64.zip"
foreach ($dir in $src, $out) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
New-Item -ItemType Directory -Force $src, $out | Out-Null

function Copy-Tree([string]$from, [string]$to, [string[]]$skipDirs = @(), [string[]]$skipFiles = @()) {
    $options = @("/E", "/NFL", "/NDL", "/NJH", "/NJS", "/NP")
    if ($skipDirs) { $options += @("/XD") + $skipDirs }
    if ($skipFiles) { $options += @("/XF") + $skipFiles }
    robocopy $from $to @options | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $from failed." }
}

function Invoke-Dotnet([string]$what, [string[]]$arguments) {
    & dotnet @arguments --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "$what failed." }
}

"Copying the sources..."
Copy-Tree (Join-Path $root "engine") (Join-Path $src "engine") @("bin", "obj")
Copy-Tree (Join-Path $root "OpenRA.Mods.Dr") (Join-Path $src "OpenRA.Mods.Dr") @("bin", "obj")
Copy-Tree (Join-Path $root "launcher") (Join-Path $src "launcher") @("bin", "obj")
Copy-Tree (Join-Path $root "packaging\dark-reign\out") (Join-Path $src "packaging\dark-reign\out")
Copy-Item (Join-Path $root "OpenRA.Mods.Dr.sln") $src
$common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true", "-p:TargetPlatform=win-x64", "-p:PublishDir=$out/")

"Building the engine..."
Invoke-Dotnet "The engine build" (@("publish", (Join-Path $src "engine\OpenRA.sln")) + $common +
    @("-p:CopyGenericLauncher=False", "-p:CopyCncDll=True", "-p:CopyD2kDll=False"))

"Building the mod..."
# Only its own library is needed; the engine above brought everything it uses.
Invoke-Dotnet "The mod build" @("build", (Join-Path $src "OpenRA.Mods.Dr\OpenRA.Mods.Dr.csproj"), "-c", "Release", "-p:TargetPlatform=win-x64")
Copy-Item (Join-Path $src "engine\bin\OpenRA.Mods.Dr.dll") $out

"Building the game host..."
$icon = Join-Path $src "packaging\dark-reign\out\icon.ico"
Invoke-Dotnet "The game host build" (@("publish", (Join-Path $src "engine\OpenRA.WindowsLauncher\OpenRA.WindowsLauncher.csproj")) + $common +
    @("-p:LauncherName=DarkReignGame", "-p:LauncherIcon=$icon", "-p:ModID=dr", "-p:DisplayName=Dark Reign",
      "-p:FaqUrl=https://github.com/ryandobsonz/OpenDR", "-p:Product=Dark Reign", "-p:AssemblyTitle=Dark Reign", "-p:InformationalVersion=$Version"))

"Building the launcher..."
# The launcher shares the engine's runtime. Where both bring a file, the higher version stays:
# the engine's NuGet System.Threading.Channels 9 over the runtime's 8, and WPF's WindowsBase 8
# over the runtime's 4.0 compatibility stub. Publishing straight into the folder would not do:
# publish copies by date, and the runtime's 8.0.31 files are dated after the 9.0.0 packages.
$launcherOut = Join-Path $build "launcher"
if (Test-Path $launcherOut) { Remove-Item $launcherOut -Recurse -Force }
Invoke-Dotnet "The launcher build" (@("publish", (Join-Path $src "launcher\DarkReignLauncher.csproj"), "-c", "Release", "-r", "win-x64",
    "--self-contained", "true", "-p:PublishSingleFile=false", "-p:PublishDir=$launcherOut/"))

function Get-FileRank([string]$path) {
    try { return [Reflection.AssemblyName]::GetAssemblyName($path).Version }
    catch { $v = (Get-Item $path).VersionInfo; return [version]::new($v.FileMajorPart, $v.FileMinorPart, $v.FileBuildPart, $v.FilePrivatePart) }
}

foreach ($file in Get-ChildItem $launcherOut -File -Recurse) {
    $target = Join-Path $out $file.FullName.Substring($launcherOut.Length + 1)
    if ((Test-Path $target) -and (Get-FileRank $target) -ge (Get-FileRank $file.FullName)) { continue }
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item $file.FullName $target -Force
}
Remove-Item $launcherOut -Recurse -Force

# The engine's generic launchers: the game host replaces OpenRA.exe, and the solution's own
# build of the Windows launcher lacks the mod's name.
Get-ChildItem $out -File | Where-Object { $_.Name -like "OpenRA.WindowsLauncher*" -or $_.Name -match '^OpenRA\.(exe|dll|deps\.json|runtimeconfig\.json|dll\.config)$' } |
    Remove-Item -Force

"Adding the game files..."
$engine = Join-Path $src "engine"
foreach ($file in "VERSION", "AUTHORS", "COPYING", "IP2LOCATION-LITE-DB1.IPV6.BIN.ZIP", "global mix database.dat") {
    Copy-Item -LiteralPath (Join-Path $engine $file) $out
}
Copy-Tree (Join-Path $engine "glsl") (Join-Path $out "glsl")
# The mods as the game runs them: the engine's common (mod.yaml reads ^EngineDir|mods/common), the rest from here.
foreach ($mod in "dr", "dr-content", "common-content") {
    Copy-Tree (Join-Path $root "mods\$mod") (Join-Path $out "mods\$mod") @("Content", "sounds_vol", "generated") @("*.dll", "*.pdb", "*.mdb")
}
Copy-Tree (Join-Path $engine "mods\common") (Join-Path $out "mods\common")
Copy-Item (Join-Path $root "import-campaign.ps1"), (Join-Path $root "CAMPAIGN.md") $out
Get-ChildItem $out -Filter "*.pdb" -Recurse | Remove-Item -Force

"Checking..."
# The game host loads the engine and lists the mods, then stops at a mod that does not exist,
# before opening a window. Its logs go to a scratch support folder.
$check = Join-Path $build "check"
New-Item -ItemType Directory -Force $check | Out-Null
$hostOutput = & (Join-Path $out "DarkReignGame.exe") "Engine.LaunchPath=$(Join-Path $out 'DarkReign.exe')" "Game.Mod=nomod" `
    "Engine.ModSearchPaths=$(Join-Path $out 'mods')" "Engine.SupportDir=$check" 2>&1 | Out-String
Remove-Item $check -Recurse -Force -ErrorAction SilentlyContinue
if ($hostOutput -notmatch "Unknown or invalid mod 'nomod'" -or $hostOutput -notmatch "(?m)^\s+dr \(") {
    throw "The packaged game host does not start:`n$hostOutput"
}

"Packing..."
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $out -DestinationPath $zip -CompressionLevel Optimal
$size = [math]::Round((Get-Item $zip).Length / 1MB)
"Built $out"
"Built $zip ($size MB)"
