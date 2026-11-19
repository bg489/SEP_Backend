# Compatibility entry point. The worker follows the map of each queued run.
# This old filename accepts the same arguments as run_worker.ps1.
& (Join-Path $PSScriptRoot "run_worker.ps1") @args
exit $LASTEXITCODE
