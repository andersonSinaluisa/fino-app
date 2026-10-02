# Tarjetas de crédito

> Una compra con tarjeta es un gasto. Pagar esa tarjeta no es otro gasto.
> El cupo de la tarjeta no es dinero. La deuda futura no es dinero comprometido hoy.

## Cómo se modela

**Una tarjeta es una cuenta.** No hay una estructura paralela: es un
`FinancialAccount` de tipo `CreditCard`. Por eso sus movimientos, importaciones,
deduplicación, categorías, reglas, movimientos divididos y presupuestos son los
que cualquier cuenta ya tiene. Lo que una cuenta no sabe vive en tablas propias:

| Tabla | Qué guarda | Qué NO guarda |
| --- | --- | --- |
| `credit_cards` | Cupo, día de corte, día máximo de pago, marca, «reservar próximo pago». Una fila por cuenta tarjeta. | Número completo, CVV, PIN, credenciales. Nombre, banco, últimos 4 y moneda ya están en la cuenta. |
| `credit_card_statements` | Solo las cifras **oficiales** de un estado (importado o escrito a mano): fecha de corte, fecha máxima, total a pagar, pago mínimo, importación de origen. | Lo pagado, lo pendiente y el estado: se derivan siempre de los movimientos. |
| `installment_plans` + `installments` | Compras diferidas: compra de origen, número de cuotas, monto de cuota, calendario (corte y fecha de pago de cada cuota), tasa informativa, precancelación. | Una segunda copia de la compra. |
| `transactions.CardMovementType` | Qué es cada movimiento de una tarjeta: `Purchase`, `Refund`, `Payment`, `Interest`, `Fee`, `CashAdvance`, `Adjustment`. Null fuera de una tarjeta. | — |

**Signo.** En una tarjeta, una compra es un egreso de la cuenta: el saldo de la
tarjeta es **negativo** cuando se debe. Deuda actual = `max(−saldo, 0)`.

## Reglas financieras (fuente única: el dominio)

`CreditCardMovementRules` decide qué significa cada tipo:

| Tipo | Deuda | ¿Gasto? | ¿Ingreso? |
| --- | --- | --- | --- |
| Compra | sube | **sí**, con su categoría (y su división) | no |
| Interés | sube | sí, categoría *Intereses* (costo financiero, no consumo) | no |
| Comisión | sube | sí, categoría *Comisiones e impuestos* | no |
| Devolución | baja | **resta** del gasto de su categoría y de su presupuesto | **nunca** |
| Pago | baja | no | no |
| Avance en efectivo | sube | no (el dinero pasa a tu efectivo) | no |
| Ajuste | sube o baja | no | no |

Los tipos neutros (pago, avance, ajuste) se marcan con el mismo
`IsInternalTransfer` que ya excluía las transferencias entre cuentas propias,
así que **todas** las cifras de ingreso/gasto que ya existían (Home,
Estadísticas, presupuestos, insights, pulsos, pagos recurrentes) los excluyen
sin una segunda regla. `MoneyFlows` (Application) es la regla única para los
totales: gasto = gastos − devoluciones de tarjeta; ingreso = ingresos sin
devoluciones de tarjeta.

`CreditCardCalculator` es la fuente única de toda cifra de una tarjeta:
deuda, cupo disponible, utilización, total/mínimo/pagado/pendiente/estado de
cada estado de cuenta, próximo pago, deuda diferida y lo que la tarjeta aporta
a Comprometido. El cliente solo presenta.

### Ciclo de facturación (`BillingCalendar`)

* Una compra del 10 sep con corte 15 va al estado que cierra el 15 sep; una del
  16 sep, al siguiente. Nunca "el mes calendario".
* Una compra **en** la fecha de corte pertenece a ese estado.
* Día configurado que el mes no tiene → **último día del mes** (corte 31 cierra
  el 28/29 de febrero, el 30 de abril…).
* Fecha máxima de pago: el día de pago en el mismo mes si es posterior al corte,
  si no en el mes siguiente. Si un mes corto hace coincidir ambos, se paga al día
  siguiente del corte. Corte y pago no pueden ser el mismo día.
* Las fechas son del calendario **local** de la persona (America/Guayaquil por
  defecto): una compra a las 22:30 del 15 es del 15.
