# Go / No-Go del MVP

Entregable 34, el ultimo del plan de 34. Este documento consolida el estado
real de los entregables como insumo para que Anderson tome la decision. No es
un argumento a favor de lanzar: es un inventario operativo.

## Como leer esta tabla

* **Hecho** = implementado y verificado por lectura manual y/o tests
  automatizados.
* **Preparado; bloqueado por infraestructura** = el repositorio ya tiene
  contrato, variables, documentacion o scripts; falta una cuenta, dominio, host,
  TLS o credencial real que no debe inventarse en codigo.
* **Pendiente de ejecucion humana** = el codigo esta listo, pero falta que una
  persona ejecute el guion con la app real.

Al 2026-09-05, `docker compose --profile tools run --rm verify` corre completo:
restore OK, build OK, tests OK y smoke de migracion `InitialCreate` OK.

| # | Entregable | Estado | Nota |
| --- | --- | --- | --- |
| 1-3 | Fases 0-2 (scaffolding, dominio, aplicacion) | Hecho | |
| 4 | PostgreSQL real | Hecho | API validada contra PostgreSQL local por `docker compose`; el healthcheck responde y el arranque aplica/valida esquema |
| 5 | Suite verde | Hecho | `docker compose --profile tools run --rm verify` ejecutado hasta el final el 2026-09-05: 185 tests passed, 0 failed; restore/build/migration smoke OK |
| 6-23 | Parsers, importacion, movimientos, transferencias, categorizacion, dashboard, insights, notificaciones, push, sesiones, hardening, auditoria, privacidad, preparacion de email | Hecho | |
| 24 | Gmail OAuth | Preparado; bloqueado por credenciales | Variables listas; necesita credenciales reales de Google Cloud Console |
| 25 | Outlook OAuth | Preparado; bloqueado por credenciales | Variables listas; necesita credenciales reales de Microsoft Entra ID |
| 26-28 | Parsers de correo, dedup correo/importacion, observabilidad | Hecho | |
| 29 | CI | Hecho; pendiente primer run GitHub real | `.github/workflows/ci.yml` esta preparado, pero no hay remoto de GitHub configurado para ver un run real |
| 30 | Staging | Preparado; bloqueado por infraestructura | No hay host, dominio ni TLS. Plantilla de entorno y smoke test listos (`.env.staging.example`, `scripts/smoke-staging.sh`) |
| 31 | Mobile builds | Hecho lo que no necesita cuenta real | `eas.json` y URLs por perfil listas. `extra.eas.projectId` sigue en placeholder porque falta cuenta EAS real |
| 32 | QA | Hecho el plan; pendiente de ejecucion humana | `docs/qa-test-plan.md`: 11 guiones por ejecutar con la app real |
| 33 | Beta cerrada | Checklist listo; bloqueada | Depende de staging, EAS, QA manual y decision de audiencia |
| 34 | Go/No-Go MVP | Este documento | La decision es de Anderson |

## Lo que esta solido hoy

* Backend completo para el alcance MVP: registro/login con rotacion de refresh
  tokens, cuentas con saldo verificado vs. estimado, importacion con preview,
  deduplicacion exacta/probable, transferencias internas, categorizacion con
  aprendizaje, insights, notificaciones, push, sesiones/dispositivos,
  hardening, auditoria y privacidad.
* Backend verificado de punta a punta con Docker el 2026-09-05:
  `docker compose --profile tools run --rm verify` dejo `restore: OK`,
  `build: OK`, `tests: OK` y `migration: InitialCreate creada`. La suite
  reporto 185 tests passed, 0 failed.
* Documentacion y preparacion operativa: arquitectura, seguridad, integracion
  de proveedores, mobile, despliegue, ingesta por correo, QA, beta y este
  Go/No-Go.

## Lo que falta y quien lo desbloquea

No quedan bloqueos tecnicos que se resuelvan inventando credenciales. Lo que
falta es externo al repositorio:

1. Credenciales reales de Google Cloud Console y Microsoft Entra ID para Gmail
   OAuth y Outlook OAuth. Las variables ya estan preparadas y pueden quedarse
   vacias hasta que existan.
2. Host de staging con dominio y TLS. El repositorio ya tiene
   `.env.staging.example` y `scripts/smoke-staging.sh`.
3. Cuenta Expo/EAS real y `eas init` para reemplazar
   `extra.eas.projectId = 00000000-0000-0000-0000-000000000000`.
4. Ejecutar los 11 guiones de `docs/qa-test-plan.md` con la app real.
5. Decidir a quien invitar a beta cerrada y si el aviso de privacidad borrador
   necesita revision legal antes de usar datos financieros reales de terceros.

## Deuda tecnica reconocida

Ver `docs/roadmap.md` para el detalle completo: paginacion por offset en vez
de keyset, parsers validados contra exports de ejemplo, workers in-process,
exportador OpenTelemetry pendiente y cifrado en reposo dependiente del proveedor
de base de datos.

## Lo que no es un defecto

Gmail/Outlook pueden permanecer no disponibles si faltan credenciales reales;
la app no debe ofrecer un boton roto. El reenvio de correo funciona cuando hay
dominio entrante y webhook secret configurados. `Nexo:Seed:Demo` no siembra
datos fuera de `Development` a proposito.

## Caminos posibles

**Camino A - uso local / circulo cercano:** viable con `docker compose up -d` y
Expo Go en la misma red. No necesita staging, EAS ni OAuth.

**Camino B - beta cerrada real:** requiere staging + EAS como minimo tecnico,
QA manual como minimo de confianza, y decision de audiencia/privacidad como
minimo de responsabilidad. La suite automatizada ya quedo confirmada en verde
el 2026-09-05.
