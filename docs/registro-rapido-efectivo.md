# Registro rápido de efectivo

> "Pagué algo y lo registré antes de guardar el cambio en mi bolsillo."

Registrar un gasto en efectivo debe tomar entre 1 y 3 segundos en el caso normal.
El formulario completo sigue existiendo, pero como opción secundaria ("Más
detalles"), no como camino por defecto.

## El punto de partida

Antes de esto, Fino **no tenía ninguna forma de crear un movimiento a mano**: no
existía `POST /transactions`, no había pantalla de registro manual, y no existía
cuenta ni proveedor de efectivo. `TransactionSource.Manual` estaba declarado en el
dominio desde el primer día y ningún camino del backend lo producía.

## La regla que ordena todo el diseño

**Un solo punto de escritura.** `QuickTransactionService.CreateAsync` es el único
sitio del backend que crea un movimiento escrito a mano, y su secuencia es la misma
que ya usaban las otras dos vías de entrada (`ImportService` y
`EmailIngestionPipeline`):

```
resolver cuenta → deduplicar → sesión de categorización → Transaction.Create
    → account.ApplyMovement → SaveChanges
```

De ahí se derivan las consecuencias:

- No hay tabla nueva para el efectivo. Un gasto en efectivo es un `Transaction`
  normal con `Source = Manual`.
- No hay lógica de categorización propia en el servidor. Cuando el cliente no manda
  categoría, decide el mismo `ICategorizationEngine` que categoriza importaciones y
  correos, así que una regla que la persona ya enseñó a Fino vale igual escribiendo
  "almuerzo" a mano que leyendo un extracto.
- El efectivo es una `FinancialAccount` con proveedor `EFECTIVO`. Cero ramas
  `if (esEfectivo)` en saldos, Home, Movimientos o Estadísticas.
- Todos los puntos de entrada externos (widget de iOS, widget de Android, atajos del
  icono, App Intent, Siri) abren el mismo deep link `fino:///registrar`, que abre el
  mismo sheet, que llama al mismo endpoint. Añadir un punto de entrada no añade una
  forma de guardar.

## Niveles de velocidad

| Nivel | Interacción | Dónde vive |
| --- | --- | --- |
| 1 toque | Un frecuente | `SuggestionChips` + `QuickEntrySuggestionService` |
| 2-3 acciones | `+` → monto → Guardar | `AmountKeypad` + `QuickCashEntrySheet` |
| Lenguaje natural | "5 almuerzo", "8 uber ayer" | `utils/quickEntry/parseNaturalEntry.ts` |
| Voz | "Gasté seis dólares en almuerzo" | `hooks/useVoiceEntry.ts` |
| Formulario completo | Solo para editar detalles | `app/movimiento/nuevo.tsx` |

## Voz y privacidad (§14)

El reconocimiento lo hace **el sistema operativo del teléfono**, mediante
`expo-speech-recognition`, que envuelve `SFSpeechRecognizer` (iOS) y
`SpeechRecognizer` (Android).

- **Fino no manda audio a ningún servidor propio ni de terceros.** Lo único que sale
  del módulo nativo es el texto ya transcrito.
- **Fino no guarda grabaciones.** La sesión se abre con `recordingOptions:
  { persist: false }`, de forma explícita y no por omisión.
- El micrófono se abre solo mientras hay un dedo sobre el botón. Es la razón de que
  el gesto sea "mantener pulsado" y no "tocar para empezar / tocar para parar": así
  no puede quedarse abierto por descuido.
- El permiso se pide **la primera vez que alguien usa la voz**, nunca al abrir la app
  ni al abrir el sheet. Esa primera pulsación **solo pide el permiso**: no arranca la
  escucha. Conceder el permiso obliga a levantar el dedo del botón, así que empezar a
  grabar ahí dejaría el micrófono abierto sin que nadie lo sujete. Fino avisa
  ("Listo. Mantén pulsado el micrófono y habla") y espera al siguiente gesto.
- La transcripción se parsea en el dispositivo y se descarta al cerrar el sheet.

El sistema operativo puede decidir por su cuenta si resuelve el reconocimiento en el
dispositivo o en el servidor del fabricante (Apple o Google); eso lo controla la
persona desde los ajustes de su teléfono, no Fino.

Si algún día se cambiara a un servicio externo de transcripción, **hay que
documentarlo aquí y decírselo a la persona**. `lib/speech/speechRecognizer.ts` es el
único sitio del que cuelga esa decisión.

El módulo nativo se carga de forma perezosa y tolerante a fallo: en Expo Go, en una
build anterior a la dependencia o en los tests, `loadSpeechRecognizer()` devuelve
`null`, el botón de micrófono no se dibuja y el resto del registro rápido funciona
igual.

## Parser de lenguaje natural

Determinístico y local (`utils/quickEntry/parseNaturalEntry.ts`). **No se manda
nada a una IA externa.** Produce confianza por campo, y la única condición que
bloquea el guardado es el monto: sin categoría se guarda igual, y sin fecha se usa
"ahora".

El parser propone un **código** de categoría (`COMIDA`, `TRANSPORTE`...) del catálogo
que ya existe en el backend. `resolveCategory.ts` lo resuelve contra las categorías
**reales** de la persona; si no encaja ninguna, se manda el movimiento sin categoría
y decide el motor de reglas del servidor. Nunca se inventa una categoría.

**El autoguardado de texto natural está apagado** (`AUTO_SAVE_ENABLED = false`).
La primera versión muestra una vista previa compacta y espera a que la persona toque
Guardar.

## Calculadora

`utils/quickEntry/safeCalculator.ts` acepta `+` y `-`. **No usa `eval()`**:
tokeniza, valida y suma. Lo que no encaje en la gramática no se evalúa a medias --
se devuelve `null`.

## Idempotencia (§36)

El cliente genera un `clientRequestId` **antes** de mandar la petición. Dos toques
en Guardar mandan el mismo valor y el servidor devuelve el movimiento ya creado en
lugar de crear otro. La garantía real es un índice único parcial en Postgres
(`IX_transactions_UserId_ClientRequestId`), no la comprobación previa: solo la base
de datos puede arbitrar dos peticiones que llegan a la vez.

Al reintentar tras un fallo se manda **el mismo** identificador, para el caso en que
el primer envío sí llegó y solo se perdió la respuesta.

## Saldo de efectivo (§24-25)

- Declarar el efectivo inicial ancla el saldo (`mode: "Anchor"`). No declararlo
  **no bloquea** el registro de movimientos.
- Corregirlo después (`mode: "Adjustment"`, el valor por defecto) registra la
  diferencia como un **movimiento de ajuste visible**, no como un cambio silencioso
  del saldo. Los movimientos históricos no se tocan nunca. Ambas operaciones quedan
  en el registro de auditoría.

## Analítica (§38)

La métrica que decide si esto funcionó es **`TIME_TO_CASH_ENTRY`**: la mediana entre
`quick_entry_opened` y `quick_entry_saved`, con objetivo por debajo de 3 segundos
para el registro numérico simple.

Nunca viajan a analítica: monto, descripción, comercio, saldo, cuenta, nombre de
categoría ni texto de voz. `services/analytics.ts` incluye un guard en desarrollo
que avisa si alguna propiedad tiene pinta de dato financiero.

## Offline

**No se implementó cola offline**, porque la app no tiene hoy infraestructura de
sincronización diferida y el spec (§34) pide explícitamente no inventarla sin
revisar el proyecto. Si la creación falla, el sheet **no se cierra**, conserva todo
lo escrito y ofrece Reintentar.

## Endpoints

| Método | Ruta | Para qué |
| --- | --- | --- |
| GET | `/api/v1/quick-entry/bootstrap` | Cuenta de efectivo, saldo y sugerencias, en una sola llamada |
| POST | `/api/v1/quick-entry/transactions` | Registrar un movimiento a mano |
| PUT | `/api/v1/quick-entry/transactions/{id}` | Corregir uno propio (409 si vino de un banco) |
| DELETE | `/api/v1/quick-entry/transactions/{id}` | Deshacer (solo movimientos manuales) |
| POST | `/api/v1/quick-entry/cash-balance` | Declarar o corregir el efectivo |

---

# Retiros de efectivo

Un retiro no es un gasto: es dinero cambiando de sitio. La regla del dominio es que
**mover dinero entre cuentas propias no es gastar dinero**, y vale igual para
banco→banco, banco→wallet y banco→efectivo.

## La especialización, no el sistema paralelo

`InternalTransferService` empareja dos movimientos que YA EXISTEN. Un retiro
normalmente tiene una sola pata: el banco dice `-$100` y no hay ningún `+$100`
porque el efectivo no lo reporta nadie.

Así que `WithdrawalService` hace exactamente una cosa distinta: **cuando falta la
pata de efectivo, la construye**. A partir de ahí llama al mismo
`Transaction.MarkAsInternalTransfer` que cualquier transferencia.

Eso es lo que hace que las estadísticas salgan gratis. `AnalyticsService`,
`InsightEngine`, `PulseEngine` y el resumen de Home ya filtran `!IsInternalTransfer`
en todas sus métricas de gasto — no se tocó ni una para esta funcionalidad.

## Sin esquema nuevo

No hay migración. El estado de revisión se deriva de datos que ya existen:

| Estado | Cómo se representa |
| --- | --- |
| Confirmado | `IsInternalTransfer` + `InternalTransferLinkId` |
| Rechazado | `CategoryManuallySet` — la persona clasificó el movimiento a mano |
| Sin revisar | Lo que el detector marque y no esté en los dos anteriores |

El aprendizaje del §14 tampoco necesita entidad nueva: `CategorizationRule` ya tiene
patrón, banco (`ProviderCode`), dueño y contador de usos. Una regla aprendida de un
retiro es una regla de categorización normal que apunta a "Transferencias".

## El detector

`WithdrawalDetector` es dominio puro, sin base de datos. Devuelve el mismo resultado
se llame desde la importación, desde la bandeja o desde el detalle de un movimiento.

**Lo primero que comprueba son las comisiones.** `COMISION RETIRO ATM` lleva todas
las señales de retiro posibles y sin embargo es un gasto real. Confundirlo sacaría
un gasto de las estadísticas, así que la exclusión es absoluta y va antes que
cualquier otra regla — no resta puntos: descarta.

Puntuación: término fuerte (`RETIRO`, `CAJERO`, `RETINJ`, `WITHDRAWAL`) 60; señales
de apoyo (`ATM`, `VENTANILLA`, `INTERNACIONAL`) 15 cada una, topadas en dos; monto
múltiplo de 10 (forma de cajero) 10; ser un egreso 10. Alta desde 75, media desde
45. `ATM` a secas nunca llega a alta, porque hay comercios que se llaman así.

## Lo que NO se implementó

- **§16-17, split de comisión incluida.** Partir un `-$100.50` en `$100` a efectivo
  y `$0.50` de comisión es un concepto de dominio nuevo (una transacción bancaria
  con dos partes lógicas) que toca saldos, deduplicación y estadísticas.
- **§18, retiros internacionales.** Se detectan (`ATM WITHDRAWAL`, `RETIRO
  INTERNACIONAL` entran al mismo detector) pero no se auto-concilian: con conversión
  de moneda, el monto debitado no es el efectivo recibido.
- **§19, conciliación automática.** Se cuenta cuántas veces se confirmó cada patrón
  y se ofrece recordar la regla, pero Fino nunca concilia sin que la persona toque
  el botón.
