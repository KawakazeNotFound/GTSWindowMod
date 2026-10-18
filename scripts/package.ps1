param([switch]$LockedMode, [string]$ReferenceRoot, [string]$BepInExCoreRoot, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'GTSWindowMod.csproj'
$version = [string]([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version
$pluginSource = Get-Content -LiteralPath (Join-Path $root 'Plugin.cs') -Raw
if ($pluginSource -notmatch ('Version = "' + [regex]::Escape($version) + '"')) { throw 'Project and plugin versions differ' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -cne "v$version") { throw 'Release tag and plugin version differ' }
$build = @('build', $project, '-c', 'Release')
if ($LockedMode) { $build += '-p:RestoreLockedMode=true' }
if ($ReferenceRoot) { $build += "-p:ReferenceRoot=$ReferenceRoot" }
& dotnet @build
if ($LASTEXITCODE -ne 0) { throw "Plugin build failed: $LASTEXITCODE" }
& dotnet run --project (Join-Path $root 'tests\GTSWindowMod.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw "Logic/native fixture tests failed: $LASTEXITCODE" }
$smoke = @('build', (Join-Path $root 'tests\HarmonySmoke\HarmonySmoke.csproj'), '-c', 'Release')
if ($LockedMode) { $smoke += '-p:RestoreLockedMode=true' }
if ($BepInExCoreRoot) { $smoke += "-p:BepInExCoreRoot=$BepInExCoreRoot" }
& dotnet @smoke
if ($LASTEXITCODE -ne 0) { throw "Harmony smoke build failed: $LASTEXITCODE" }
& (Join-Path $root 'tests\HarmonySmoke\bin\Release\net472\HarmonySmoke.exe')
if ($LASTEXITCODE -ne 0) { throw "Harmony smoke tests failed: $LASTEXITCODE" }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $root 'dist\release' }
$stage = Join-Path $root ('.cache\packages\' + [guid]::NewGuid().ToString('N'))
$plugins = Join-Path $stage 'BepInEx\plugins\GTSWindowMod'
$docs = Join-Path $stage 'docs'
New-Item -ItemType Directory -Path $plugins, $docs -ErrorAction Stop | Out-Null
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$dll = Join-Path $root 'bin\Release\net472\GTSWindowMod.dll'
$assembly = [System.Reflection.AssemblyName]::GetAssemblyName($dll)
if ($assembly.Name -cne 'GTSWindowMod' -or $assembly.Version.ToString(3) -cne $version) { throw 'Unexpected assembly identity/version' }
Copy-Item -LiteralPath $dll -Destination $plugins
foreach ($file in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
}
Copy-Item -LiteralPath (Join-Path $root 'docs\VALIDATION.md'), (Join-Path $root 'docs\INTEGRATION.md') -Destination $docs
$commit = $env:GITHUB_SHA
if (-not $commit) {
    $commit = & git -C $root rev-parse HEAD 2>$null
    if ($LASTEXITCODE -ne 0) { $commit = 'UNKNOWN' }
}
$metadata = [ordered]@{
    schemaVersion = 1; project = 'GTSWindowMod'; version = $version; commit = [string]$commit
    repository = 'KawakazeNotFound/GTSWindowMod'; ref = $env:GITHUB_REF_NAME
    runId = $env:GITHUB_RUN_ID; runAttempt = $env:GITHUB_RUN_ATTEMPT
    builtAtUtc = [DateTime]::UtcNow.ToString('o')
    buildReferences = @{ BepInExCore = '5.4.21'; HarmonyX = '2.9.0'; UnityEngineModules = '2019.4.28' }
    validation = @{ logicAndHiddenWindowChecks = 85; syntheticHarmonyChecks = 8; gameRuntimeVerified = $false }
}
$encoding = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText((Join-Path $stage 'BUILD_INFO.json'), ($metadata | ConvertTo-Json -Depth 5), $encoding)
$manifest = @(Get-ChildItem -LiteralPath $stage -Recurse -File | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($stage.Length + 1).Replace('\', '/')
    "$((Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash)  $relative"
})
[System.IO.File]::WriteAllLines((Join-Path $stage 'SHA256SUMS.txt'), $manifest, $encoding)
$zip = Join-Path $OutputDirectory "GTSWindowMod-v$version.zip"
$children = @(Get-ChildItem -LiteralPath $stage | ForEach-Object FullName)
Compress-Archive -LiteralPath $children -DestinationPath $zip -Force
& (Join-Path $PSScriptRoot 'verify-package.ps1') -PackagePath $zip -ExpectedVersion $version -ExpectedCommit ([string]$commit)
if (-not $?) { throw 'Package verification failed' }
[System.IO.File]::WriteAllText("$zip.sha256", "$((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash)  $([System.IO.Path]::GetFileName($zip))`n", $encoding)
Copy-Item -LiteralPath (Join-Path $stage 'BUILD_INFO.json') -Destination $OutputDirectory -Force
Copy-Item -LiteralPath (Join-Path $root "docs\release-v$version.md") -Destination (Join-Path $OutputDirectory 'RELEASE_NOTES.md') -Force
Write-Output "Package: $zip"
$global:LASTEXITCODE = 0
