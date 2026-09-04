# Nexo

**Todo tu dinero, en un solo lugar.**

Nexo es una app de finanzas personales para Ecuador: centraliza los movimientos
que hoy están repartidos entre Pichincha, Guayaquil, Produbanco, Pacífico, DEUNA,
PayPhone y PeiGo, y los convierte en una sola vista entendible.

Nexo **no** es un banco, ni una billetera, ni un procesador de pagos, ni un
sistema contable. **En el MVP no mueve ni custodia dinero.**

---

## Qué hay hoy en el repositorio

| Área | Estado |
| --- | --- |
| Backend .NET 10 (modular monolith) | Implementado |
| PostgreSQL + EF Core | Implementado |
| Autenticación (email/contraseña, access + refresh con rotación) | Implementado |
| Cuentas financieras y saldo verificado vs. estimado | Implementado |
| Importación CSV/XLSX con arquitectura de parsers | Implementado |
| Deduplicación (exacta / probable / sin coincidencia) | Implementado |
| Categorización por reglas + aprendizaje de correcciones | Implementado |
| Motor de insights por reglas | Implementado |
| App móvil Expo + Expo Router (todas las pantallas del MVP) | Implementado |
| Notificaciones push (Expo) y tiempo real (SignalR) | Implementado |
| Ingesta por correo | Arquitectura, parsers y endpoint listos; **requiere credenciales** |
| APIs oficiales de bancos | No implementado (fase 3, requiere acuerdos) |

Lo que no existe todavía está marcado como tal en la app: un proveedor nunca
anuncia una conexión automática que no está implementada.

---

## Requisitos

* [.NET SDK 10](https://dotnet.microsoft.com/download)
* [Docker](https://docs.docker.com/get-docker/) (para PostgreSQL)
* [Node.js 20+](https://nodejs.org) y npm
* Android Studio (emulador) o la app **Expo Go** en tu teléfono

---

## Arranque rápido

```bash
git clone <este-repo> nexo
cd nexo
cp .env.example .env
```

### 1. Base de datos + backend con Docker

```bash
docker compose up -d
```

Esto levanta PostgreSQL y la API en `http://localhost:5080`.
El contenedor de la API aplica el esquema y siembra los datos de referencia
(catálogo de proveedores, categorías y reglas) al arrancar.

### 2. O bien: PostgreSQL en Docker y el backend en local

```bash
docker compose up -d db

cd backend
dotnet restore
dotnet ef migrations add InitialCreate --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet ef database update --project src/Nexo.Infrastructure --startup-project src/Nexo.Api
dotnet run --project src/Nexo.Api
```

> **Sobre las migrations.** El repositorio no incluye la migración inicial
> generada: se crea con el comando de arriba, que es la forma normal de
> trabajar con EF Core. Si arrancas sin ninguna migración, la API crea el
> esquema desde el modelo (`EnsureCreated`) y lo registra con un warning, para
> que puedas probar de inmediato. **Para producción siempre usa migrations.**

La API queda en `http://localhost:5080`:

* `GET /health/live` — vive
* `GET /health/ready` — vive y llega a la base
* `GET /openapi/v1.json` — contrato OpenAPI (solo en Development)

### 3. Datos demo

Con `Nexo__Seed__Demo=true` (por defecto en Development) se crea el usuario
ficticio **Anderson** con cuatro cuentas y tres meses de movimientos:

```
Correo:      anderson@nexo.dev
Contraseña:  NexoDemo2026!

Banco Pichincha   $1,240.50
Banco Guayaquil   $  860.20
DEUNA             $  485.50
PayPhone          $  260.00
                  ---------
Total             $2,846.20
```

Los datos demo **nunca** se siembran fuera de `Development`: hay una doble
comprobación en el arranque.

### 4. App móvil

```bash
cd mobile
npm install
npx expo start
```

Pulsa `a` para abrir el emulador de Android, o escanea el QR con Expo Go.

**Cómo apunta el móvil al backend** (`mobile/services/config.ts`):

1. `EXPO_PUBLIC_API_URL` si está definida,
2. `extra.apiBaseUrl` de `app.json`,
3. si no, `10.0.2.2:5080` en Android y `localhost:5080` en iOS.

Para un teléfono físico usa la IP de tu máquina en la red local:

```bash
EXPO_PUBLIC_API_URL=http://192.168.1.20:5080 npx expo start
```

---

## Comandos

```bash
# Backend
cd backend
dotnet build                      # compilar toda la solución
dotnet test                       # unit + integration (SQLite, sin Docker)
dotnet test --filter Deduplication  # solo un área

# Móvil
cd mobile
npm run typecheck                 # tsc --noEmit
npm test                          # jest
npx expo start                    # servidor de desarrollo
```

---

## Android e iOS con EAS Build

```bash
npm install -g eas-cli
eas login
cd mobile
eas init                          # crea el projectId y lo escribe en app.json
```

**Android (APK instalable):**

```bash
eas build --platform android --profile preview
```

**iOS sin Mac** — EAS compila en la nube; solo necesitas una cuenta de Apple
Developer para firmar:

```bash
eas build --platform ios --profile preview   # simulador
eas build --platform ios --profile production
eas submit --platform ios
```

Recuerda apuntar `EXPO_PUBLIC_API_URL` (en `eas.json`) a una API accesible
públicamente: `localhost` no existe dentro de un build.

---

## Estructura

```
nexo/
├── backend/
│   ├── src/
│   │   ├── Nexo.Domain/          # entidades, reglas, fingerprint, saldos
│   │   ├── Nexo.Application/     # casos de uso, parsers, dedup, insights
│   │   ├── Nexo.Infrastructure/  # EF Core, seguridad, seeds, push
│   │   ├── Nexo.Workers/         # background jobs (in-process)
│   │   └── Nexo.Api/             # minimal APIs, auth, SignalR
│   └── tests/
├── mobile/                       # Expo + Expo Router + TypeScript
├── docs/                         # arquitectura, dominio, seguridad, ...
├── samples/                      # estados de cuenta y correos ficticios
├── docker-compose.yml
├── .env.example
└── README.md
```

---

## Documentación

| Documento | Contenido |
| --- | --- |
| [docs/architecture.md](docs/architecture.md) | Arquitectura, capas y decisiones (ADR) |
| [docs/domain.md](docs/domain.md) | Modelo canónico y reglas de negocio |
| [docs/transaction-deduplication.md](docs/transaction-deduplication.md) | Cómo se decide si un movimiento ya existe |
| [docs/security.md](docs/security.md) | Controles y threat model |
| [docs/email-ingestion.md](docs/email-ingestion.md) | Pipeline de correo y qué falta para activarlo |
| [docs/provider-integration.md](docs/provider-integration.md) | Cómo agregar un banco nuevo |
| [docs/mobile-architecture.md](docs/mobile-architecture.md) | Estructura y design system del móvil |
| [docs/deployment.md](docs/deployment.md) | Despliegue y operación |
| [docs/roadmap.md](docs/roadmap.md) | MVP, fase 2 y fase 3 |

---

## Privacidad

Desde el perfil el usuario puede exportar sus datos, eliminar sus movimientos,
desconectar su correo, eliminar una cuenta financiera y eliminar su cuenta Nexo.

Nexo **nunca** guarda contraseñas de banca electrónica, PIN ni OTP, y no hace
scraping de banca web.
