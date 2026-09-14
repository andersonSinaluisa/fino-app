# Despliegue

## Local

```bash
cp .env.example .env
docker compose up -d          # PostgreSQL + API en http://localhost:5080
```

Solo la base y el backend en local:

```bash
docker compose up -d db
cd backend
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet run --project src/Nexo.Api
```

## Migraciones

La primera migración se genera una sola vez y se versiona:

```bash
cd backend
dotnet ef migrations add InitialCreate \
  --project src/Nexo.Infrastructure \
  --startup-project src/Nexo.Api
```

Al arrancar, la API aplica las migraciones pendientes cuando
`Nexo:Database:AutoMigrate` es `true`. Si el ensamblado no tiene ninguna
migración, crea el esquema desde el modelo y lo registra con un warning: eso es
para desarrollo, **no para producción**.

En producción, lo recomendable es aplicar las migraciones en un paso de
despliegue separado (`dotnet ef database update` o un bundle) y dejar
`AutoMigrate=false`, para que varias instancias no compitan por migrar.

## Variables de entorno en producción

Usa `.env.production.example` como plantilla. El archivo real debe llamarse
`.env.production` o vivir en el gestor de secretos del proveedor; no se
versiona.

| Variable | Obligatoria | Notas |
| --- | --- | --- |
| `ConnectionStrings__Default` | Sí | Con TLS hacia PostgreSQL |
| `Nexo__Jwt__SigningKey` | Sí | ≥ 32 caracteres; el arranque falla sin ella |
| `Nexo__Secrets__EncryptionKey` | Sí | 32 bytes en base64 |
| `Nexo__Database__AutoMigrate` | `true` en staging inicial | Cambiar a `false` cuando haya migraciones versionadas y el pipeline las aplique |
| `Nexo__Seed__Demo` | `false` | Doble comprobación en el código |
| `Nexo__Push__Enabled` | `true` para push real | Usa el servicio de Expo |
| `Nexo__Cors__AllowedOrigins__0` | Si hay web | Vacío = permisivo solo en Development; fuera de Development falla cerrado |
| `Nexo__Workers__Enabled` | `true` | `false` si los workers corren aparte |
| `Nexo__RateLimiting__Enabled` | `true` | Solo `false` en entornos de prueba |
| `Nexo__EmailIngestion__Gmail__ClientId` | No hasta activar Gmail | De Google Cloud Console |
| `Nexo__EmailIngestion__Gmail__ClientSecret` | No hasta activar Gmail | Secreto real, nunca commiteado |
| `Nexo__EmailIngestion__Gmail__RedirectUri` | No hasta activar Gmail | Ej. `https://api.nexo.app/oauth/gmail/callback` |
| `Nexo__EmailIngestion__Outlook__ClientId` | No hasta activar Outlook | De Microsoft Entra ID |
| `Nexo__EmailIngestion__Outlook__ClientSecret` | No hasta activar Outlook | Secreto real, nunca commiteado |
| `Nexo__EmailIngestion__Outlook__RedirectUri` | No hasta activar Outlook | Ej. `https://api.nexo.app/oauth/outlook/callback` |
| `Nexo__EmailIngestion__Forwarding__InboundDomain` | Para reenvío | Dominio entrante real del relay |
| `Nexo__EmailIngestion__Forwarding__WebhookSecret` | Para reenvío | Secreto compartido real del relay |

Genera claves con:

```bash
openssl rand -base64 48   # firma JWT
openssl rand -base64 32   # cifrado de secretos
```

## Contenedor

`backend/Dockerfile` es multi-stage: restaura, publica en Release y ejecuta sobre
`aspnet:10.0` con el usuario sin privilegios que trae la imagen oficial
(`USER $APP_UID`).

Para levantar la API con configuración productiva desde este repo:

```bash
scripts/start-production.sh \
  --db-host host.docker.internal \
  --db-ssl disable \
  --api-url https://api.fino.app
```

El script crea `.env.production` si no existe, genera los secretos faltantes
(`Nexo__Jwt__SigningKey`, `Nexo__Secrets__EncryptionKey`,
`Nexo__EmailIngestion__Forwarding__WebhookSecret` y `POSTGRES_PASSWORD` si sigue
en placeholder), valida el compose y levanta solo la API. No levanta
PostgreSQL: en producción debe existir en el servidor o en una instancia
administrada. Si PostgreSQL está instalado en el mismo host Docker, usa
`--db-host host.docker.internal`; el compose productivo mapea ese nombre al
gateway del host.

**La imagen necesita ICU.** La normalización de acentos (`SUPERMAXI ALBÓRADA` debe
igualar a `ALBORADA` para deduplicar) y la zona horaria `America/Guayaquil`
dependen de ella. La imagen Debian que usamos la trae; si alguna vez se cambia a
Alpine, hay que instalar `icu-libs` y **no** activar `InvariantGlobalization`.

## Salud

