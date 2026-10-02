import { fireEvent, render } from '@testing-library/react-native';
import { AvailableMoneySection } from '../../components/home/AvailableMoneySection';
import type { BudgetOverview, CommittedMoney, HomeSummary } from '../../types/api';

const summary = { accountCount: 2 } as HomeSummary;

function committed(overrides: Partial<CommittedMoney> = {}): CommittedMoney {
  return {
    currentMoney: 668.89,
    committed: 97.59,
    available: 571.3,
    overcommitted: 0,
    isOvercommitted: false,
    currency: 'USD',
    sources: [
      { type: 'upcoming_payment', label: 'Próximos pagos', description: '', amount: 52.59, items: [] },
      { type: 'reserved_budget', label: 'Presupuestos reservados', description: '', amount: 45, items: [] },
    ],
    daysRemainingInMonth: 2,
    dailyAvailable: 285.65,
    ...overrides,
  };
}

function overview(overrides: Partial<BudgetOverview> = {}): BudgetOverview {
  return {
    date: '2026-09-29',
    label: 'Septiembre 2026',
    isCurrent: true,
    totals: { budgeted: 900, spent: 520, remaining: 380, reserved: 45, exceededCount: 0 },
    budgets: [{ isActive: true } as BudgetOverview['budgets'][number]],
    topInsight: null,
    ...overrides,
  };
}

function renderSection(props: Partial<Parameters<typeof AvailableMoneySection>[0]> = {}) {
  return render(
    <AvailableMoneySection
      summary={summary}
      committed={committed()}
      budgets={overview()}
      hidden={false}
      onOpenCommitted={jest.fn()}
      onOpenBudgets={jest.fn()}
      onOpenBudget={jest.fn()}
      {...props}
    />,
  );
}

/**
 * Home ya no calcula nada: pinta Tu dinero − Comprometido = Disponible tal
 * como lo devuelve GET /finance/committed, con el origen de lo comprometido
 * y el avance de los presupuestos del mes (GET /budgets).
 */
describe('AvailableMoneySection', () => {
  it('muestra Tu dinero, Comprometido con su origen y Disponible', async () => {
    const screen = await renderSection();

    expect(screen.getByText('$668.89')).toBeTruthy();
    expect(screen.getByText('−$97.59')).toBeTruthy();
    expect(screen.getByText('Próximos pagos')).toBeTruthy();
    expect(screen.getByText('$52.59')).toBeTruthy();
    expect(screen.getByText('Presupuestos reservados')).toBeTruthy();
    expect(screen.getAllByText('$571.30')).toHaveLength(2);
  });

  it('muestra cómo vas con los presupuestos del mes', async () => {
    const onOpenBudgets = jest.fn();
    const screen = await renderSection({ onOpenBudgets });

    expect(screen.getByText('PRESUPUESTOS · SEPTIEMBRE 2026')).toBeTruthy();
    expect(screen.getByText('$520.00 de $900.00')).toBeTruthy();
    expect(screen.getByText('Restante $380.00')).toBeTruthy();
    expect(screen.getByText('Reservado $45.00')).toBeTruthy();

    fireEvent.press(screen.getByText('$520.00 de $900.00'));
    expect(onOpenBudgets).toHaveBeenCalled();
  });

  it('Comprometido se puede tocar para ver de dónde sale', async () => {
    const onOpen = jest.fn();
    const screen = await renderSection({ onOpenCommitted: onOpen });

    fireEvent.press(screen.getByLabelText('Comprometido $97.59'));
    expect(onOpen).toHaveBeenCalled();
  });

  it('sin presupuestos invita a crear uno y explica que no hay nada comprometido', async () => {
    const screen = await renderSection({
      committed: committed({ committed: 0, available: 668.89, sources: [] }),
      budgets: overview({ budgets: [] }),
    });

    expect(screen.getByText('Planifica con presupuestos')).toBeTruthy();
    expect(screen.getByText(/Nada comprometido/)).toBeTruthy();
  });

  it('avisa cuando lo comprometido supera lo que hay, sin Disponible negativo', async () => {
    const screen = await renderSection({
      committed: committed({ currentMoney: 100, committed: 150, available: 0, overcommitted: 50, isOvercommitted: true }),
    });

    expect(screen.getByText('Tienes $50.00 más comprometidos de lo que tienes disponible.')).toBeTruthy();
    expect(screen.queryByText(/^-\$/)).toBeNull();
  });

  it('con montos ocultos no muestra cifras de presupuestos', async () => {
    const screen = await renderSection({ hidden: true });
    expect(screen.queryByText('$520.00 de $900.00')).toBeNull();
  });

  it('muestra lo que reservan las tarjetas como una línea más de Comprometido', async () => {
    const screen = await renderSection({
      committed: committed({
        committed: 317.59,
        available: 351.3,
        sources: [
          { type: 'upcoming_payment', label: 'Próximos pagos', description: '', amount: 52.59, items: [] },
          { type: 'reserved_budget', label: 'Presupuestos reservados', description: '', amount: 45, items: [] },
          { type: 'credit_card', label: 'Tarjetas', description: '', amount: 220, items: [] },
        ],
      }),
    });

    expect(screen.getByText('Tarjetas')).toBeTruthy();
    expect(screen.getByText('$220.00')).toBeTruthy();
    expect(screen.getByText('−$317.59')).toBeTruthy();
    expect(screen.getAllByText('$351.30')).toHaveLength(2);
  });
});
