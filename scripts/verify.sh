#!/usr/bin/env bash
# Compila y prueba Nexo, y deja los resultados en .verify/.
# Equivalente a scripts/verify.ps1 para Linux y macOS.
set -uo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
out="$root/.verify"
mkdir -p "$out"
: > "$out/summary.txt"
: > "$out/build-errors.txt"
: > "$out/test-failures.txt"

log() { echo "$1"; echo "$1" >> "$out/summary.txt"; }

log "== nexo verify == $(date '+%Y-%m-%d %H:%M:%S')"

if ! command -v dotnet >/dev/null 2>&1; then
  log "SDK: dotnet no encontrado en PATH"
  exit 1
fi

sdk="$(dotnet --version)"
log "SDK: $sdk"
case "$sdk" in
  10.*) ;;
  *) log "AVISO: los proyectos apuntan a net10.0 y este SDK es $sdk (fallara con NETSDK1045)." ;;
esac

# NexoArtifactsPath (definido por el servicio 'verify' de docker compose) redirige
# obj/ y bin/ a un volumen persistente, a proposito, para no mezclar rutas de NuGet
# de Linux con un build posterior en Windows (ver Directory.Build.props). Pero eso
# significa que si una corrida anterior se interrumpe a medio compilar (el
# contenedor se detiene, la maquina se suspende), el volumen puede quedar con
# archivos generados a medio escribir -- y el sintoma es exactamente este: errores
# CS0579 "Duplicate ... attribute" en AssemblyInfo.cs/AssemblyAttributes.cs
# autogenerados, que no tienen nada que ver con el codigo fuente y que ningun
# `dotnet build` posterior corrige solo mientras ese volumen exista. Como este
# script existe para dar una respuesta autoritativa ("esto compila y pasa"), un
# build desde cero en cada corrida es lo correcto, no una optimizacion opcional.
if [ -n "${NexoArtifactsPath:-}" ] && [ -d "$NexoArtifactsPath" ]; then
  log "limpiando obj/bin cacheados en \$NexoArtifactsPath antes de compilar (evita CS0579 por una corrida anterior interrumpida a medias)"
  rm -rf "${NexoArtifactsPath:?}"/obj "${NexoArtifactsPath:?}"/bin
fi

# Este proyecto no versiona migraciones de EF Core a proposito: en desarrollo
# y en los tests de integracion, el esquema se crea desde el modelo actual
# (EnsureCreatedAsync en Program.cs) -- ver README.md y docs/deployment.md.
# El chequeo de "se puede generar una migracion" al final de este script SI
# genera una carpeta Migrations/ real y la deja en el arbol de trabajo (no
# esta en .gitignore ni se commitea). Si una corrida anterior la genero contra
# un modelo mas viejo y el modelo cambio desde entonces (columnas/entidades
# nuevas en cualquier entregable posterior), Program.cs pasa de EnsureCreated
# a MigrateAsync porque detecta migraciones en el ensamblado, y esa migracion
# vieja ya no coincide con el modelo -- EF Core lo trata como error
# (PendingModelChangesWarning), no como advertencia, y el host de pruebas ni
# siquiera arranca: TODOS los tests de integracion fallan de una, sin relacion
# con lo que cada test prueba. Por eso se limpia antes de compilar/probar (deja
# el arbol en el estado real del proyecto) y otra vez al final (para que la
# proxima corrida no herede una migracion generada por esta).
#
# Ruta absoluta a proposito (no relativa a "src/..."): este bloque corre
# ANTES del `cd "$root/backend"` de mas abajo, asi que si se usara una ruta
# relativa aca, se evaluaria contra el directorio desde el que se invoco el
# script (por ejemplo la raiz del repo dentro del contenedor docker), no
# contra backend/ -- el `[ -d ... ]` daria falso aunque la carpeta sí exista,
# y la limpieza nunca se ejecutaria de verdad. Esto paso en la practica: la
# corrida de las 18:19 UTC siguio mostrando "tests: FALLO" con la misma
# Migrations/ de las 12:53 sin tocar, pese a este fix ya sincronizado.
migrations_dir="$root/backend/src/Nexo.Infrastructure/Migrations"
if [ -d "$migrations_dir" ]; then
  log "eliminando Migrations/ generada por una corrida anterior (evita PendingModelChangesWarning si el modelo cambio desde entonces)"
  rm -rf "$migrations_dir"
fi

cd "$root/backend"

if ! dotnet restore Nexo.sln > "$out/restore.log" 2>&1; then
  log "restore: FALLO -> .verify/restore.log"
  exit 1
fi
log "restore: OK"

if ! dotnet build Nexo.sln --configuration Debug --no-restore > "$out/build.log" 2>&1; then
  grep -E ': error [A-Z]+[0-9]+' "$out/build.log" | sort -u > "$out/build-errors.txt"
  log "build: FALLO con $(wc -l < "$out/build-errors.txt") errores unicos -> .verify/build-errors.txt"
  exit 1
fi
log "build: OK"

if dotnet test Nexo.sln --configuration Debug --no-build --verbosity normal > "$out/test.log" 2>&1; then
  log "tests: OK"
else
  grep -E '^\s*(Failed|Error Message|Assert\.|\s+at Nexo)' "$out/test.log" | head -200 > "$out/test-failures.txt"
  log "tests: FALLO -> .verify/test-failures.txt"
fi
grep -E 'Passed!|Failed!|total:' "$out/test.log" | sort -u | while read -r line; do log "tests: $line"; done

if [ ! -d "$migrations_dir" ]; then
  dotnet tool restore > "$out/tool-restore.log" 2>&1
  if dotnet ef migrations add InitialCreate \
       --project src/Nexo.Infrastructure --startup-project src/Nexo.Api > "$out/migration.log" 2>&1; then
    log "migration: InitialCreate creada (solo como smoke test -- se borra abajo, ver comentario mas arriba)"
  else
    log "migration: FALLO -> .verify/migration.log"
  fi
  # Se borra de nuevo aca mismo: esta carpeta es solo un smoke test de "se puede
  # generar una migracion", no algo que el proyecto versione (ver el comentario
  # extenso mas arriba). Si se deja en el arbol de trabajo, la proxima corrida la
  # hereda y, en cuanto el modelo cambie, revienta todos los tests de integracion
  # con PendingModelChangesWarning antes de que lleguen a correr un solo Assert.
  rm -rf "$migrations_dir"
else
  log "migration: ya existe"
fi

log "== fin =="
