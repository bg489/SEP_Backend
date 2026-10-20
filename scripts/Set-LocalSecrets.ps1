$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
Push-Location $projectRoot

try {
    $postgresHost = Read-Host "PostgreSQL host [localhost]"
    if ([string]::IsNullOrWhiteSpace($postgresHost)) { $postgresHost = "localhost" }

    $postgresPort = Read-Host "PostgreSQL port [5432]"
    if ([string]::IsNullOrWhiteSpace($postgresPort)) { $postgresPort = "5432" }

    $postgresUser = Read-Host "PostgreSQL username [postgres]"
    if ([string]::IsNullOrWhiteSpace($postgresUser)) { $postgresUser = "postgres" }

    $postgresPasswordSecure = Read-Host "PostgreSQL password" -AsSecureString
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($postgresPasswordSecure)
    try {
        $postgresPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
        $escapedUser = $postgresUser.Replace('"', '""')
        $escapedPassword = $postgresPassword.Replace('"', '""')
        $authConnectionString = "Host=$postgresHost;Port=$postgresPort;Database=autoscenariox;Username=`"$escapedUser`";Password=`"$escapedPassword`""
        dotnet user-secrets set "ConnectionStrings:AuthDatabase" $authConnectionString
        if ($LASTEXITCODE -ne 0) { throw "Could not save the PostgreSQL connection string." }
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
        $postgresPasswordSecure.Dispose()
        Remove-Variable postgresPassword, authConnectionString, escapedPassword -ErrorAction SilentlyContinue
    }

    $randomBytes = New-Object byte[] 48
    $randomGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $randomGenerator.GetBytes($randomBytes)
        $jwtSigningKey = [Convert]::ToBase64String($randomBytes)
    }
    finally {
        $randomGenerator.Dispose()
        [Array]::Clear($randomBytes, 0, $randomBytes.Length)
    }
    dotnet user-secrets set "Jwt:SigningKey" $jwtSigningKey
    if ($LASTEXITCODE -ne 0) { throw "Could not save the JWT signing key." }
    Remove-Variable jwtSigningKey, randomBytes, randomGenerator

    $adminEmail = Read-Host "Bootstrap administrator email (leave blank to skip)"
    if (-not [string]::IsNullOrWhiteSpace($adminEmail)) {
        $adminName = Read-Host "Administrator full name"
        if ([string]::IsNullOrWhiteSpace($adminName)) { $adminName = "AutoScenarioX Administrator" }
        dotnet user-secrets set "BootstrapAdmin:Email" $adminEmail.Trim()
        if ($LASTEXITCODE -ne 0) { throw "Could not save the administrator email." }
        dotnet user-secrets set "BootstrapAdmin:FullName" $adminName.Trim()
        if ($LASTEXITCODE -ne 0) { throw "Could not save the administrator name." }

        $adminPasswordSecure = Read-Host "Bootstrap administrator password" -AsSecureString
        $adminPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($adminPasswordSecure)
        try {
            $adminPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($adminPasswordPointer)
            dotnet user-secrets set "BootstrapAdmin:Password" $adminPassword
            if ($LASTEXITCODE -ne 0) { throw "Could not save the administrator password." }
        }
        finally {
            [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($adminPasswordPointer)
            $adminPasswordSecure.Dispose()
            Remove-Variable adminPassword -ErrorAction SilentlyContinue
        }
    }

    Write-Host "Local secrets saved. Run 'dotnet tool restore' and 'dotnet run' from the Backend folder."
}
finally {
    Pop-Location
}