* Un estado declarado (importado o escrito) cuya fecha de corte está a ≤ 7 días
  de la configurada la reemplaza (los bancos corren el corte por fines de semana).

### Estados de cuenta

* **Total**: el del banco si se declaró; si no, `deuda al corte − cuotas aún no
  facturadas`. Un saldo impago del estado anterior se arrastra solo, porque sigue
  siendo deuda.
* **Pagado**: pagos con fecha posterior al corte, hasta el corte siguiente (para
  el último estado, hasta hoy), topado al total.
* **Pendiente**: total − pagado. En un estado calculado, nunca más de lo que se
  debe hoy (una devolución posterior lo baja); en uno declarado, la cifra del banco.
* **Estado**: `Open` (ciclo actual), `Closed`, `PartiallyPaid`, `Paid`,
  `Overdue` (pasó la fecha máxima sin pagarse completo). Se deriva, no se guarda.

### Próximo pago y Comprometido

* Próximo pago = lo pendiente del **último estado cerrado**; si ya está pagado, el
  total proyectado del **ciclo abierto** (sus compras + la cuota que factura).
* Nunca toda la deuda: las cuotas de estados futuros son *deuda futura*.
* Si la tarjeta tiene «Reservar próximo pago», ese monto entra a
  `CommittedMoneyCalculator` como fuente `CreditCard` («Tarjetas»). Si no, la
  deuda se ve pero no baja Disponible.
* No hay doble conteo con presupuestos: la compra ya consumió su presupuesto
  (que solo reserva lo que falta gastar) y la tarjeta reserva lo que falta pagar.
  Ejemplo: presupuesto Comida $300 reservado + compra Comida $100 con tarjeta →
  presupuesto $200 + tarjeta $100 = $300.

### Tu dinero y Disponible

«Tu dinero» suma solo cuentas que no son pasivo (`AccountKinds`). El saldo de una
tarjeta (deuda) y su cupo disponible **nunca** se suman a Tu dinero, Disponible,
el saldo de Home, el gráfico de saldo de Estadísticas ni el widget. Disponible =
`max(Tu dinero − Comprometido, 0)`. Fino no tiene «patrimonio»; si se agrega,
será activos − pasivos y nunca se mezclará con Disponible.

### Compras a cuotas

* La compra es **un** gasto de su monto completo en su fecha (Laptop $1,200 en
  septiembre). El plan solo dice cómo esa deuda se vuelve exigible: $100 por estado.
* La cuota 1 se factura en el corte del ciclo de la compra (configurable).
* «4/12» = cuotas ya facturadas. «Pendiente» = cuotas aún no facturadas.
* Precancelar: todo lo pendiente pasa a ser exigible desde hoy.
* La tasa es informativa: los intereses reales llegan como movimientos de
  *Intereses* del estado (aplicarla además los contaría dos veces).
* Si cambian los días de corte/pago, las cuotas no facturadas se recalendarizan.

### Pagos

`POST /credit-cards/{id}/payments` crea **un** pago como dos patas vinculadas
(banco −, tarjeta +), ambas neutras. Sin cuenta de origen (pagó desde una cuenta
fuera de Fino) solo se crea la pata de la tarjeta. Deshacer el pago (borrar
cualquiera de las patas manuales) borra las dos. Moneda distinta: se rechaza
(Fino no inventa tipos de cambio).

### Conciliación e importación

* **Débito del banco ya importado** que es un pago: `GET .../payments/suggestions`
  lo sugiere (mismo monto que un pago sin pareja ±5 días, o texto «PAGO TARJETA»,
  «PAGO VISA», últimos 4…). `POST .../payments/link` lo confirma. Nunca se
  vincula solo.
* **Pago registrado a mano que luego aparece en el estado** (del banco o de la
  tarjeta): mismo monto y sentido ±5 días contra una pata manual de un pago →
  duplicado **probable** (queda para revisar, no cuenta). Nunca se fusiona solo.
* **Compra escrita a mano que luego trae el estado** («Supermaxi» vs
  «SUPERMERCADO SUPERMAXI»): el deduplicador de siempre (huella, referencia,
  monto + fecha + descripción).
