# Benchmark runner: YARP Proxy Manager vs plain Nginx (and optionally NPM).
# Requires Docker. Run from the benchmarks/ directory:
#   ./run.ps1 [-DurationSec 30] [-IncludeNpm]
param(
    [int]$DurationSec = 30,
    [switch]$IncludeNpm
)

# Docker writes progress to stderr; with EAP=Stop that would throw on PS 5.1.
# We use explicit readiness checks instead.
$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $here

function Invoke-Docker {
    & docker @args 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

Write-Host "==> Starting benchmark services"
if ($IncludeNpm) {
    Invoke-Docker compose --profile npm up -d upstream yarp nginx npm
} else {
    Invoke-Docker compose up -d upstream yarp nginx
}

Write-Host "==> Waiting for services"
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/auth/antiforgery" -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
}
if (-not $ready) { throw "YARP admin did not become ready" }
Write-Host "    YARP admin ready"

# Configure the YARP host: yarp.local -> upstream:80 (idempotent)
$xsrf1 = (Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/auth/antiforgery" -SessionVariable sess -UseBasicParsing).Content | ConvertFrom-Json
$login = @{ email = 'admin@example.com'; password = 'benchmark' } | ConvertTo-Json
Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/auth/login" -Method Post -Body $login -ContentType 'application/json' -WebSession $sess -Headers @{ 'X-XSRF-TOKEN' = $xsrf1.token } -UseBasicParsing | Out-Null
$xsrf2 = (Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/auth/antiforgery" -WebSession $sess -UseBasicParsing).Content | ConvertFrom-Json
$existingHosts = (Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/hosts" -WebSession $sess -UseBasicParsing).Content | ConvertFrom-Json
if (-not ($existingHosts | Where-Object { $_.name -eq 'bench' })) {
    $hostBody = @{
        name = 'bench'; domainNames = @('yarp.local'); enabled = $true; scheme = 'http'
        forwardHost = 'upstream'; forwardPort = 80; webSocketsEnabled = $true
        blockCommonExploits = $true; forceHttps = $false; http2Support = $true
        certificateId = $null; accessListId = $null; requestHeaders = @(); responseHeaders = @()
        locations = @(); destinations = @(); loadBalancingPolicy = $null
        healthCheckEnabled = $false; healthCheckPath = $null; healthCheckIntervalSeconds = 10
    } | ConvertTo-Json
    Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/hosts" -Method Post -Body $hostBody -ContentType 'application/json' -WebSession $sess -Headers @{ 'X-XSRF-TOKEN' = $xsrf2.token } -UseBasicParsing | Out-Null
}
Write-Host "    YARP host 'bench' configured (yarp.local -> upstream:80)"

# Warm up
Invoke-WebRequest -Uri "http://127.0.0.1:18080/hello" -Headers @{ Host = 'yarp.local' } -UseBasicParsing -TimeoutSec 5 | Out-Null
Invoke-WebRequest -Uri "http://127.0.0.1:18082/hello" -UseBasicParsing -TimeoutSec 5 | Out-Null
Start-Sleep -Seconds 2

$targets = @(
    @{ Name = 'YARP';    Target = 'http://yarp:80/hello';    Host = 'yarp.local' },
    @{ Name = 'Nginx';   Target = 'http://nginx:80/hello';   Host = $null }
)
if ($IncludeNpm) {
    $targets += @{ Name = 'NPM'; Target = 'http://npm:80/hello'; Host = $null }
}

$vus = @(10, 50, 100, 200)
$results = @()

foreach ($vu in $vus) {
    foreach ($t in $targets) {
        Write-Host "==> Load test: $($t.Name) @ $vu VUs (${DurationSec}s)"
        $outFile = "k6/out/$($t.Name.ToLower())-$vu.json"
        $envArgs = @('compose', '--profile', 'loadgen', 'run', '--rm', 'k6', 'run')
        $envArgs += "-e", "TARGET=$($t.Target)", "-e", "VUS=$vu"
        if ($t.Host) { $envArgs += "-e", "HOST=$($t.Host)" }
        $envArgs += '--vus', "$vu", '--duration', "${DurationSec}s", '--summary-export', "/scripts/out/$($t.Name.ToLower())-$vu.json", '/scripts/script.js'
        & docker $envArgs 2>&1 | Out-Null

        $summary = Get-Content -Raw "$here/$outFile" | ConvertFrom-Json
        $rate = $summary.metrics.'http_reqs'.rate
        $dur = $summary.metrics.'http_req_duration'
        $results += [pscustomobject]@{
            VUs = $vu
            Target = $t.Name
            RPS = [math]::Round([double]$rate)
            P90Ms = [math]::Round([double]$dur.'p(90)', 2)
            P95Ms = [math]::Round([double]$dur.'p(95)', 2)
            AvgMs = [math]::Round([double]$dur.avg, 2)
        }
    }
}

Write-Host ""
Write-Host "==== RESULTS (this machine, Docker, ${DurationSec}s per run) ===="
$results | Format-Table VUs, Target, RPS, AvgMs, P90Ms, P95Ms -AutoSize
$results | Export-Csv -Path "$here/results.csv" -NoTypeInformation
Write-Host "Saved to benchmarks/results.csv"

# Clean up the configured host so reruns are idempotent
Invoke-WebRequest -Uri "http://127.0.0.1:18081/api/v1/hosts" -WebSession $sess -UseBasicParsing | Out-Null
Write-Host "Done."
