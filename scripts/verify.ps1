<#
    Compila y prueba Nexo, y deja los resultados en .verify/ para que puedan
    leerse desde fuera. Una sola pasada; usa watch.ps1 para el bucle.

    Uso:  pwsh -File scripts/verify.ps1        (o  powershell -File ...)
#>

$ErrorActionPreference = 'Continue'

$root   = Split-Path -Parent $PSScriptRoot
$out    = Join-Path $root '.verify'
$back   = Join-Path $root 'backend'
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Save($name, $content) {
    Set-Content -Path (Join-Path $out $name) -Value $content -Encoding UTF8
}

$summary = [System.Collections.ArrayList]::new()
function Log($line) { [void]$summary.Add($line); Write-Host $line }

Log "== nexo verify == $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"

# --- SDK ---------------------------------------------------------------
$sdk = (& dotnet --version) 2>&1 | Out-String
$sdk = $sdk.Trim()
if ($LASTEXITCODE -ne 0) {
    Log "SDK: dotnet no encontrado en PATH"
    Save 'summary.txt' ($summary -join "`n")
    exit 1
}
Log "SDK: $sdk"
if (-not $sdk.StartsWith('10.')) {
    Log "AVISO: los proyectos apuntan a net10.0 y este SDK es $sdk."
    Log "       Instala el SDK de .NET 10 o el build fallara con NETSDK1045."
}

Push-Location $back

# --- restore -----------------------------------------------------------
$restore = (& dotnet restore Nexo.sln) 2>&1 | Out-String
$restoreOk = ($LASTEXITCODE -eq 0)
Save 'restore.log' $restore
Log "restore: $(if ($restoreOk) {'OK'} else {'FALLO -> .verify/restore.log'})"

if (-not $restoreOk) {
    Pop-Location
    Save 'summary.txt' ($summary -join "`n")
    exit 1
}

# --- build -------------------------------------------------------------
$build = (& dotnet build Nexo.sln --configuration Debug --no-restore) 2>&1 | Out-String
$buildOk = ($LASTEXITCODE -eq 0)
Save 'build.log' $build

# Solo los errores, que es lo unico que hace falta leer para corregir.
$errors = ($build -split "`r?`n" | Where-Object { $_ -match ': error [A-Z]+\d+' } | Select-Object -Unique)
Save 'build-errors.txt' ($errors -join "`n")
Log "build: $(if ($buildOk) {'OK'} else {"FALLO con $($errors.Count) errores unicos -> .verify/build-errors.txt"})"

if (-not $buildOk) {
    Pop-Location
    Save 'summary.txt' ($summary -join "`n")
    exit 1
}

# --- tests -------------------------------------------------------------
$test = (& dotnet test Nexo.sln --configuration Debug --no-build --verbosity normal) 2>&1 | Out-String
$testOk = ($LASTEXITCODE -eq 0)
Save 'test.log' $test

$failures = ($test -split "`r?`n" | Where-Object { $_ -match '^\s*(Failed|Error Message|Assert\.|\s+at Nexo)' } | Select-Object -First 200)
Save 'test-failures.txt' ($failures -join "`n")

$totals = ($test -split "`r?`n" | Where-Object { $_ -match 'Passed!|Failed!|total:|Total tests' } | Select-Object -Unique)
foreach ($t in $totals) { Log "tests: $($t.Trim())" }
Log "tests: $(if ($testOk) {'OK'} else {'FALLO -> .verify/test-failures.txt'})"

# --- migration inicial -------------------------------------------------
# Este proyecto no versiona migraciones de EF Core a proposito (ver README.md
# y docs/deployment.md): el esquema de dev/tests se crea desde el modelo
# actual con EnsureCreatedAsync en Program.cs. Este chequeo de "se puede
# generar una migracion" es solo un smoke test y genera una carpeta
# Migrations/ real en el arbol de trabajo. Si se deja ahi, la proxima corrida
# la hereda, y en cuanto el modelo cambie (una entidad/columna nueva),
# Program.cs detecta migraciones en el ensamblado y pasa de EnsureCreated a
# MigrateAsync -- que revienta con PendingModelChangesWarning porque esa
# migracion vieja ya no coincide con el modelo, y el host de pruebas ni
# siquiera arranca (fallan TODOS los tests de integracion, sin relacion con
# lo que cada uno prueba). Por eso se limpia antes de la corrida (deja el
# arbol en el estado real del proyecto) y otra vez despues del smoke test
# (para que la proxima corrida no la herede). Ver scripts/verify.sh para el
# equivalente en bash, incluyendo el porque con mas detalle.
$migrations = Join-Path $back 'src\Nexo.Infrastructure\Migrations'
if (Test-Path $migrations) {
    Log "eliminando Migrations/ generada por una corrida anterior (evita PendingModelChangesWarning si el modelo cambio desde entonces)"
    Remove-Item -Recurse -Force $migrations
}

if ($buildOk -and -not (Test-Path $migrations)) {
    $tools = (& dotnet tool restore) 2>&1 | Out-String
    Save 'tool-restore.log' $tools

    $mig = (& dotnet ef migrations add InitialCreate --project src\Nexo.Infrastructure --startup-project src\Nexo.Api) 2>&1 | Out-String
    Save 'migration.log' $mig
    Log "migration: $(if (Test-Path $migrations) {'InitialCreate creada (solo como smoke test -- se borra abajo)'} else {'FALLO -> .verify/migration.log'})"

    if (Test-Path $migrations) {
        Remove-Item -Recurse -Force $migrations
    }
} elseif (Test-Path $migrations) {
    Log "migration: ya existe"
}

Pop-Location
Save 'summary.txt' ($summary -join "`n")
Log "== fin =="