* **Importar un estado de tarjeta** usa el mismo pipeline (parsers, mapeo manual,
  categorías, vista previa). Además: se corrigen los signos si el archivo trae las
  compras en positivo (`CreditCardStatementImport.Orient`), se clasifica cada fila
  (pago, devolución, interés, comisión, avance, compra), se leen del encabezado
  corte/fecha máxima/total/mínimo/cupo y, al confirmar, se declaran como estado
  oficial (trazable a la importación) y se actualiza el cupo. Las líneas «CUOTA n/m»
  de una compra que ya está diferida en Fino se dejan fuera con su motivo.
* El «saldo» de un archivo de tarjeta nunca ancla la deuda (no es la deuda en el
  signo de Fino y el total excluye cuotas futuras): para eso está «Actualizar deuda».

### Archivar

`DELETE /credit-cards/{id}` **archiva**: deja de aparecer y de reservar dinero; sus
movimientos, estados, cuotas e historial se conservan. `POST .../restore` la
reactiva. El borrado definitivo es el de cualquier cuenta (Privacidad), en cascada.

## API

| Método | Ruta | |
| --- | --- | --- |
| GET | `/api/v1/credit-cards?includeArchived=` | Tarjetas + totales (deuda, próximos pagos, comprometido). |
| GET | `/api/v1/credit-cards/{id}` | Resumen, ciclo actual, último estado, este mes, cuotas activas, últimos movimientos. |
| POST | `/api/v1/credit-cards` | Crear. Solo últimos 4 dígitos (400 si mandan más). |
| PUT | `/api/v1/credit-cards/{id}` | Editar o completar una tarjeta creada antes del módulo. |
| POST | `/api/v1/credit-cards/{id}/debt` | «Actualizar deuda» con lo que dice el banco. |
| DELETE / POST | `/api/v1/credit-cards/{id}` · `/restore` | Archivar / restaurar. |
| GET / PUT | `/api/v1/credit-cards/{id}/statements` | Estados (derivados) / declarar cifras oficiales. |
| DELETE | `/api/v1/credit-cards/{id}/statements/{closingDate}` | Quitar cifras oficiales. |
| GET / POST | `/api/v1/credit-cards/{id}/installments` | Planes / diferir una compra. |
| POST / DELETE | `/api/v1/credit-cards/{id}/installments/{planId}/cancel` · `/{planId}` | Precancelar / quitar plan. |
| POST | `/api/v1/credit-cards/{id}/payments` | Registrar pago (idempotente con `clientRequestId`). |
| GET / POST | `/api/v1/credit-cards/{id}/payments/suggestions` · `/link` | Conciliación de pagos. |
| POST | `/api/v1/credit-cards/{id}/movements/{transactionId}/type` | Reclasificar un movimiento. |

Los movimientos de una tarjeta son `GET /transactions?accountId={id}` y se
registran con `POST /quick-entry/transactions` (`financialAccountId` = tarjeta,
`cardMovementType` opcional; sin `direction`, el backend la deduce del tipo).
Comprometido (`GET /finance/committed`) suma la fuente `credit_card`.

## Migración

`20261002154619_AddCreditCards` (script idempotente:
`scripts/sql/add_credit_cards.sql`). Además de las tablas, clasifica los
movimientos de cuentas tarjeta que ya existían y vuelve neutros sus pagos (hasta
hoy un «PAGO TARJETA» importado en la tarjeta contaba como ingreso). Al arrancar,
el seeder agrega la categoría del sistema *Intereses*.

## Deudas técnicas conocidas

* Rankings por comercio, días pico y entre semana/fin de semana de Estadísticas,
  insights y pulsos usan gasto **bruto**: no restan devoluciones de tarjeta (sí lo
  hacen KPIs, categorías, Home y presupuestos).
* Una línea «CUOTA n/m» sin plan en Fino (compra anterior a usar Fino) entra como
  compra del monto de la cuota; la deuda total se corrige con «Actualizar deuda».
* Sin multimoneda: pago y tarjeta deben estar en la misma moneda.
* Los parsers de estado de tarjeta son los genéricos (CSV/XLSX/HTML con columnas
  reconocibles o mapeo manual). No hay parser de PDF.