| Endpoint | Para qué |
| --- | --- |
| `/health/live` | Liveness: el proceso responde. No toca la base, así que una base lenta no reinicia el pod. |
| `/health/ready` | Readiness: además llega a PostgreSQL. |

Smoke test productivo después del despliegue:

```bash
scripts/smoke-production.sh https://api.fino.app
```

El script valida `/health/live` y `/health/ready`; no crea usuarios ni escribe
datos en producción.

## Observabilidad

* Logs JSON a stdout con `CorrelationId` en el scope.
* `X-Correlation-Id` se acepta (si el header viene vacío o supera 64 caracteres,
  se genera uno nuevo) y se devuelve en cada respuesta.
* Errores no controlados: `ProblemDetailsHandler` responde RFC 9457 con
  `correlationId`, y solo registra el detalle completo en el log del servidor
  (nunca en la respuesta al cliente).
* Métricas y trazas (Entregable 28): `Nexo.Application.Common.NexoTelemetry`
  expone un `ActivitySource` y un `Meter` de la BCL (ambos nombrados `"Nexo"`),
  compatibles con OpenTelemetry sin añadir el paquete. Instrumentan las tres
  rutas de negocio más relevantes:
  - `EmailIngestionPipeline.ProcessAsync` — span `EmailIngestion.Process` +
    contador `nexo.email_ingestion.processed`, etiquetado por resultado
    (`Created`, `Duplicate`, `Rejected`, `NotAMovement`, `NoMatchingAccount`).
  - `ImportService.ConfirmAsync` — span `Import.Confirm` + contador
    `nexo.import.rows_processed`, etiquetado `imported` / `flagged_for_review`
    / `upgraded`.
  - `DeduplicationService.CheckBatchAsync` — contador
    `nexo.deduplication.checks`, etiquetado por tipo de coincidencia
    (`ExactMatch`, `ProbableMatch`, `NoMatch`).

  Solo se registran conteos y resultados de tipo enum — nunca un monto, una
  descripción, un correo o un nombre de archivo (misma disciplina que
  `docs/security.md` exige para logs).

  Sin un listener registrado, tanto `ActivitySource` como `Meter` son
  prácticamente no-op (`StartActivity` devuelve `null`, `Counter.Add` es una
  comprobación barata), así que queda seguro dejarlo activo en producción antes
  de configurar un exportador. Para exportar, añade al host:

```xml
<PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.*" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.*" />
<PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.*" />
```

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddAspNetCoreInstrumentation().AddOtlpExporter())
    .WithMetrics(m => m.AddAspNetCoreInstrumentation().AddOtlpExporter());
```

Se documenta en vez de incluirse para no añadir tres dependencias que este MVP
todavía no usa.

## Integración continua

`.github/workflows/ci.yml` corre en cada push y pull request (y bajo demanda,
`workflow_dispatch`):

* **Backend**: `dotnet restore` / `build` / `test` sobre `Nexo.sln`, en
  Release. Los tests de integración corren contra SQLite (`NexoApiFactory`),
  así que el job no necesita levantar Postgres como servicio.
* **Mobile**: `npm ci`, `npm run typecheck` (`tsc --noEmit`) y `npm test`
  (Jest).

El workflow usa `permissions: contents: read` (no necesita escribir nada) y
un `concurrency` group que cancela una corrida vieja cuando llega un push más
nuevo a la misma rama, para no seguir gastando minutos de Actions en un
commit ya superado.

**Entregable 29 ("CI"), lo que corregí:** el trigger de `push` solo escuchaba
la rama `main`; la rama real de este repositorio es `master` (no hay remoto
de GitHub configurado todavía). Con eso, un push directo a `master` nunca
habría disparado el workflow. Ahora escucha ambos nombres (`[main, master]`)
para que funcione sin importar cuál termine siendo la rama por defecto una
vez que el repositorio tenga un remoto real.

**Lo que no puedo verificar desde aquí:** este entorno no tiene acceso a
ejecutar GitHub Actions ni a un remoto de GitHub real, así que el workflow
está validado solo estáticamente (YAML parseado, nombres de pasos, rutas de
trabajo, existencia de `Nexo.sln` y de `mobile/package-lock.json` — todo
verificado a mano). **No ha corrido nunca de verdad.** La primera vez que se
haga push a un repositorio de GitHub real, hay que revisar manualmente ese
primer run antes de confiar en el badge o en un branch-protection rule que
lo exija.

## Staging

**No hay un entorno de staging real desplegado en ningún lado.** Levantarlo
necesita una cuenta de nube, un dominio, un certificado TLS y una decisión de
presupuesto/proveedor — nada de eso existe en este repositorio ni puede
inventarse desde aquí. `mobile/eas.json` ya asume uno en su perfil `preview`
(`EXPO_PUBLIC_API_URL=https://api.staging.nexo.app`), así que lo que sigue es
justamente lo que falta para que ese perfil apunte a algo real.

Lo que sí queda preparado en el repositorio para cuando exista ese host:

