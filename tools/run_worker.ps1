# AutoScenarioX multi-map worker launcher. Compatible with Windows PowerShell 5.1.
# No database changes, package installation or simulator startup occur with -ListMaps.
$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$carlaRoot = if ($env:CARLA_ROOT) { $env:CARLA_ROOT } else { "C:\Users\admin\Downloads\CARLA_0.9.16" }
$apiAddress = if ($env:ASX_API) { $env:ASX_API } else { "http://localhost:5080" }
$quick = $false
$listMaps = $false
$forward = @()

try {
    for ($index = 0; $index -lt $args.Count; $index++) {
        $argument = [string]$args[$index]
        switch ($argument.ToLowerInvariant()) {
            "-quick" { $quick = $true }
            "--quick" { $quick = $true }
            "-full" { $quick = $false }
            "--full" { $quick = $false }
            "-listmaps" { $listMaps = $true }
            "--list-maps" { $listMaps = $true }
            "--carla-root" {
                if ($index + 1 -ge $args.Count) { throw "--carla-root requires the CARLA installation directory." }
                $index++
                $carlaRoot = [string]$args[$index]
            }
            "--api" {
                if ($index + 1 -ge $args.Count) { throw "--api requires the backend base URL." }
                $index++
                $apiAddress = [string]$args[$index]
            }
            default { $forward += $argument }
        }
    }

    $carlaRoot = [IO.Path]::GetFullPath($carlaRoot)
    $env:CARLA_ROOT = $carlaRoot
    $env:ASX_API = $apiAddress
    $env:PYTHONIOENCODING = "utf-8"
    # Previous Town10 launchers left this environment variable behind. A map is
    # now selected from each immutable execution package, never from that default.
    Remove-Item Env:\ASX_FIXED_MAP -ErrorAction SilentlyContinue

    $pythonExecutable = $null
    $pythonPrefixArgs = @()
    if ($env:ASX_PYTHON) {
        if (-not (Test-Path -LiteralPath $env:ASX_PYTHON -PathType Leaf)) {
            throw "ASX_PYTHON does not point to an existing Python executable: $env:ASX_PYTHON"
        }
        $pythonExecutable = [IO.Path]::GetFullPath($env:ASX_PYTHON)
    }
    else {
        $candidates = @(
            (Join-Path $carlaRoot "ScenarioRunner\.venv\Scripts\python.exe"),
            (Join-Path $repositoryRoot ".venv\Scripts\python.exe")
        )
        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                $pythonExecutable = $candidate
                break
            }
        }
        if (-not $pythonExecutable) {
            $pythonLauncher = Get-Command py.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($pythonLauncher) {
                $pythonExecutable = $pythonLauncher.Source
                $pythonPrefixArgs = @("-3.12")
            }
            else {
                $pythonCommand = Get-Command python.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
                if ($pythonCommand) { $pythonExecutable = $pythonCommand.Source }
            }
        }
    }
    if (-not $pythonExecutable) {
        throw "Python was not found. Set ASX_PYTHON to an existing environment with carla==0.9.16 and the CARLA PythonAPI dependencies."
    }

    $workerArgs = @(
        "-u", (Join-Path $PSScriptRoot "campaign_worker.py"),
        "--api", $apiAddress,
        "--carla-root", $carlaRoot,
        "--poll", "3",
        "--max-infra-retries", "3",
        "--carla-boot-timeout", "180",
        "--out", (Join-Path $repositoryRoot "recordings")
    )
    if ($quick) { $workerArgs += "--quick" }
    else { $workerArgs += @("--cooldown", "15") }
    if ($listMaps) { $workerArgs += "--list-maps" }
    $workerArgs += $forward

    Write-Host ""
    Write-Host "=== ASX Worker - all installed scenario maps ==="
    Write-Host "API   = $apiAddress"
    Write-Host "CARLA = $carlaRoot"
    Write-Host "Python= $pythonExecutable"
    Write-Host "Quality is Low. Each queued run selects its own map."
    if ($listMaps) {
        Write-Host "Inventory only: no queue claim and no CARLA process."
    }
    elseif ($quick) {
        Write-Host "Quick mode: at most 40 simulation seconds per scenario."
    }
    else {
        Write-Host "Full mode: finish at the goal, scenario timeout or configured violation stop."
    }
    Write-Host ""

    Push-Location -LiteralPath $repositoryRoot
    try {
        & $pythonExecutable @pythonPrefixArgs @workerArgs
        $workerExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    exit $workerExitCode
}
catch {
    Write-Host ("Worker launcher failed: " + $_.Exception.Message) -ForegroundColor Red
    exit 1
}
