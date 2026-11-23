<#
.SYNOPSIS
  Publishes a launcher release and announces it to installed launchers.

.DESCRIPTION
  From a clean, pushed main: builds the self-contained exe, creates the GitHub release v<version> with it
  (version from LunarLauncher.csproj), checks the uploaded file, then writes the "launcher" entry of
  client-version.json and pushes it to main. Launchers read that file on start and update themselves.
  With -DryRun it only builds and prints what it would do.

.EXAMPLE
  scripts/release.ps1 -Notes "Collega un'installazione di ArcaneMP esistente."
  scripts/release.ps1 -Notes "Correzione importante." -MinVersion 0.4.6   # older launchers must update to play
#>
param(
    [Parameter(Mandatory = $true)][string[]]$Notes,
    [string]$MinVersion,
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $repo

function Fail($message) { Write-Error $message; exit 1 }
# Not named "Git": PowerShell names are case-insensitive, so it would call itself instead of git.exe.
function Invoke-Git { & git.exe @args; if ($LASTEXITCODE -ne 0) { Fail "git $($args -join ' ') failed" } }

[xml]$project = Get-Content (Join-Path $repo 'src\LunarLauncher\LunarLauncher.csproj')
$version = ([string]$project.Project.PropertyGroup.Version).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { Fail "Unexpected version '$version' in LunarLauncher.csproj" }
if ($MinVersion -and $MinVersion -notmatch '^\d+\.\d+\.\d+$') { Fail "MinVersion must look like 0.4.6" }
if ($MinVersion -and [version]$MinVersion -gt [version]$version) { Fail "MinVersion $MinVersion is newer than the release $version" }
$tag = "v$version"

if (-not (Test-Path (Join-Path $repo 'launcher.json'))) { Fail 'launcher.json is missing: it is embedded in the exe' }
if (-not $DryRun) {
    if ((git rev-parse --abbrev-ref HEAD) -ne 'main') { Fail 'Release from main' }
    if (git status --porcelain) { Fail 'The working tree is not clean' }
    Invoke-Git fetch -q origin main
    if ((git rev-parse HEAD) -ne (git rev-parse origin/main)) { Fail 'main is not the same as origin/main: push or pull first' }
    # Redirected stderr would be a terminating error under Stop, and "release not found" is the expected answer.
    $ErrorActionPreference = 'Continue'
    & gh release view $tag *> $null
    $ErrorActionPreference = 'Stop'
    if ($LASTEXITCODE -eq 0) { Fail "Release $tag already exists: raise <Version> in LunarLauncher.csproj" }
}

Write-Host "Building $version..."
& dotnet publish src/LunarLauncher -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -o publish -nologo -v q
if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed' }
$exe = Join-Path $repo 'publish\LunarLauncher.exe'
$built = ((Get-Item $exe).VersionInfo.ProductVersion -split '\+')[0]
if ($built -ne $version) { Fail "The built exe reports $built, expected $version" }
$sha = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
$size = (Get-Item $exe).Length
$url = "https://github.com/ende19/lunar-launcher/releases/download/$tag/LunarLauncher.exe"

$body = @("Lunar Roleplay Launcher $version", '')
$body += ($Notes | ForEach-Object { "- $_" })
$body += @('', ('Windows potrebbe mostrare un avviso SmartScreen al primo avvio: "Ulteriori informazioni" ' + [char]0x2192 + ' "Esegui comunque".'), '',
    'SHA-256 di `LunarLauncher.exe`:', "``$sha``")

$entry = "  `"launcher`": {`n    `"version`": `"$version`",`n    `"url`": `"$url`",`n    `"sha256`": `"$sha`",`n    `"size`": $size"
if ($MinVersion) { $entry += ",`n    `"minVersion`": `"$MinVersion`"" }
$entry += "`n  }"

if ($DryRun) {
    Write-Host "`n[DryRun] Release $tag, $size bytes, SHA-256 $sha`n"
    Write-Host ($body -join "`n")
    Write-Host "`n[DryRun] client-version.json entry:`n$entry"
    exit 0
}

$notesFile = Join-Path ([IO.Path]::GetTempPath()) "lunar-launcher-$tag.md"
[IO.File]::WriteAllText($notesFile, ($body -join "`n") + "`n", (New-Object Text.UTF8Encoding $false))
Write-Host "Creating release $tag..."
& gh release create $tag $exe --target main --title "Lunar Roleplay Launcher $version" --notes-file $notesFile
if ($LASTEXITCODE -ne 0) { Fail 'gh release create failed' }
Remove-Item $notesFile -ErrorAction SilentlyContinue
$digest = & gh release view $tag --json assets --jq '.assets[0].digest'
if ($digest -ne "sha256:$sha") { Fail "Uploaded file digest '$digest' does not match $($sha): check the release before announcing it" }

# Announce it: replace (or add) the "launcher" entry, keeping the rest of the file as it is.
$manifestPath = Join-Path $repo 'client-version.json'
$manifest = [IO.File]::ReadAllText($manifestPath) -replace "`r`n", "`n"
if ($manifest -match '(?s)\n  "launcher": \{[^{}]*\}') {
    $manifest = $manifest -replace '(?s)\n  "launcher": \{[^{}]*\}', ("`n" + $entry)
} else {
    $manifest = $manifest -replace '(?s)\n\}\s*$', (",`n$entry`n}`n")
}
$null = $manifest | ConvertFrom-Json # Refuse to push a file launchers cannot read.
[IO.File]::WriteAllText($manifestPath, $manifest, (New-Object Text.UTF8Encoding $false))
Invoke-Git add client-version.json
Invoke-Git commit -q -m "Release: launcher $version announced in client-version.json"
Invoke-Git push -q origin main
Write-Host "Released $tag and announced it. Installed launchers update on their next start."