* **`.env.staging.example`** — igual que `.env.example` pero para staging, con
  todas las variables preparadas y sin credenciales reales: se usa
  `ASPNETCORE_ENVIRONMENT=Staging`, que se comporta como producción, no como
  desarrollo — CORS exige orígenes explícitos (sin permisivo por defecto),
  HSTS/HTTPS redirect se activan, y `Nexo:Seed:Demo` **no puede** sembrar datos
  de ejemplo sin importar su valor: `DemoDataSeeder` y `Program.cs` lo
  condicionan a `IsDevelopment()` a propósito, sin excepción para staging (ver
  `docs/security.md`). Las cuentas de QA/beta en staging se crean como cuentas
  reales a través de la app, igual que en producción.
* **`scripts/smoke-staging.sh`** — un smoke test para correr justo después de
  desplegar: `/health/live`, `/health/ready`, un registro de cuenta
  desechable (`smoke-test+<timestamp>@nexo.invalid`, fácil de purgar en bloque)
  y una lectura de `/api/v1/accounts` con el token resultante. Verificado de
  verdad en este entorno contra un servidor HTTP de prueba (no contra la API
  real, que no existe todavía en ningún host) — el script en sí funciona;
  lo que falta es un host real donde apuntarlo.

**Lo que queda pendiente y es una decisión de Anderson, no técnica:** elegir
dónde vive staging (un VPS con el mismo `docker-compose.yml` de este repo
usando `.env.staging`, o un PaaS como Fly.io/Railway/Azure Container Apps),
comprar o delegar el dominio `api.staging.nexo.app`, y provisionar TLS. Una
vez que exista, `scripts/smoke-staging.sh` es el primer chequeo a correr.

## App móvil

```bash
cd mobile
npm install
npx expo start                                   # desarrollo

eas build --platform android --profile preview   # APK
eas build --platform ios --profile production    # iOS, sin Mac local
eas submit --platform ios
```

`EXPO_PUBLIC_API_URL` se define por perfil en `eas.json`. Un build nunca puede
apuntar a `localhost` ni a un túnel temporal. El perfil `preview` apunta a
`https://api.staging.nexo.app` y el perfil `production` a
`https://api.fino.app`; cambia esos dominios solo cuando existan los hosts
reales equivalentes.

**Entregable 31 ("Mobile builds"), estado real.** `mobile/app.json` y
`mobile/eas.json` ya existían con los tres perfiles (development/preview/
production); revisé ambos con `npx expo-doctor` (corre en local, sin
necesitar una cuenta de Expo) y encontré y corregí dos problemas concretos,
sin necesitar credenciales:

* **`expo-font` faltaba como dependencia directa.** `@expo/vector-icons` lo
  necesita como peer dependency nativo; `expo-doctor` señalaba
  explícitamente "tu app puede crashear fuera de Expo Go sin esto" — es decir,
  exactamente en un build real (development/preview/production), no en Expo
  Go durante desarrollo, que es donde nadie lo habría notado hasta el primer
  build. Ya está agregado (`~57.0.3`, mismo estilo de versión que el resto de
  paquetes `expo-*` del proyecto) y `npm install` corrido.
* **Los assets de marca en `mobile/assets/` (icon.png, los tres PNG del
  ícono adaptativo de Android, splash-icon.png, favicon.png) existían pero
  `app.json` nunca los referenciaba** — un build real habría usado el ícono
  y splash por defecto de Expo, no los de Nexo. Ahora `icon`, `splash.image`,
  `android.adaptiveIcon.{foregroundImage,backgroundImage,monochromeImage}` y
  `web.favicon` apuntan a los archivos reales. Verificado con
  `npx expo config --type public` (el merge resuelve las rutas sin error) y
  confirmando a mano que cada archivo referenciado existe.

`npx expo-doctor` corre 21 checks; con estos dos arreglos pasan 19. Los 2 que
siguen fallando son de red (intentan alcanzar servicios de Expo/GitHub para
validar el esquema del config y el directorio de paquetes de React Native) y
este entorno no tiene esa salida a internet — no son fallas del proyecto,
son checks que no pueden correr aquí. Ejecútalos de nuevo desde tu máquina
para confirmarlo.

**Lo que sigue bloqueado, y por qué:** `extra.eas.projectId` en `app.json`
sigue en `00000000-0000-0000-0000-000000000000` — un placeholder a
propósito, tal como ya documenta el README ("`eas init` crea el projectId y
lo escribe en app.json"). Generar uno real requiere `eas login` con una
cuenta de Expo real, que no existe en este entorno; inventar un GUID
cualquiera rompería silenciosamente cualquier build futuro apuntando a un
proyecto EAS que no existe. Lo mismo aplica a correr `eas build` de verdad
(necesita esa cuenta, y para iOS una cuenta de Apple Developer para firmar)
y a `eas submit`.

## Copias de seguridad

PostgreSQL es la única fuente de verdad; no hay archivos que respaldar (los
estados de cuenta no se persisten). Backups diarios con retención de 30 días y
una prueba de restauración periódica —un backup sin restauración probada no es
un backup.
