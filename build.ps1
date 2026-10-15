param(
    [Parameter(Mandatory=$true)][string]$PKHeXDirectory,
    [switch]$Test,
    [switch]$TestUI
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$targetFolder = (Resolve-Path -LiteralPath $PKHeXDirectory).Path
$coreFile = Join-Path $targetFolder 'PKHeX.Core.dll'
if (!(Test-Path -LiteralPath $coreFile)) { throw 'No se encontró PKHeX.Core.dll.' }
$lockInfo = Get-Content -LiteralPath (Join-Path $projectRoot 'dependencies\dependency-lock.json') -Raw | ConvertFrom-Json
$coreVersion = [System.Reflection.AssemblyName]::GetAssemblyName($coreFile).Version.ToString()
$coreHash = (Get-FileHash -LiteralPath $coreFile -Algorithm SHA256).Hash
if ($coreVersion -ne $lockInfo.pkhexAssemblyVersion) {
    throw "La versión comprobada es $($lockInfo.pkhexAssemblyVersion), pero se encontró $coreVersion. Actualiza y prueba la compatibilidad antes de cambiar dependency-lock.json."
}
if ($coreHash -ne $lockInfo.coreSha256) {
    Write-Warning 'Coincide la versión, pero el Core no es idéntico al usado en validación. Repite las pruebas con esta compilación.'
}
Copy-Item -LiteralPath $coreFile -Destination (Join-Path $projectRoot 'dependencies\PKHeX.Core.dll') -Force
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
$env:MSBuildEnableWorkloadResolver = 'false'
Push-Location $projectRoot
try {
    dotnet build LanguageDexGenerator -c Release --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación del plugin.' }
    if ($Test -or $TestUI) {
        $hostFolder = Join-Path $projectRoot 'dependencies\host'
        New-Item -ItemType Directory -Path $hostFolder -Force | Out-Null
        Copy-Item -Path (Join-Path $targetFolder '*.dll') -Destination $hostFolder -Force
        dotnet build tests\IntegrationTests -c Release --ignore-failed-sources
        if ($LASTEXITCODE -ne 0) { throw 'Falló la compilación de pruebas.' }
        dotnet tests\IntegrationTests\bin\Release\net10.0-windows\IntegrationTests.dll
        if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas de integración.' }
    }
    if ($TestUI) {
        dotnet tests\IntegrationTests\bin\Release\net10.0-windows\IntegrationTests.dll --ui
        if ($LASTEXITCODE -ne 0) { throw 'Falló la prueba de interfaz.' }
    }
    Write-Output (Join-Path $projectRoot 'LanguageDexGenerator\bin\Release\net10.0-windows\LanguageDexGenerator.dll')
} finally { Pop-Location }
