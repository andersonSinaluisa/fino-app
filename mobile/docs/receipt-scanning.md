# Escaneo de facturas

Registrar un gasto fotografiando la factura, sin transcribir nada.

## La decisión que define todo lo demás

**La imagen nunca sale del teléfono.**

El OCR corre en el dispositivo con ML Kit y el parser es TypeScript
determinístico. Ni la foto, ni el texto reconocido, ni el RUC, ni el nombre
del comercio viajan a ningún servidor — ni de FINO, ni de terceros.

Una factura ecuatoriana lleva RUC, a veces el nombre del cliente, la dirección
del local y los últimos dígitos de una tarjeta. Mandar todo eso a un servicio
externo para leer un número sería un intercambio pésimo.

## Pipeline

```
cámara / galería      pickImage.ts        permiso al tocar, no al instalar
        ↓
prepareReceiptImage   prepareImage.ts     orientación, tamaño, y fuera el EXIF
        ↓
ReceiptExtractor      extractor.ts        ML Kit on-device
        ↓
parseReceiptText      parseReceiptText.ts el parser ecuatoriano
        ↓
ReceiptExtractionResult
        ↓
findReceiptMatches    matchTransaction.ts ¿ya está en el banco? ¿es duplicado?
        ↓
receiptToDraft        toDraft.ts          → el MISMO draft del registro rápido
        ↓
useCreateQuickTransaction                 → el MISMO endpoint
```

La pantalla no conoce al extractor ni al parser. Cambiar de motor (§14) es
escribir otra clase que cumpla `ReceiptExtractor`.

**No hay un sistema de gastos para facturas.** El escaneo es el cuarto
productor del mismo borrador:

```
teclado ─┐
texto ───┤
voz ─────┤
factura ─┤
         ↓
   TransactionDraft → CreateQuickTransaction
```

## El parser (§12-13)

Todo el trabajo real está aquí, y es lo que más tests tiene.

El problema central es **no confundir el total con otra cifra**:

```
SUBTOTAL      20.00
IVA 15%        3.00
TOTAL         23.00
EFECTIVO      25.00
CAMBIO         2.00
```

El gasto es **23.00**. Un parser que busque "el número más grande" devuelve
25.00; uno que tome la última cifra devuelve 2.00.

Cómo se resuelve:

1. Se clasifica **cada línea** por su etiqueta, no por su número.
2. Las etiquetas **excluyentes** se miran **primero**: `SUBTOTAL`, `IVA`,
   `BASE IMPONIBLE`, `CAMBIO`, `VUELTO`, `EFECTIVO`, `RECIBIDO`, `TARJETA`,
   `PROPINA`, `SERVICIO`, `DESCUENTO`. Una línea excluida no puede ganar,
   tenga el número que tenga.
3. Sobre lo que queda se buscan las etiquetas de total, con peso:
   `TOTAL A PAGAR` / `VALOR TOTAL` / `IMPORTE TOTAL` (1.0) antes que un
   `TOTAL` suelto (0.9). Con el mismo peso gana la línea más abajo.
4. Se comprueba que `subtotal + IVA ≈ total`. Si no cuadra **se avisa, no se
   corrige**: corregir el total en silencio es confiar ciegamente en el OCR
   por la puerta de atrás.

Detalles que importan en Ecuador:

- IVA al 15% y al 12%, `IVA`, `I.V.A.`, `IVA 15%`.
- Fechas `DD/MM/AAAA`, `11-SEP-2026`, ISO. Se rechaza el futuro y todo lo
  anterior al año 2000 (casi siempre es el OCR leyendo mal el número de
  autorización).
- Importes con punto o coma decimal, y con separador de miles en los dos
  formatos.
- `EFECTIVO` sugiere la cuenta Efectivo. `VISA`/`TARJETA` **no** adivina qué
  banco fue: eso lo decide la conciliación o la persona (§21).

## Conciliación y duplicados (§22, §23, §26)

Dos preguntas distintas, un solo motor de puntuación — el mismo enfoque que ya
usan `WithdrawalDetector` e `InternalTransferService`:

| | Qué busca | Qué hace |
|---|---|---|
| **Conciliación** | El banco ya reportó este gasto | No crear otro |
| **Duplicado** | La persona ya lo anotó a mano | Avisar antes de guardar |

Puntuación: monto exacto (+50) o cercano (+30), mismo día (+25) / ±1 día
(+18) / ±3 días (+8), parecido del comercio (hasta +25). Alto ≥ 75.

Descartes duros: fuera de 7 días, ingresos, y **patas de transferencia
interna** — mover dinero propio no es gastar, así que un retiro conciliado
nunca puede ser la contraparte de una factura.

**Si dos candidatos quedan a menos de 10 puntos, no se sugiere ninguno.** Con
tres movimientos del mismo monto el mismo día no hay un ganador honesto: la
persona decide (§23, §47).

## Qué se mide y qué no (§37)

Sale: `source`, `analysisResult` (complete/partial/failed), `totalDetected`,
`dateDetected`, `merchantDetected`, `matchedExisting`, `durationBucket`,
`confidence` del match.

**No sale nunca:** el monto, el comercio, la fecha exacta, el texto OCR, la
imagen, el RUC, el número de factura. Los booleanos `*Detected` miden la
calidad del OCR sin leer el recibo. La allowlist de `services/analytics/schema.ts`
lo impone, y hay un test que falla si alguien añade una propiedad sensible.

## Decisiones que se apartan del spec

**El `+` NO se convirtió en un menú.** §2 pedía
`Efectivo / Escanear factura / Importar` al tocar el `+`. Eso añade un toque
delante del camino rápido y rompe la regla anterior de que registrar efectivo
tome entre 1 y 3 segundos — la métrica que mide
`quick_entry_saved.durationBucket`. El `+` sigue abriendo directo el teclado;
"Escanear factura" vive dentro del sheet, a un toque, que es donde de verdad
es de primer nivel.

**No se construyó una cámara a medida.** §5 describía una pantalla propia con
marco guía. Se usa la cámara del sistema vía `expo-image-picker`: ya trae
enfoque, flash, HDR y, en iOS, recorte automático de documentos — mejor que
cualquier marco que dibujemos — y ya está traducida y es accesible.
`allowsEditing` da el recorte y la rotación de §7 sin escribir un editor de
imágenes. Si más adelante hace falta la cámara propia, es otra implementación
de la misma función.

## Lo que falta

- **Guardar la foto con el movimiento (§27).** Requiere backend nuevo: hoy no
  existe ninguna entidad de adjuntos ni almacenamiento de blobs. Es FASE 3.
- **Asociar la factura a un movimiento existente (§22).** La detección está y
  se avisa en pantalla; el botón "Asociar" necesita el adjunto de arriba para
  tener sentido.
- Items por producto (§33), múltiples fotos (§31), PDF (§32), dividir (§34).

## Instalación

```bash
npx expo install expo-image-picker expo-image-manipulator
npm install @react-native-ml-kit/text-recognition
npx expo prebuild --clean
```

Los tres se cargan con `require` protegido: sin ellos la app compila y corre,
y el flujo avisa de que el escaneo no está disponible en ese build en vez de
romperse.
