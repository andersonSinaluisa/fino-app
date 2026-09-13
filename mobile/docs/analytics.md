# Product analytics en FINO

## Objetivo

Saber **qué funciones usa la gente y dónde abandona**, sin que salga de la app
una sola cifra de su dinero.

La frase que esta implementación tiene que sostener literalmente:

> Medimos cómo se usa la aplicación, no cuánto dinero tienes ni qué compraste.

## Proveedor

**PostHog**, instanciado directamente desde `services/analytics/providers/posthog.ts`.

Por qué PostHog y no Amplitude, Mixpanel o Firebase:

- es el único de los serios que se puede **auto-hospedar**, y en una app
  financiera eso cambia quién custodia los datos de comportamiento;
- trae funnels, cohortes y retención nativos, que es exactamente lo que
  necesitan los dashboards de más abajo;
- incluye feature flags, que hoy FINO no tiene.

### Dos decisiones de configuración que no son negociables

1. **El cliente se instancia directamente, NUNCA con `<PostHogProvider>`.**
   Ese componente es el que activa el autocapture de PostHog, que registra
   toques y el texto de los elementos tocados. En FINO eso capturaría montos,
   alias de cuenta y descripciones de movimientos. Sin el componente, el SDK
   solo manda lo que le pasamos explícitamente.

2. **`enableSessionReplay: false` y `disableGeoip: true`.** Una grabación de
   sesión de una app de finanzas es un volcado de datos financieros, y la
   ubicación derivada de la IP no responde ninguna pregunta de producto.

### Instalación (pendiente)

El paquete todavía **no está instalado**. El adaptador lo carga con un
`require` protegido y cae a Debug/Noop si no está, así que la app compila y
corre igual. Para activarlo:

```bash
npx expo install posthog-react-native expo-file-system expo-application expo-device expo-localization
```

Y en el perfil de EAS correspondiente:

```
EXPO_PUBLIC_POSTHOG_KEY=phc_...
EXPO_PUBLIC_POSTHOG_HOST=https://us.i.posthog.com   # o tu instancia propia
EXPO_PUBLIC_ENVIRONMENT=production
```

Sin `EXPO_PUBLIC_POSTHOG_KEY` no se instancia ningún proveedor real. No hay
forma de que un build empiece a mandar eventos sin que alguien haya puesto la
clave a propósito.

## Arquitectura

```
pantallas y hooks
      |  track(AnalyticsEvent.X, { ... })
      v
services/analytics.ts            <- fachada pública, único import de la app
      v
services/analytics/service.ts    <- AnalyticsService: consentimiento, sesión,
      |                             identidad, trackOnce, contexto global
      |  sanitizeAnalyticsProperties()   <- allowlist por evento
      v
AnalyticsProvider (interfaz)
      |
      +-- PostHogAnalyticsProvider   (producción)
      +-- DebugAnalyticsProvider     (desarrollo: imprime, no envía)
      +-- NoopAnalyticsProvider      (desactivado / consentimiento denegado / tests)
```

Ninguna pantalla conoce el SDK. Cambiar de proveedor es escribir otra
implementación de `AnalyticsProvider`; no se toca una sola pantalla.

### Archivos

| Archivo | Qué hace |
|---|---|
| `services/analytics.ts` | Fachada. `track`, `trackOnce`, `screen`, `identify`. |
| `services/analytics/events.ts` | Catálogo de eventos, pantallas y enums de valores. |
| `services/analytics/schema.ts` | Allowlist de propiedades **por evento**. |
| `services/analytics/sanitize.ts` | Filtro que aplica la allowlist y valida la forma de los valores. |
| `services/analytics/buckets.ts` | Duración, conteos y longitudes a tramos. |
| `services/analytics/service.ts` | El servicio. |
| `services/analytics/screens.ts` | Mapa ruta → pantalla (lógica pura, testeable). |
| `services/analytics/storage.ts` | Hitos únicos, ventana de sesión, consentimiento. |
| `services/analytics/firstValue.ts` | Definición del momento de valor. |
| `services/analytics/config.ts` | Config desde `EXPO_PUBLIC_*`. |
| `hooks/useAnalyticsBootstrap.ts` | Arranque, sesión, identidad y pantallas. Montado una vez en `app/_layout.tsx`. |

## Identidad

El id de analytics es el **id interno del usuario** (un GUID del backend),
nunca su correo, su nombre ni un número de cuenta.

