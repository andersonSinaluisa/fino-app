# Roadmap

## MVP — implementado

* Registro e inicio de sesión (access + refresh con rotación y detección de reuso)
* Cuentas financieras con saldo verificado y estimado, explícitamente distinguidos
* Importación de CSV/XLSX con arquitectura de parsers y preview antes de escribir
* Modelo canónico de transacciones, único para todos los canales
* Deduplicación exacta y probable, sin borrar información por decisión propia
* Categorías por reglas + aprendizaje de correcciones manuales
* Insights por reglas
* App móvil completa: Inicio, Movimientos, Cuentas, Perfil, detalle e importación
* Notificaciones push (Expo) y señal en tiempo real (SignalR)
* Privacidad: exportar, eliminar movimientos, eliminar cuenta financiera, eliminar cuenta Nexo
* Docker Compose, seed demo, documentación y suite de tests

## Fase 2 — correo y sincronización

| Tarea | Bloqueante |
| --- | --- |
| Gmail OAuth (lectura) | Credenciales de Google + verificación de la app |
| Microsoft/Outlook OAuth | Registro en Entra ID |
| Reenvío a buzón Nexo | Dominio de entrada + relay con evaluación DMARC |
| Validación de parsers con correos reales | Correos reales con consentimiento |
| Sincronización incremental (historyId / deltaLink) | Lo anterior |
| Biometría al abrir la app | — |
| Certificate pinning | — |
| Paginación por keyset en movimientos | — |
| Reglas avanzadas editables por el usuario | — |

Nada de esto se puede completar sin credenciales reales. Los contratos, la
configuración y el endpoint ya existen; ver `docs/email-ingestion.md`.

## Fase 3 — integraciones oficiales

* APIs oficiales de instituciones (requiere acuerdos comerciales)
* Webhooks de billeteras (DEUNA, PayPhone, PeiGo)
* Categorización con modelo, entrenada sobre `category_corrections`
* Detección avanzada de suscripciones (cambios de precio, renovaciones próximas)
* Presupuestos y metas
* Multi-moneda

## Deuda técnica reconocida

| Tema | Detalle |
| --- | --- |
| Paginación | Offset hoy; keyset cuando el historial crezca (ADR-005) |
| Encabezados de los parsers | Validados contra exports de ejemplo, no contra los de cada banco |
| Workers | In-process; separar cuando la ingesta sea continua (ADR-004) |
| OpenTelemetry | Instrumentado con la BCL; falta el exportador |
| Cifrado en reposo | Depende del proveedor de base de datos |

## Principio de trabajo

No se marca como hecha una funcionalidad que no funciona. Si una integración
necesita credenciales que no existen, se entrega el contrato, la configuración y
la documentación, y se dice claramente que falta la credencial.
