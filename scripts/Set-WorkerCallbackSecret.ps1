$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot
try {
    $randomBytes = New-Object byte[] 48
    $randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomGenerator.GetBytes($randomBytes)
        $workerKey = [Convert]::ToBase64String($randomBytes)
    }
    finally {
        $randomGenerator.Dispose()
        [Array]::Clear($randomBytes, 0, $randomBytes.Length)
    }
    dotnet user-secrets set "Worker:CallbackKey" $workerKey
    if ($LASTEXITCODE -ne 0) { throw "Could not save Worker:CallbackKey." }
    Remove-Variable workerKey, randomBytes, randomGenerator
    Write-Host "Worker callback key saved to .NET User Secrets."
}
finally {
    Pop-Location
}
