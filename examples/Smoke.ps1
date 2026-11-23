param([string]$ExpectedCommit)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
$previousCache = $env:NUGET_PACKAGES
try {
    if (-not $ExpectedCommit) {
        $ExpectedCommit = git rev-parse HEAD
        if ($LASTEXITCODE -ne 0) { throw 'Cannot determine the expected Git commit.' }
    }
    $runDirectory = Join-Path $root ('artifacts/smoke-' + [guid]::NewGuid().ToString('N'))
    $toolDirectory = Join-Path $runDirectory 'tool'
    $fixtures = Join-Path $runDirectory 'fixtures'
    $env:NUGET_PACKAGES = Join-Path $runDirectory 'packages'
    dotnet tool install NupkgReleaseCheck.Tool --version 0.2.0 --tool-path $toolDirectory --configfile nuget.local.config --no-cache
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
    $policyPath = Join-Path $fixtures 'policy.json'
    $policyHash = (Get-FileHash $policyPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $policyReport = & $tool $good --policy $policyPath --strict --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $policyReport.requiredFilesChecked -ne 2 -or $policyReport.fileHashesChecked -ne 1 -or $policyReport.policySha256 -ne $policyHash) { throw 'Positive policy failed.' }
    foreach ($case in @(@{name='changed';code='NRC009'},@{name='missing';code='NRC008'})) {
        $candidate = Join-Path $fixtures ($case.name + '.nupkg')
        $candidateBefore = (Get-FileHash $candidate -Algorithm SHA256).Hash
        $rejected = & $tool $candidate --expect-commit ('a' * 40) --policy $policyPath --format json | ConvertFrom-Json
        if ($LASTEXITCODE -ne 1 -or $rejected.diagnostics.Count -ne 1 -or $rejected.diagnostics[0].code -ne $case.code -or $rejected.repositoryCommit -ne ('a' * 40)) { throw 'Policy rejection failed.' }
        if ((Get-FileHash $candidate -Algorithm SHA256).Hash -ne $candidateBefore) { throw 'Policy changed source.' }
        & $tool $candidate --strict > (Join-Path $runDirectory ($case.name + '-default.txt'))
        if ($LASTEXITCODE -ne 0) { throw 'Policy-free compatibility failed.' }
    }
    $invalidPolicy = Join-Path $runDirectory 'invalid-policy.json'
    '{"schemaVersion":1,"requiredFiles":[{"path":"README.md","typo":true}]}' | Set-Content -LiteralPath $invalidPolicy -Encoding utf8
    $invalidOutput = & $tool $good --policy $invalidPolicy --format json 2> (Join-Path $runDirectory 'policy-error.txt')
    if ($LASTEXITCODE -ne 2 -or $invalidOutput) { throw 'Invalid policy error contract failed.' }
    if ((Get-FileHash $good -Algorithm SHA256).Hash.ToLowerInvariant() -ne $before -or (Get-FileHash $policyPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $policyHash) { throw 'Package/policy preservation failed.' }
    & $tool $good --format unsupported 2> (Join-Path $runDirectory 'argument-error.txt')
    if ($LASTEXITCODE -ne 2) { throw 'Argument error contract failed.' }
    $package = Join-Path $root 'artifacts/NupkgReleaseCheck.Tool.0.2.0.nupkg'
    $selfPolicy = Join-Path $runDirectory 'self-policy.json'
    @{schemaVersion=1;requiredFiles=@(@{path='README.md'},@{path='docs/POLICY.md'},@{path='tools/net10.0/any/NupkgReleaseCheck.dll';sha256=(Get-FileHash (Join-Path $root 'src/Cli/bin/Release/net10.0/NupkgReleaseCheck.dll') -Algorithm SHA256).Hash})} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $selfPolicy -Encoding utf8
    $self = & $tool $package --expect-id NupkgReleaseCheck.Tool --expect-version 0.2.0 --expect-commit $ExpectedCommit --policy $selfPolicy --strict --format json | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $self.errors -ne 0 -or $self.warnings -ne 0) { throw 'Final package inspection failed.' }
    $self | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $runDirectory 'self-inspection.json') -Encoding utf8
    Write-Output ('Installed-tool smoke passed; evidence: ' + $runDirectory)
    $global:LASTEXITCODE = 0
} finally { $env:NUGET_PACKAGES = $previousCache; Pop-Location }
