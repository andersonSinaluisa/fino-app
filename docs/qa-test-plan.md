# Plan de QA manual

Entregable 32 del plan de 34. No tengo el texto original en español de este
entregable, solo la etiqueta corta "QA" — este documento es mi propia
interpretación de lo que hace falta antes de una beta cerrada (Entregable 33)
y de la decisión Go/No-Go (Entregable 34), después de revisar qué existe hoy.

**Esto no es un reporte de QA ya ejecutado.** No hay dispositivo real, ni
simulador, ni build instalado en este entorno — nada de lo que sigue fue
clickeado por mí. Es el plan y las herramientas para que Anderson (o quien
haga de tester) lo ejecute con la app real, antes de invitar a los primeros
usuarios de la beta cerrada.

## Qué ya garantiza la suite automatizada (no lo repitas a mano)

45 archivos de test en el backend (dominio, aplicación, integración) y 58
tests en mobile ya cubren, con certeza de que corren en cada cambio:

| Área | Dónde |
| --- | --- |
| Aislamiento entre usuarios (nunca ver datos de otro) | `AuthorizationTests.cs` |
| Rotación de refresh tokens, reuso detectado, bloqueo por intentos fallidos | `AuthenticationTests.cs`, `AccountLockoutTests.cs` |
| Parsers de cada banco (Pichincha, Guayaquil, Produbanco, Pacífico, genérico, mapeo manual) contra los exports reales | `StatementParserTests.cs`, `GenericStatementParserTests.cs`, `ManualMappingStatementParserTests.cs`, `ValueParserTests.cs` |
| Deduplicación exacta/probable, incluida la frontera de día calendario Ecuador vs. UTC | `DeduplicationMatcherTests.cs`, `CrossSourceDeduplicationTests.cs` |
| Confirmación de importación, actualización de duplicados de correo → extracto | `ImportPipelineFlowTests.cs`, `ImportFlowTests.cs` |
| Revisión de duplicado desde la app (confirmar/descartar) | `DuplicateReviewTests.cs` |
| Categorización y aprendizaje de reglas | `CategorizationTests.cs` |
| Transferencias internas | `InternalTransferTests.cs` |
| Privacidad (exportar, borrar movimientos, borrar cuenta con período de gracia) | `PrivacyFlowTests.cs`, `PrivacyEndToEndTests.cs` |
| Sesiones y auditoría | `SessionsTests.cs`, `AuditActivityTests.cs` |
| Notificaciones | `NotificationsTests.cs` |
| Salud, correlation id | `ObservabilityTests.cs` |
| Formato/cálculo de montos, tarjetas de cuenta y movimiento (mobile) | `mobile/__tests__/*` |

Si un caso de esta tabla falla en un dispositivo real, es casi seguro un
problema de la UI (no de la lógica de negocio, que ya está probada) — mira
primero la pantalla, no el backend.

## Lo que la suite automatizada NO cubre (por eso existe este documento)

No hay Detox/Maestro ni ningún framework de UI end-to-end para mobile en
este repositorio — los 58 tests de mobile son de utilidades y componentes
sueltos, no de pantallas completas ni de navegación. Esta pasada manual es,
hoy, la única verificación real de que las pantallas funcionan juntas.
Tampoco hay manera automatizada de comprobar: apariencia visual real (los
íconos/splash recién conectados en el Entregable 31), push de verdad
llegando a un dispositivo, ni el comportamiento contra un archivo de banco
que nadie haya visto todavía.

## Preparar el entorno

No existe todavía un staging real (Entregable 30) — corre esto contra tu
`docker compose up -d` local:

```bash
cp .env.example .env
docker compose up -d
```

Los archivos de `samples/` (ver `samples/README.md`) son las mismas
notificaciones y extractos ficticios que usan los tests automatizados —
úsalos para las importaciones y el correo de este plan.

## Guiones de prueba

Cada guion asume una cuenta nueva salvo que diga lo contrario. Anota
resultado (OK / FALLA) y, si falla, la severidad según la rúbrica al final.

### 1. Cuenta y sesión

1. Registrarse con un correo nuevo, contraseña y nombre. Debe entrar
   directo a la app (sin pantalla de verificación de correo — no existe).