- Al entrar: `analytics.identify(user.id, { ... })`
- Al registrarse: `analytics.aliasToCurrentUser(user.id)` cose los eventos
  anónimos previos (instalación, pantalla de registro) al id interno, para no
  perder el tramo `install → signup` del embudo.
- Al salir: `analytics.reset()` en `finishLocalLogout`, **antes** de limpiar la
  sesión. En un teléfono compartido, los eventos de quien entre después no
  pueden colgar del usuario que acaba de salir.

### Mejora pendiente

Hoy el id de analytics **es** el id de la base de datos. Es opaco y cumple la
regla, pero lo ideal sería que el backend expusiera un `analyticsId` aparte
(por ejemplo un HMAC del userId con un secreto del servidor), para que unos
datos de analytics filtrados no se puedan cruzar con la base directamente. El
cambio afecta a una sola línea de `useAnalyticsBootstrap.ts`.

## Cómo se protegen los datos sensibles

Son **dos** filtros, y hacen falta los dos.

### 1. Allowlist por evento (`schema.ts`)

Para cada evento se declara exactamente qué propiedades pueden salir:

```ts
[AnalyticsEvent.QuickEntrySaved]: [
  'source', 'entryMode', 'transactionType',
  'categorySource', 'offline', 'durationBucket',
],
```

Todo lo que no esté ahí se descarta. Es allowlist y no blacklist porque una
blacklist solo bloquea lo que alguien se acordó de prohibir: el día que
alguien mande `{ merchantLabel }`, la blacklist no lo ve venir.

El tipo es `Record<AnalyticsEventName, readonly string[]>`, así que
**TypeScript obliga** a declarar el esquema de cada evento nuevo. Un evento
sin esquema no compila.

### 2. Forma del valor (`sanitize.ts`)

La allowlist controla *qué claves* salen. Esto controla *qué forma* pueden
tener los valores:

- **Números: solo en `step` y `fieldsParsed`**, y entre 0 y 100. Todo lo demás
  cuantitativo va bucketizado como texto. Esta es la protección que de verdad
  importa: si cualquier clave pudiera llevar números, un monto redondeado
  podría colarse por una propiedad permitida.
- **Textos: solo símbolos de vocabulario cerrado** — `^[a-z0-9_.:-]+$`, máximo
  40 caracteres. Una descripción (`"Supermaxi Urdesa"`), un nombre
  (`"María Pérez"`) o una transcripción llevan espacios, tildes o mayúsculas
  mezcladas y **no pasan**, aunque alguien las ponga en una clave permitida.
- Objetos, arrays y funciones: descartados siempre.

### 3. Red secundaria

`SUSPICIOUS_KEY_PATTERN` descarta claves que *parezcan* dato sensible
(`amount`, `balance`, `merchant`, `description`, `email`, `iban`, `card`,
`transcript`, `fileName`…) incluso si están en la allowlist — sería un error
de quien escribió el esquema, y es mejor perder una métrica que filtrar un
dato.

Hay un test (`__tests__/analytics/catalog.test.ts`) que recorre **todas** las
allowlists y falla si alguna declara una propiedad que dispare ese patrón.

### Excepciones justificadas

| Clave | Por qué está permitida |
|---|---|
| `bankCode` | El código del proveedor soportado (`pichincha`, `guayaquil`), de un conjunto cerrado. Sin él no se puede responder "¿qué tutorial de banco convierte peor?". No es el banco descubierto de los datos del usuario. |
| `accountKind` | `bank` / `wallet` / `cash`. El tipo, no la cuenta. |
| `transactionType` | `expense` / `income`. |
| `transcriptLengthBucket` | Tramo de longitud, nunca el texto ni la longitud exacta. |

## Propiedades prohibidas

Nunca, por ninguna vía: montos, saldos, números o alias de cuenta, nombres de
personas, descripciones de movimientos, comercios, destinatarios, contenido de
estados de cuenta, texto de voz, contenido de correo o de archivos, nombres de
archivo, categorías personalizadas, identificadores bancarios, documentos,
tokens, correos, nombres completos.

Tampoco duraciones en milisegundos ni conteos exactos: van en tramos.

## Contexto global (§29)

Se añade a todos los eventos: `platform`, `appVersion`, `buildNumber`,
`locale`, `environment`, `schemaVersion`. Nada más. Ningún identificador de
dispositivo propio.

## Sesiones

