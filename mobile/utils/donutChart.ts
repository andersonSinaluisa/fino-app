export interface DonutSlice {
  id: string;
  color: string;
  percentage: number;
}

export interface DonutSegment {
  id: string;
  color: string;
  /** Degrees, 0-360, clockwise from 12 o'clock -- the END of this slice's wedge. */
  cumulativeAngle: number;
}

export const OTHER_SLICE_ID = '__other__';

export const MAX_DONUT_SLICES = 6;

/**
 * "Gastos por categoría" (anillo de Inicio) necesita que las porciones sumen
 * 100% para que el anillo se vea completo -- a diferencia de CategoryBreakdown
 * (barras) o CategoryTrendList, que pueden mostrar solo un top-N sin que se
 * note que falta el resto. Si hay más categorías de las que el anillo puede
 * distinguir a simple vista, las más chicas se agrupan en una sola porción
 * "Otros" en vez de dejar el anillo incompleto o de mentir mostrando solo una
 * parte del gasto real. `otherColor` lo decide quien llama (el tema visual no
 * vive en utils/).
 */
export function buildCategorySlices<T extends { categoryId: string; color: string; percentage: number; total: number }>(
  items: T[],
  otherColor: string,
  maxSlices: number = MAX_DONUT_SLICES,
): DonutSlice[] {
  const sorted = [...items].sort((a, b) => b.total - a.total);

  if (sorted.length <= maxSlices) {
    return sorted.map((item) => ({ id: item.categoryId, color: item.color, percentage: item.percentage }));
  }

  const top = sorted.slice(0, maxSlices - 1);
  const rest = sorted.slice(maxSlices - 1);
  const otherPercentage = rest.reduce((sum, item) => sum + item.percentage, 0);

  return [
    ...top.map((item) => ({ id: item.categoryId, color: item.color, percentage: item.percentage })),
    { id: OTHER_SLICE_ID, color: otherColor, percentage: otherPercentage },
  ];
}

/**
 * Convierte porcentajes en ángulos ACUMULADOS (0-360°, sentido horario desde
 * las 12) para dibujar el anillo con Views puras -- ver components/ui/RingChart.
 * Se pintan de atrás hacia adelante (ángulo acumulado más grande primero), así
 * que cada porción más chica queda encima y no la tapa la anterior.
 */
export function buildDonutSegments(slices: DonutSlice[]): DonutSegment[] {
  let cumulative = 0;

  return slices
    .filter((slice) => slice.percentage > 0)
    .map((slice) => {
      cumulative += slice.percentage;
      return {
        id: slice.id,
        color: slice.color,
        cumulativeAngle: Math.min(360, Math.max(0, (cumulative / 100) * 360)),
      };
    });
}