2. Cerrar sesión y volver a entrar con las mismas credenciales.
3. Provocar 5 intentos de login con la contraseña equivocada (el límite por
   defecto, `MaxFailedLoginAttempts`): la cuenta debe bloquearse
   temporalmente (no debe decir cuántos intentos faltan).
4. Iniciar sesión en dos dispositivos (o un dispositivo + Postman con el
   refresh token). Ir a Perfil → Sesiones: deben aparecer ambas. Revocar una
   desde la otra y confirmar que la revocada pide login de nuevo.
5. "Cerrar sesión en todos los dispositivos" desde Perfil: ambas sesiones
   deben quedar inválidas.

### 2. Cuentas financieras

1. Crear una cuenta de cada tipo (ahorro, corriente, billetera) para cada
   proveedor de `samples/`.
2. Actualizar el saldo verificado a mano y confirmar que la pantalla lo
   distingue de un saldo estimado (el texto/estado debe ser distinto, nunca
   mostrar un saldo calculado como si fuera oficial — esto es una regla dura
   del proyecto, no solo un detalle visual).
3. Eliminar una cuenta y confirmar que el flujo avisa del período de gracia
   reversible (no debe ser un borrado inmediato e irreversible desde la UI).

### 3. Importación (usa `samples/`)

Para cada archivo, sube, revisa el preview (nunca debe escribir nada todavía)
y confirma:

1. `pichincha-marzo-2026.csv` — feliz, columnas débito/crédito.
2. `guayaquil-marzo-2026.csv` — separador `;`, decimales con coma: los montos
   en la app deben verse correctos, no con la coma mal interpretada.
3. `generico-billetera.csv` — activa el mapeo genérico (probar también el
   wizard de mapeo manual de columnas si el parser no lo reconoce solo).
4. `archivo-invalido.csv` — debe fallar visiblemente, sin agregar nada al
   historial de importaciones ni a los movimientos.
5. `pichincha-movimientos-agosto-2026.xlsx` — el caso difícil: deben entrar
   **6** movimientos (transferencia + IVA + comisión son tres movimientos
   distintos con el mismo número de documento), no 4.
6. Repetir la importación de `pichincha-marzo-2026.csv`: el preview debe
   mostrar 0 nuevos y 12 duplicados exactos, y confirmar no debe duplicar
   nada en el historial de movimientos.
7. Subir un archivo y cancelarlo antes de confirmar: no debe dejar rastro en
   movimientos; sí debe aparecer en el historial de importaciones como
   cancelado.

### 4. Movimientos y duplicados

1. Filtrar movimientos por cuenta, categoría, rango de fecha y texto.
2. Editar la categoría, la nota y el comerciante de un movimiento; confirmar
   que persiste al recargar.
3. Simular un correo de notificación (`correo-pichincha-compra.txt` vía el
   endpoint `/api/v1/email-ingestion/inbound`, o crear la conexión de
   reenvío desde la app y reenviar el correo de verdad si tienes uno de
   prueba) y luego importar el extracto que contiene el mismo movimiento:
   debe aparecer marcado para revisión de duplicado, no duplicado ni
   perdido. Desde el detalle del movimiento, probar ambos botones: "es el
   mismo" (debe quedar Ignored, sin contar al saldo) y "son distintos" (debe
   quedar Posted, contando al saldo).
4. `correo-phishing.txt`: enviarlo al webhook de ingestión no debe crear
   ningún movimiento ni cuenta nueva; debe generar (si las notificaciones de
   seguridad están activas) una alerta de correo sospechoso.

### 5. Transferencias internas

1. Con dos cuentas propias, hacer un movimiento que luzca como transferencia
   entre ellas (mismo monto, fechas cercanas, direcciones opuestas) y
   confirmar que aparece como candidato de transferencia interna.
2. Confirmarla y verificar que no se cuenta dos veces en el resumen (ni como
   gasto en una cuenta y como "nada" en la otra).

### 6. Categorización, dashboard e insights

1. Corregir la categoría sugerida de un movimiento nuevo del mismo comercio
   varias veces seguidas; confirmar que la app empieza a sugerir la
   categoría corregida sola.
