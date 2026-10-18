param([Parameter(Mandatory=$true)][string]$PackagePath, [string]$ExpectedVersion, [string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem
$allowed = @('BepInEx/plugins/GTSWindowMod/GTSWindowMod.dll', 'README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'CHANGELOG.md', 'docs/VALIDATION.md', 'docs/INTEGRATION.md', 'BUILD_INFO.json', 'SHA256SUMS.txt')
$archive = [System.IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
try {
    $files = @($archive.Entries | Where-Object { $_.Name.Length -gt 0 })
    $names = @($files | ForEach-Object { $_.FullName.Replace('\','/') })
    if ($names.Count -ne $allowed.Count -or @($names | Select-Object -Unique).Count -ne $names.Count) { throw 'Unexpected or duplicated package entries' }
    foreach ($name in $names) { if ($allowed -cnotcontains $name) { throw "Unexpected package entry: $name" } }
    function Read-ZipText([string]$name) {
        $entry = $files | Where-Object { $_.FullName.Replace('\','/') -ceq $name }
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    $metadata = (Read-ZipText 'BUILD_INFO.json') | ConvertFrom-Json
    if ($metadata.project -cne 'GTSWindowMod') { throw 'Unexpected project metadata' }
    if ($ExpectedVersion -and $metadata.version -cne $ExpectedVersion) { throw 'Metadata version mismatch' }
    if ($ExpectedCommit -and $metadata.commit -cne $ExpectedCommit) { throw 'Metadata commit mismatch' }
    $hashes = @{}
    foreach ($line in ((Read-ZipText 'SHA256SUMS.txt') -split '\r?\n')) {
        if (-not $line) { continue }
        if ($line -notmatch '^([a-fA-F0-9]{64})  (.+)$') { throw "Invalid checksum line: $line" }
        if ($hashes.ContainsKey($Matches[2])) { throw 'Duplicate checksum path' }
        $hashes[$Matches[2]] = $Matches[1]
    }
    if ($hashes.Count -ne $allowed.Count - 1) { throw 'Checksum coverage mismatch' }
    foreach ($entry in $files) {
        $name = $entry.FullName.Replace('\','/')
        if ($name -ceq 'SHA256SUMS.txt') { continue }
        if (-not $hashes.ContainsKey($name)) { throw "Missing checksum: $name" }
        $stream = $entry.Open(); $sha = [System.Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-','') }
        finally { $sha.Dispose(); $stream.Dispose() }
        if ($actual -ine $hashes[$name]) { throw "Checksum mismatch: $name" }
    }
    Write-Output "Verified package: $($metadata.project) $($metadata.version), commit=$($metadata.commit), $($files.Count) files, all checksums match."
} finally { $archive.Dispose() }
