import { fireEvent, render } from '@testing-library/react-native';
import { BudgetRow } from '../../components/budgets/BudgetRow';
import type { Budget } from '../../types/api';

function budget(overrides: Partial<Budget> = {}): Budget {
  return {
    id: 'b-1',
    name: 'Comida',
    amount: 300,
    categoryId: 'cat-1',
    categoryName: 'Comida',
    categoryIcon: 'utensils',
    categoryColor: '#E4A853',
    period: 'Monthly',
    startDate: '2026-09-01',
    endDate: null,
    isRecurring: true,
    reserveFunds: false,
    priority: 'Important',
    isActive: true,
    currency: 'USD',
    window: { start: '2026-09-01', end: '2026-09-30', label: 'Septiembre 2026' },
    progress: {
      amount: 300,
      spent: 120,
      remaining: 180,
      overspent: 0,
      percentUsed: 40,
      level: 'Normal',
      reserved: 0,
      daysInWindow: 30,
      daysElapsed: 19,
      daysRemaining: 12,
      dailyAllowance: 15,
      projectedSpend: null,
      isCurrentWindow: true,
    },
    createdAt: '2026-09-01T00:00:00Z',
    updatedAt: '2026-09-01T00:00:00Z',
    ...overrides,
  };
}

describe('BudgetRow', () => {
  it('muestra lo gastado de lo presupuestado, el porcentaje y cuánto queda', async () => {
    const onPress = jest.fn();
    const screen = await render(<BudgetRow budget={budget()} hidden={false} onPress={onPress} />);

    expect(screen.getByText('Comida')).toBeTruthy();
    expect(screen.getByText('$120.00 de $300.00')).toBeTruthy();
    expect(screen.getByText('40%')).toBeTruthy();
    expect(screen.getByText('Te quedan $180.00')).toBeTruthy();

    fireEvent.press(screen.getByRole('button'));
    expect(onPress).toHaveBeenCalled();
  });

  it('marca el dinero reservado y el exceso con texto, no solo con color', async () => {
    const exceeded = budget({
      reserveFunds: true,
      progress: { ...budget().progress!, spent: 324, remaining: 0, overspent: 24, percentUsed: 108, level: 'Exceeded' },
    });
    const screen = await render(<BudgetRow budget={exceeded} hidden={false} onPress={jest.fn()} />);

    expect(screen.getByText('RESERVADO')).toBeTruthy();
    expect(screen.getByText('Excediste tu presupuesto por $24.00')).toBeTruthy();
  });

  it('oculta los montos cuando la persona lo pidió', async () => {
    const screen = await render(<BudgetRow budget={budget()} hidden onPress={jest.fn()} />);

    expect(screen.queryByText('$120.00 de $300.00')).toBeNull();
    expect(screen.getByText('Has usado el 40%')).toBeTruthy();
  });
});
