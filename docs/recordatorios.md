# Recordatorios

Notificaciones para que la persona vuelva a Fino cuando hay algo útil que hacer.
Reglas en `Nexo.Application/Reminders/ReminderRules.cs` (funciones puras, con tests),
carga de datos y política de envío en `ReminderService.cs`, y `ReminderWorker`
(cada 30 min) en `Nexo.Workers`.

| Recordatorio | Tipo | Cuándo | Prioridad |
| --- | --- | --- | --- |
| Pago de tarjeta próximo | `CardPaymentDue` | Estado cerrado con saldo, de 3 a 1 días antes del vencimiento (una vez) y el mismo día | 1 |
| Estado de cuenta nuevo | `StatementAvailable` | 2 a 5 días después del corte, si el estado no se registró | 2 |
| Presupuesto al límite / superado | `BudgetThreshold` | Al pasar 80 % y 100 % del período (el 80 % no llega si ya llegó el 100 %) | 3 |
| Cuenta desactualizada | `AccountNeedsUpdate` | Cuenta de importación manual sin actualizar hace 14 días; como máximo 1 aviso cada 14 días | 4 |
| Resumen semanal | `WeeklySummary` | Domingo 19:00–21:59, solo si hubo movimientos en la semana | fuera del tope |

Política:

- Horario: de 10:00 a 19:59 (hora de Ecuador), salvo el resumen semanal.
- Máximo un recordatorio por día (el de mayor prioridad); el resumen semanal no cuenta.
- "Estado nuevo" y "cuenta desactualizada" no se envían si la persona abrió la app ese día
  (`devices.LastSeenAt` o una sesión renovada desde la medianoche).
- Cada recordatorio tiene un `DedupKey` único por persona (índice único filtrado en
  `notifications`), así que nunca se envía dos veces aunque el worker corra en paralelo.
- Silencio, interruptor "Recordatorios" y ocultar montos los aplica `NotificationDispatcher`,
  igual que en el resto de notificaciones.
- El `Payload` lleva el destino (`cardId`, `budgetId`, `accountId`, `screen`); la app abre
  esa pantalla al tocar la push o la fila del historial.

Base de datos: `scripts/sql/add_notification_dedup_key.sql` (migración `AddNotificationDedupKey`).

## Probar sin esperar

Con `Nexo__Reminders__ManualRunEnabled=true` existe `POST /api/v1/notifications/reminders/run`
(sin la flag responde 404). Solo evalúa los recordatorios de quien llama y devuelve, por cada
candidato, qué pasó: `Sent`, `AlreadySent`, `DailyLimit`, `AppOpenedToday`, `NotChosen` o `Failed`.

- Sin parámetros: aplica todas las reglas reales (hora, tope diario, "abrió hoy", una sola vez).
- `?force=true`: ignora hora, tope diario, "abrió hoy" y "una sola vez", y envía todos los
  candidatos. Lo enviado así no guarda `DedupKey`, por lo que no impide el recordatorio real.

Apaga la flag cuando termines de probar.
