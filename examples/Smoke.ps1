param([string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    if (-not $ExpectedCommit) {
        $ExpectedCommit = git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the expected Git commit.' }
    }
    $runDirectory = Join-Path $root ('artifacts/smoke-' + [guid]::NewGuid().ToString('N'))
    $toolDirectory = Join-Path $runDirectory 'tool'
    $fixtures = Join-Path $runDirectory 'fixtures'
    dotnet tool install NupkgReleaseCheck.Tool --version 0.1.0 --tool-path $toolDirectory --configfile nuget.local.config --no-cache
    if ($LASTEXITCODE -ne 0) { throw 'Local tool install failed.' }
    dotnet run --project examples/Demo -c Release -- $fixtures
    if ($LASTEXITCODE -ne 0) { throw 'Fixture generation failed.' }
    $tool = Join-Path $toolDirectory 'nupkg-release-check'
    $good = Join-Path $fixtures 'good.nupkg'
    $before = (Get-FileHash $good -Algorithm SHA256).Hash.ToLowerInvariant()
    $report = & $tool $good --expect-id Demo.Tool --expect-version 1.2.3 --expect-commit ('a' * 40) --strict --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $report.errors -ne 0 -or $report.warnings -ne 0 -or $report.localLinksChecked -ne 2 -or $report.packageSha256 -ne $before) { throw 'Positive fixture failed.' }
    if ((Get-FileHash $good -Algorithm SHA256).Hash.ToLowerInvariant() -ne $before) { throw 'Source package changed.' }
    $bad = & $tool (Join-Path $fixtures 'bad.nupkg') --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 1 -or $bad.diagnostics[0].code -ne 'NRC006' -or $bad.diagnostics[0].line -ne 3) { throw 'Negative fixture failed.' }
    & $tool $good --format unsupported 2> (Join-Path $runDirectory 'argument-error.txt')
    if ($LASTEXITCODE -ne 2) { throw 'Argument error contract failed.' }
    $package = Join-Path $root 'artifacts/NupkgReleaseCheck.Tool.0.1.0.nupkg'
    $self = & $tool $package --expect-id NupkgReleaseCheck.Tool --expect-version 0.1.0 --expect-commit $ExpectedCommit --strict --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $self.errors -ne 0 -or $self.warnings -ne 0) { throw 'Final package inspection failed.' }
    $self | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $runDirectory 'self-inspection.json') -Encoding utf8
    Write-Output ('Installed-tool smoke passed; evidence: ' + $runDirectory)
} finally { Pop-Location }
