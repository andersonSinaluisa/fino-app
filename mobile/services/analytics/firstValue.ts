import { analytics } from './service';
import { AnalyticsEvent, ValueSource } from './events';

export type ValueSourceValue = (typeof ValueSource)[keyof typeof ValueSource];

/**
 * §11: el "momento de valor" de FINO.
 *
 * DEFINICIÓN (derivada de la arquitectura real, no inventada):
 *
 *   el usuario llegó a Inicio y la pantalla tiene datos de verdad que mostrar,
 *   habiendo llegado ahí por una de dos vías:
 *     - `statement_import`: importó un estado de cuenta con éxito
 *       (`onboarding.hasImportedData` del backend), o
 *     - `cash_manual`: registró movimientos a mano suficientes para que
 *       Inicio muestre un resumen real.
 *
 * Las dos condiciones importan. "Importó un archivo" por sí solo no es valor:
 * el valor es ver su dinero ordenado. Por eso esto se dispara desde Inicio
 * cuando el resumen ya resolvió CON datos, y no al terminar la importación.
 *
 * Se emite una sola vez por usuario y sobrevive a reinicios (trackOnce).
 */
export async function trackFirstValueIfReached(
  hasUsefulData: boolean,
  source: ValueSourceValue,
): Promise<void> {
  if (!hasUsefulData) {
    return;
  }

  await analytics.trackOnce(AnalyticsEvent.FirstValueReached, { valueSource: source });
}
