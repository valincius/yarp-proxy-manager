# Traffic soak smoke for YARP Proxy Manager diagnostics.
#
# Fires N requests through the proxy port with a given Host header and prints the
# diagnostics traffic summary afterwards. Use it to validate that the traffic table,
# recent-requests view, metrics and (optionally) traces reflect real load before
# replacing your existing reverse proxy.
#
# Example:
#   .\scripts\soak-traffic.ps1 -ProxyUrl http://127.0.0.1:5080 -HostHeader app.example.com -Requests 500 -Concurrency 10
#
# Parameters:
#   -ProxyUrl   proxy port base URL        (required)
#   -HostHeader Host header to send        (required)
#   -Requests   total requests             (default 200)
#   -Concurrency concurrent workers        (default 8)
#   -AdminUrl   admin API URL for the summary (default: same host as -ProxyUrl on port 5081)
#   -AdminUser / -AdminPass  admin credentials for the summary (default admin@example.com/admin)

param(
    [Parameter(Mandatory = $true)][string]$ProxyUrl,
    [Parameter(Mandatory = $true)][string]$HostHeader,
    [int]$Requests = 200,
    [int]$Concurrency = 8,
    [string]$AdminUrl = '',
    [string]$AdminUser = 'admin@example.com',
    [string]$AdminPass = 'admin'
)

$ErrorActionPreference = 'Stop'

function Invoke-Traffic {
    $perWorker = [Math]::Ceiling($Requests / $Concurrency)
    $jobs = 1..$Concurrency | ForEach-Object {
        Start-Job -ArgumentList $ProxyUrl, $HostHeader, $perWorker -ScriptBlock {
            param($url, $hostHeader, $count)
            for ($i = 0; $i -lt $count; $i++) {
                try {
                    Invoke-WebRequest -Uri $url -Headers @{ Host = $hostHeader } -UseBasicParsing `
                        -TimeoutSec 10 -ErrorAction SilentlyContinue | Out-Null
                } catch { }
            }
        }
    }
    $jobs | Wait-Job | Remove-Job
}

Write-Host "Soaking $Requests requests to $HostHeader via $ProxyUrl (concurrency $Concurrency)…"
$sw = [System.Diagnostics.Stopwatch]::StartNew()
Invoke-Traffic
$sw.Stop()
Write-Host "Done in $([math]::Round($sw.Elapsed.TotalSeconds, 1))s."

if (-not $AdminUrl) {
    $uri = [Uri]$ProxyUrl
    $AdminUrl = "http://$($uri.Host):5081"
}

# Summarize via the diagnostics API.
$xsrf = (Invoke-RestMethod -Uri "$AdminUrl/api/v1/auth/antiforgery").token
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod -Uri "$AdminUrl/api/v1/auth/login" -Method Post -WebSession $session `
    -Headers @{ 'X-XSRF-TOKEN' = $xsrf } -ContentType 'application/json' `
    -Body (@{ email = $AdminUser; password = $AdminPass } | ConvertTo-Json) | Out-Null
$rotated = (Invoke-RestMethod -Uri "$AdminUrl/api/v1/auth/antiforgery" -WebSession $session).token

$traffic = Invoke-RestMethod -Uri "$AdminUrl/api/v1/diagnostics/traffic?window=1m" `
    -WebSession $session -Headers @{ 'X-XSRF-TOKEN' = $rotated }
$overview = Invoke-RestMethod -Uri "$AdminUrl/api/v1/diagnostics/overview" `
    -WebSession $session -Headers @{ 'X-XSRF-TOKEN' = $rotated }

Write-Host "`nDiagnostics after soak:"
Write-Host ("  total requests (boot): {0}   failed: {1}   tracked hosts: {2}" -f `
    $overview.totalRequests, $overview.totalFailed, $overview.trackedHosts)
$traffic | Format-Table host, requests, class2xx, class4xx, class5xx, averageMs, p95Ms, lastError -AutoSize
