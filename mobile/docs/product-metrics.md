# Plan de métricas de producto — FINO

Este documento define **qué medimos y por qué**. El catálogo técnico de
eventos está en [`analytics.md`](./analytics.md).

## North Star: WFAU

**Weekly Financially Active Users** — usuarios que en una semana realizan al
menos una acción que mejora o consulta de forma significativa su estado
financiero.

Abrir la app no cuenta. Alguien que entra, mira y se va no recibió valor, y
una North Star que premia eso lleva a optimizar lo que no importa.

### Eventos que califican

| Acción | Eventos |
|---|---|
| Importa movimientos | `import_completed` |
| Registra un movimiento | `quick_entry_saved`, `quick_entry_queued_offline` |
| Concilia un movimiento | `transfer_confirmed`, `transfer_rejected`, `withdrawal_confirmed_as_cash`, `withdrawal_rejected` |
| Interactúa con un Pulso accionable | `pulse_opened`, `pulse_action_clicked` |

**No califican:** `app_opened`, `session_started`, ningún `screen`,
`home_viewed`, `movements_viewed`. Son consulta pasiva.

`statistics_viewed` es el caso de frontera: mirar tus estadísticas **sí** es
consultar tu estado financiero de forma significativa. Queda **fuera** de la
v1 a propósito, para que la North Star empiece exigente; si se incluye más
adelante, se sube `ANALYTICS_SCHEMA_VERSION` y se anota aquí, porque cambia la
serie histórica.

## Activación (§12)

```
ACTIVATED_USER = en sus primeros 7 días:
    (import_completed  OR  al menos 3 quick_entry_saved)
  AND
    app_opened en 2 días distintos
```

La rama del efectivo existe porque en Ecuador mucha gente vive en efectivo y
puede no importar nunca un estado de cuenta. Exigir la importación para
considerarlos activados mediría mal a un segmento entero.

**Esta definición se calcula en el dashboard, no en la app.** En el código no
hay ningún `ACTIVATED_USER`: una métrica codificada en la UI queda congelada
el día que la escribes y, cuando cambia el criterio, pierdes la serie vieja.

## Métricas

### Principal del onboarding

**FIRST_SUCCESSFUL_IMPORT_RATE** = usuarios con `first_import_completed` /
usuarios con `signup_succeeded`.

### Secundarias

| Métrica | Definición |
|---|---|
| Activation rate | Activados / nuevos registros |
| Onboarding completion rate | `onboarding_completed` / `onboarding_started` |
| Time to first import | Mediana `signup_succeeded` → `first_import_completed` |
| Time to first value | Mediana `signup_succeeded` → `first_value_reached` |
| D1 / D7 / D30 retention | Cohortes por `app_opened`, calculadas en el proveedor |
| QuickEntry adoption | % de MAU con ≥1 `quick_entry_saved` |
| QuickEntry completion rate | `quick_entry_saved` / `quick_entry_opened` |
| Pulse open rate | `pulse_opened` / `pulse_card_viewed` |
| Reconciliation completion rate | (`transfer_confirmed` + `withdrawal_confirmed_as_cash`) / `reconciliation_viewed` |
| Notification opt-in rate | `notification_permission_accepted` / `notification_permission_shown` |
| Voice adoption | % de MAU con ≥1 `voice_entry_parsed` |

### Retención: no se instrumenta (§13)

No existe ningún evento `D7_RETAINED` en el código, y no debe existir. La
retención se calcula como cohorte en el proveedor a partir de `app_opened` y
`session_started`. Un evento "retenido" emitido por la app sería una métrica
calculada localmente, imposible de corregir hacia atrás.

## No confundir evento, métrica y KPI (§27)

| Nivel | Ejemplo |
|---|---|
| **Evento** | `quick_entry_saved` |
| **Métrica** | QuickEntry adoption = % de MAU que lo dispara |
| **KPI** | % de MAU que registra efectivo al menos una vez por semana |

El código solo produce **eventos**. Las métricas y los KPIs viven en el
dashboard.

## Dashboards

### 1 — Overview
DAU, WAU, MAU, DAU/MAU, nuevos usuarios, sesiones, D1/D7/D30.

### 2 — Activación
```
signup_succeeded → onboarding_started → bank_selected
→ bank_tutorial_completed → file_picker_opened → import_started
→ import_completed → first_value_reached
```
Segmentar por `bankCode` para ver qué tutorial de banco pierde gente.

### 3 — Adopción de funciones
% de MAU que usa: import, quick entry, smart entry, voz, estadísticas,
conciliación, Pulso, proyección.

### 4 — Registro rápido de efectivo
```
quick_entry_opened → quick_entry_saved
```
Segmentado por `entryMode` (`keypad` / `smart_text` / `voice` / `frequent`).

Métricas: completion rate, distribución de `durationBucket`, undo rate
(`quick_entry_undo` / `quick_entry_saved`).

**La meta del spec: la mediana de `durationBucket` para `keypad` debe ser
`under_2s`.**

### 5 — Pulso
```
pulse_card_viewed → notification_opened → pulse_opened → pulse_action_clicked
```
Métrica principal: **PULSE_OPEN_RATE**. Segmentar por `pulseKind` para saber
qué tipos de pulso valen la pena y cuáles son ruido.

### 6 — Conciliación
```
reconciliation_viewed → transfer_suggestion_shown → transfer_confirmed / transfer_rejected
withdrawal_detected → withdrawal_reviewed → withdrawal_confirmed_as_cash / withdrawal_rejected
```
Segmentar los retiros por `confidence`. Si los `high` se rechazan a menudo, el
umbral del `WithdrawalDetector` está mal puesto.

### 7 — Calidad de importación
`import_started` / `import_completed` / `import_failed`, por `fileFormat`,
`bankCode` y `reason`.

Es el dashboard que dice qué parser de banco hay que arreglar primero, sin un
solo dato financiero.

## Cohortes

New users · Activated users · Users with successful import · **Cash-only
users** (registran efectivo y nunca importan) · QuickEntry users · Pulse users
· Notification-enabled users · Pro users.

Las preguntas que estas cohortes existen para responder:

- ¿Los usuarios que abren Pulso retienen más?
- ¿Los que usan registro rápido vuelven más?
- ¿Los que completan la importación el primer día retienen más?
- ¿Los **cash-only** retienen distinto? En Ecuador puede ser un segmento
  grande, y si retienen bien, la prioridad del producto cambia.

## Lo que estas métricas NO pueden decir

Y es a propósito: cuánto dinero tiene alguien, qué compró, a quién le
transfirió, con qué banco opera de verdad, qué dijo por voz, ni qué había en
sus archivos.

Si alguna pregunta de producto parece necesitar uno de esos datos, la pregunta
está mal formulada. Casi siempre hay una versión de comportamiento que
responde lo mismo.