2. Revisar el dashboard: saldo total, resumen del mes, movimientos
   recientes.
3. Revisar Insights: la comparación con el mes anterior debe tener sentido
   con los montos reales importados (no un texto genérico).

### 7. Notificaciones

1. Revisar la pantalla de notificaciones in-app después de una importación
   o un correo procesado.
2. Si `Nexo:Push:Enabled=true` y hay un proyecto Expo real (ver Entregable
   31 — hoy no lo hay): registrar el dispositivo y confirmar que llega un
   push real. Con `Push:Enabled=false` (el valor por defecto), confirmar que
   la app no rompe ni promete un push que nunca llega.
3. Cambiar las preferencias de notificación por dispositivo y confirmar que
   se respetan.

### 8. Conectar correo (Gmail/Outlook deben verse "próximamente")

1. Ir a Conectar correo → Reenvío: debe funcionar (genera una dirección de
   reenvío).
2. Ir a Conectar correo → Gmail u Outlook: debe mostrar el aviso "esta
   conexión necesita credenciales que este servidor todavía no tiene
   configuradas" **sin ningún botón para intentarlo** — si aparece un botón
   de "Conectar" ahí, es un defecto (la app no debe prometer algo que el
   backend rechazaría con 409).

### 9. Privacidad

1. Exportar mis datos: el archivo debe incluir cuentas y movimientos
   propios, nada de otro usuario.
2. Borrar movimientos de una cuenta (no la cuenta completa): confirmar que
   desaparecen pero la cuenta sigue existiendo.
3. Eliminar la cuenta Nexo completa: confirmar el aviso de período de
   gracia reversible antes del borrado definitivo, y que iniciar sesión de
   nuevo durante ese período cancela el borrado.

### 10. Aislamiento multiusuario (pasada manual, no solo confiar en los tests)

1. Crear un segundo usuario. Confirmar que el dashboard, movimientos,
   cuentas y notificaciones están completamente vacíos para él.
2. Intentar adivinar una URL de movimiento/cuenta del primer usuario desde
   la sesión del segundo (si la app expone algún deep link) — debe dar
   "no encontrado", nunca los datos ajenos.

### 11. Salud (para quien despliegue, no para el usuario final)

```bash
curl -i http://localhost:5080/health/live
curl -i http://localhost:5080/health/ready
curl -i -H "X-Correlation-Id: prueba-manual-123" http://localhost:5080/health/live
```

Las dos primeras deben responder `200 Healthy`; la tercera debe devolver el
mismo `X-Correlation-Id: prueba-manual-123` en la respuesta.

## Rúbrica de severidad (para decidir Go/No-Go en el Entregable 34)

| Severidad | Definición | Ejemplo |
| --- | --- | --- |
| Bloqueante | Dinero mal calculado, datos de un usuario visibles para otro, pérdida de datos, o un borrado que no debía ser irreversible y lo es | Un movimiento se cuenta dos veces al saldo |
| Alta | Una funcionalidad central no funciona pero no hay pérdida de datos ni fuga entre usuarios | La importación de un banco soportado falla siempre |
| Media | Funciona pero con una experiencia rota o confusa | El mensaje de error no explica qué pasó |
| Baja | Cosmético | Un ícono o texto mal alineado |

Ningún hallazgo Bloqueante o Alto debería quedar abierto antes de invitar a
los primeros beta testers (Entregable 33).

## Fuera de alcance para este QA (no son defectos, son límites conocidos)

* Gmail/Outlook: bloqueados por falta de credenciales OAuth reales
  (Entregables 24/25) — el comportamiento correcto es exactamente el descrito
  en el guion 8, no un error a reportar.
* No hay staging real (Entregable 30): esta pasada corre contra el
  `docker compose` local.
* Push real end-to-end (Entregable 31/18): necesita un proyecto EAS real,
  que no existe todavía (`extra.eas.projectId` sigue siendo un placeholder).
* `Nexo:Seed:Demo` no aplica fuera de `ASPNETCORE_ENVIRONMENT=Development` a
  propósito — no esperes ver los datos de ejemplo del usuario `Anderson` si
  pruebas contra un build de staging/producción.