Una vuelta al primer plano abre **sesión nueva solo si la pausa superó los 30
minutos**. Sin esa ventana, mirar una notificación y volver contaría como otra
sesión y la retención saldría inflada. La regla vive en
`AnalyticsService.noteAppActive()`; nadie más la reimplementa.

`captureAppLifecycleEvents: false` en PostHog, precisamente para que el SDK no
mande sus propios eventos de ciclo de vida y duplique `app_opened`.

## Pantallas (§15)

El tracking de pantallas se resuelve en **un** sitio, leyendo la ruta de
expo-router (`services/analytics/screens.ts`). Ninguna pantalla llama a
`screen()` por su cuenta.

Las rutas que no están en el mapa **no generan evento**: los modales pequeños
(ajustar saldo, agregar cuenta, compartir, registrar) no son pantallas a
efectos de producto, y contarlos hace que "pantallas por sesión" deje de
significar nada.

`screen()` solo deja pasar `source`. Ni siquiera por ahí se puede colar un dato.

## Momento de valor (§11)

```
first_value_reached = el usuario llegó a Inicio Y el resumen trajo datos reales
                      (tiene cuentas y tiene movimientos)
```

`valueSource` distingue cómo llegó: `statement_import` o `cash_manual`.

**Se dispara desde Inicio, no al terminar la importación.** Importar un
archivo no es el valor; ver tu dinero ordenado sí. Se emite una sola vez por
usuario (`trackOnce`) y sobrevive a reinicios.

## Eventos únicos (§30)

`trackOnce(event)` guarda la marca en AsyncStorage bajo
`fino.analytics.once.<analyticsId>.<evento>`.

La clave incluye el id de usuario **a propósito**: si fuera solo el evento,
alguien que entra con otra cuenta en el mismo teléfono nunca dispararía su
`first_value_reached` y el embudo de activación mediría de menos.

Eventos únicos: `first_value_reached`, `first_import_completed`,
`first_cash_entry`, `first_pulse_opened`.

Requiere `identify()` previo. Ante un fallo de almacenamiento devuelve
`false`: preferimos perder un hito a inflar la activación con duplicados.

## Errores (§19)

Product analytics recibe **solo una razón de un conjunto cerrado**
(`ErrorReason`): `parser_failed`, `network_error`, `permission_denied`,
`server_error`, `unknown`…

Nunca el mensaje de la excepción, nunca el stack, nunca el contenido del
archivo. Eso es trabajo de un error monitor (Sentry o equivalente), que FINO
todavía **no tiene** — sigue pendiente y no lo sustituye `app_error`.

## Consentimiento (§24)

Tres estados: `unknown` | `granted` | `denied`.

Hoy `requireExplicitConsent` está en **false**, porque FINO todavía no pide
ese consentimiento en ninguna pantalla y activarlo sin la pantalla
correspondiente solo apagaría las métricas sin proteger a nadie más. La
maquinaria está lista: el día que haya pantalla de consentimiento (la LOPDP
ecuatoriana es razón suficiente para revisarlo), se pone en `true` y `unknown`
deja de enviar.

`analytics.setEnabled(false)` es el interruptor del usuario, independiente del
consentimiento. Con cualquiera de los dos en contra, el proveedor pasa a Noop:
desactivar analytics es de verdad desactivarlo, no un `if` repartido por el
código.

## Versionado (§28)

`ANALYTICS_SCHEMA_VERSION` viaja en cada evento como `schemaVersion`. Sube en
1 cuando cambia la *forma* de los eventos (se renombra una propiedad, cambia
el significado de un bucket), y el cambio se anota aquí abajo.

| Versión | Fecha | Cambio |
|---|---|---|
| 1 | 2026-09 | Catálogo inicial. |

## Cómo añadir un evento nuevo

1. Añádelo a `AnalyticsEvent` en `events.ts`, en snake_case.
2. Declara su allowlist en `EVENT_SCHEMA` (`schema.ts`). **Si te lo saltas, no
   compila.**
3. Si necesitas un valor nuevo, añádelo como enum en `events.ts`. No mandes
   strings sueltos.
4. Si es una cantidad, bucketízala en `buckets.ts`. No mandes el número.
5. Llama a `track(AnalyticsEvent.TuEvento, { ... })` desde la fachada.
6. Corre `npx jest __tests__/analytics`. El test del catálogo comprueba que no
   metiste una propiedad sospechosa.

Lo que **no** hay que hacer nunca: llamar al SDK desde una pantalla, inventar
el nombre de un evento, o pasar el objeto de datos completo "por si acaso".
