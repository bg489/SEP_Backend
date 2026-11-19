$ErrorActionPreference = "Stop"
$backendRoot = Split-Path -Parent $PSScriptRoot
$carlaRoot = if ([string]::IsNullOrWhiteSpace($env:CARLA_ROOT)) { "C:\Users\admin\Downloads\CARLA_0.9.16" } else { $env:CARLA_ROOT }
$runnerRoot = Join-Path $carlaRoot "ScenarioRunner"
$runnerScript = Join-Path $runnerRoot "scenario_runner.py"
$pythonExe = "C:\Users\admin\AppData\Local\Python\pythoncore-3.12-64\python.exe"
$pythonLauncher = Get-Command py -ErrorAction SilentlyContinue
if (-not (Test-Path -LiteralPath $pythonExe) -and $pythonLauncher) {
    & $pythonLauncher.Source -3.12 -c "import sys; print(sys.executable)" *> $null
    if ($LASTEXITCODE -eq 0) { $pythonExe = (& $pythonLauncher.Source -3.12 -c "import sys; print(sys.executable)").Trim() }
}

if (-not (Test-Path -LiteralPath (Join-Path $carlaRoot "CarlaUE4.exe")) -and
    -not (Test-Path -LiteralPath (Join-Path $carlaRoot "CarlaUE4\Binaries\Win64\CarlaUE4-Win64-Shipping.exe"))) {
    throw "CARLA 0.9.16 executable was not found under $carlaRoot"
}
if (-not (Test-Path -LiteralPath $pythonExe)) { throw "Python 3.12 is required because this CARLA package includes its Python client wheel for CPython 3.12." }
if (-not (Test-Path -LiteralPath $runnerScript)) {
    git clone --depth 1 --branch v0.9.16 https://github.com/carla-simulator/scenario_runner.git $runnerRoot
    if ($LASTEXITCODE -ne 0) { throw "Could not download the official ScenarioRunner v0.9.16 release." }
}

$wheel = Get-ChildItem -LiteralPath (Join-Path $carlaRoot "PythonAPI\carla\dist") -File -Filter "carla-0.9.16-cp312-*.whl" | Select-Object -First 1
if (-not $wheel) { throw "The CARLA 0.9.16 CPython 3.12 wheel was not found under PythonAPI\carla\dist." }
$venvPython = Join-Path $runnerRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) {
    & $pythonExe -m venv (Join-Path $runnerRoot ".venv")
    if ($LASTEXITCODE -ne 0) { throw "Could not create the ScenarioRunner Python environment." }
}
& $venvPython -m pip install --disable-pip-version-check --upgrade pip
if ($LASTEXITCODE -ne 0) { throw "Could not update pip in the ScenarioRunner environment." }
& $venvPython -m pip install --disable-pip-version-check -r (Join-Path $backendRoot "scripts\ScenarioRunner.Python312.requirements.txt") $wheel.FullName
if ($LASTEXITCODE -ne 0) { throw "Could not install ScenarioRunner dependencies and the matching CARLA client wheel." }
$env:CARLA_ROOT = $carlaRoot
$env:SCENARIO_RUNNER_ROOT = $runnerRoot
$pythonPaths = @((Join-Path $carlaRoot "PythonAPI\carla"), (Join-Path $carlaRoot "PythonAPI"), $runnerRoot)
if (-not [string]::IsNullOrWhiteSpace($env:PYTHONPATH)) { $pythonPaths += $env:PYTHONPATH }
$env:PYTHONPATH = $pythonPaths -join [IO.Path]::PathSeparator
& $venvPython $runnerScript --version
if ($LASTEXITCODE -ne 0) { throw "ScenarioRunner did not pass its version startup check." }
Write-Host "ScenarioRunner 0.9.16 is ready at $runnerRoot"
