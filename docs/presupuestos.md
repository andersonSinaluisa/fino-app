# Presupuestos y Comprometido

## Regla central

```
Tu dinero      = suma de EstimatedBalance de las cuentas no archivadas
Comprometido   = CommittedMoneyCalculator(candidatos)   (valor real, nunca recortado)
Disponible     = max(Tu dinero − Comprometido, 0)
Sobrecomprometido = max(Comprometido − max(Tu dinero, 0), 0)
```

`CommittedMoneyCalculator` (Nexo.Domain/Budgets) es la **única** implementación.
`CommittedMoneyService` es el único que le arma los candidatos. Home, el desglose
de Comprometido, "Dinero disponible", "Tu Plan" y la previsualización al crear o
editar un presupuesto consumen ese resultado; la app no calcula nada.

## Presupuestado ≠ comprometido

| ReserveFunds | Efecto |
| --- | --- |
| `false` (límite) | Solo mide el gasto. No toca Disponible. |
| `true` (reserva) | Lo que queda sin gastar **en la ventana actual** entra en Comprometido. |

Un presupuesto pausado, una ventana pasada o futura, o una reserva ya gastada no
reservan nada.

## Fuentes de Comprometido

1. **Presupuestos reservados** — `restante` de cada presupuesto activo con reserva en su ventana actual.
2. **Próximos pagos** — pagos recurrentes (mismo detector que Estadísticas: ≥ 2 meses
   locales distintos, monto ±25%) vistos el mes pasado y todavía no este mes, cuya
   fecha esperada no pasó hace más de 5 días. Es una estimación y se muestra como tal.

No existe todavía una entidad de obligaciones/pagos programados; cuando exista, será
un tercer tipo de candidato en `CommittedMoneyService` y en ningún otro lugar.

### Deduplicación

Un presupuesto reservado ya tiene destino para toda su categoría: un próximo pago de
esa misma categoría se absorbe primero en la reserva y solo cuenta lo que no quepa
(resultado por categoría = `max(reserva, pagos)`, nunca la suma). El desglose muestra
el pago como "Incluido en tu presupuesto X". Candidatos sin categoría no se
deduplican (no se puede probar que sean el mismo dinero). El mismo candidato
(fuente + clave) cuenta una sola vez.

## Qué movimiento consume qué presupuesto

- Solo movimientos `Posted`/`Pending`, **nunca transferencias internas confirmadas**.
- Fechas en la zona horaria del usuario (una cena a las 22:00 del 28 feb en
  Guayaquil es de febrero aunque en UTC ya sea marzo).
- **Presupuesto por categoría**: gastos de la categoría; ingresos en la misma
  categoría = reembolso/reversión y restan (el gastado nunca baja de 0).
- **Presupuesto general** (sin categoría): gastos cuya categoría no tiene un
  presupuesto activo en esa fecha, incluidos los sin categoría. Nunca toma
  ingresos como reembolso.
- **Solapamiento**: no pueden existir dos presupuestos **activos** de la misma
  categoría (o dos generales) cuyos rangos de fechas se crucen, aunque tengan
  períodos distintos. Se valida en el backend (409). Pausar libera la categoría;
  reanudar vuelve a validar.

Con estas reglas cada movimiento lo consume como mucho un presupuesto activo.

## Períodos e historial

`Weekly` (7 días), `Biweekly` (14), `Monthly` (anclado al día de inicio, recortado
en meses cortos; empezar el 1 = mes calendario) y `Custom` (rango explícito, máx. 1
año, no recurrente). Las ventanas **se derivan** de la definición; no hay filas por
período que crear o cerrar al cambiar de mes. El único dato histórico que cambia es
el monto: `budget_amount_revisions` guarda "desde tal fecha vale X", y editar el
monto aplica desde la ventana actual sin reescribir las anteriores. Período y fecha
de inicio no se editan (sería reescribir el historial): se crea otro presupuesto.

Nada derivado se persiste: gastado, restante, reservado, comprometido y disponible
se recalculan en cada lectura, así que editar o borrar un movimiento se refleja sin
nada que sincronizar.

## Estados

`Normal` < 70% · `Attention` 70–89.9% · `NearLimit` 90–99.9% · `Exceeded` ≥ 100%
(`BudgetCalculator.LevelFor`). La app acompaña cada estado de texto e ícono, no
solo color.

## Insights (deterministas)

`BudgetInsightRules`: excedido, ritmo que superaría el monto (desde el 5.º día de la
ventana), uso ≥ 70%, monto diario restante y comparación con el período anterior
**al mismo día** (no contra el mes completo). Home muestra como máximo uno, y solo de
los tipos marcados `IsHomeWorthy`. Cada insight trae una versión sin montos para
"ocultar cantidades".

## API

| Método | Ruta | |
| --- | --- | --- |
| GET | `/api/v1/budgets?date=yyyy-MM-dd` | Lista + totales (presupuestado, gastado, restante, reservado) + insight principal. Reemplaza a un `/budgets/summary` separado. |
| GET | `/api/v1/budgets/{id}?date=` | Detalle, insights e historial (6 períodos). |
| GET | `/api/v1/budgets/{id}/movements?date=` | Exactamente los movimientos sumados. |
| POST | `/api/v1/budgets` | Crear. 409 si se solapa. |
| POST | `/api/v1/budgets/preview` | Impacto en Comprometido/Disponible con el mismo motor. |
| PUT | `/api/v1/budgets/{id}` | Editar / pausar / reanudar. |
| DELETE | `/api/v1/budgets/{id}` | Eliminar (los movimientos no se tocan). |
| GET | `/api/v1/finance/committed` | Tu dinero, Comprometido con desglose, Disponible. |

## Persistencia

Migración `AddBudgets` (solo aditiva): `budgets` y `budget_amount_revisions`, con
FKs en cascada a `users` y `categories`, checks de monto > 0 y fin ≥ inicio, índices
`(UserId, IsActive)`, `(UserId, CategoryId, IsActive, StartDate, EndDate)`,
`(CategoryId)` y único `(BudgetId, EffectiveFrom)`.
