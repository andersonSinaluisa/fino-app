/**
 * Solo presentación. El cálculo de Disponible/Comprometido que vivía aquí
 * (una proyección del ritmo de gasto hecha en el teléfono) se movió al
 * backend: CommittedMoneyCalculator es la única fuente de verdad y la app lo
 * consume con useCommittedMoney() (GET /api/v1/finance/committed).
 */
export function monthRangeLabel(today = new Date()): string {
  const month = today.toLocaleDateString('es-EC', { month: 'short' }).replace('.', '');
  const year = today.getFullYear();
  const daysInMonth = new Date(year, today.getMonth() + 1, 0).getDate();

  return `1 ${month} - ${daysInMonth} ${month}`;
}
