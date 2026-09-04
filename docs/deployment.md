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

| Variable | Obligatoria | Notas |
| --- | --- | --- |
| `ConnectionStrings__Default` | Sí | Con TLS hacia PostgreSQL |
| `Nexo__Jwt__SigningKey` | Sí | ≥ 32 caracteres; el arranque falla sin ella |
| `Nexo__Secrets__EncryptionKey` | Sí | 32 bytes en base64 |
| `Nexo__Database__AutoMigrate` | Recomendado `false` | Migrar en el pipeline |
| `Nexo__Seed__Demo` | `false` | Doble comprobación en el código |
| `Nexo__Push__Enabled` | `true` para push real | Usa el servicio de Expo |
| `Nexo__Cors__AllowedOrigins` | Si hay web | Vacío = permisivo (solo dev) |
| `Nexo__Workers__Enabled` | `true` | `false` si los workers corren aparte |
| `Nexo__RateLimiting__Enabled` | `true` | Solo `false` en entornos de prueba |

Genera claves con:

```bash
openssl rand -base64 48   # firma JWT
openssl rand -base64 32   # cifrado de secretos
```

## Contenedor

`backend/Dockerfile` es multi-stage: restaura, publica en Release y ejecuta sobre
`aspnet:10.0` con un usuario sin privilegios (uid 10001).

**La imagen necesita ICU.** La normalización de acentos (`SUPERMAXI ALBÓRADA` debe
igualar a `ALBORADA` para deduplicar) y la zona horaria `America/Guayaquil`
dependen de ella. La imagen Debian que usamos la trae; si alguna vez se cambia a
Alpine, hay que instalar `icu-libs` y **no** activar `InvariantGlobalization`.

## Salud

| Endpoint | Para qué |
| --- | --- |
| `/health/live` | Liveness: el proceso responde. No toca la base, así que una base lenta no reinicia el pod. |
| `/health/ready` | Readiness: además llega a PostgreSQL. |

## Observabilidad

* Logs JSON a stdout con `CorrelationId` en el scope.
* `X-Correlation-Id` se acepta y se devuelve en cada respuesta.
* Métricas y trazas: el código usa `ActivitySource` y `Meter` de la BCL, que son
  compatibles con OpenTelemetry. Para exportar, añade al host:

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
apuntar a `localhost`.

## Copias de seguridad

PostgreSQL es la única fuente de verdad; no hay archivos que respaldar (los
estados de cuenta no se persisten). Backups diarios con retención de 30 días y
una prueba de restauración periódica —un backup sin restauración probada no es
un backup.
