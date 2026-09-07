<#
    Lanza verify.ps1 en bucle. Ejecutalo UNA vez y dejalo abierto:
    cada vez que cambien los archivos, vuelve a compilar y actualiza .verify/.

    Uso:  pwsh -File scripts/watch.ps1
    Salir: Ctrl+C
#>

$root = Split-Path -Parent $PSScriptRoot
$verify = Join-Path $PSScriptRoot 'verify.ps1'
$lastHash = ''

Write-Host "Vigilando cambios en backend/. Ctrl+C para salir." -ForegroundColor Cyan

while ($true) {
    # Huella de las fuentes: si no cambio nada, no recompila.
    $files = Get-ChildItem -Path (Join-Path $root 'backend') -Recurse -Include *.cs,*.csproj,*.props,*.json -ErrorAction SilentlyContinue |
             Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }
    $hash = ($files | ForEach-Object { "$($_.FullName)|$($_.LastWriteTimeUtc.Ticks)" }) -join ';'
    $hash = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::Create().ComputeHash(
            [System.Text.Encoding]::UTF8.GetBytes($hash)))

    if ($hash -ne $lastHash) {
        $lastHash = $hash
        Write-Host "`n--- cambios detectados, verificando ---" -ForegroundColor Yellow
        & $verify
    }

    Start-Sleep -Seconds 5
}
