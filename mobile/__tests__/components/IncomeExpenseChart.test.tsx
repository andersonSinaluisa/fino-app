import { act, fireEvent, render } from '@testing-library/react-native';
import { IncomeExpenseChart } from '../../components/analytics/IncomeExpenseChart';
import type { AnalyticsSeriesPoint } from '../../types/api';

function point(overrides: Partial<AnalyticsSeriesPoint> = {}): AnalyticsSeriesPoint {
  return {
    from: '2026-09-01',
    to: '2026-09-07',
    label: '1 sep',
    income: 0,
    expense: 100,
    ...overrides,
  };
}

const POINTS: AnalyticsSeriesPoint[] = [
  point({ from: '2026-08-25', to: '2026-08-31', label: '25 ago', income: 200, expense: 50 }),
  point({ from: '2026-09-01', to: '2026-09-07', label: '1 sep', income: 0, expense: 100 }),
];

describe('IncomeExpenseChart', () => {
  it('shows the most recent point in the detail readout by default', async () => {
    const screen = await render(
      <IncomeExpenseChart points={POINTS} mode="weekly" onModeChange={jest.fn()} hidden={false} />,
    );

    expect(screen.getByText('Ingresos $0.00')).toBeTruthy();
    expect(screen.getByText('Gastos $100.00')).toBeTruthy();
  });

  it('updates the detail readout when an earlier column is tapped (the "tooltip")', async () => {
    const screen = await render(
      <IncomeExpenseChart points={POINTS} mode="weekly" onModeChange={jest.fn()} hidden={false} />,
    );

    await act(async () => {
      fireEvent.press(screen.getByLabelText('25 ago: ingresos $200.00, gastos $50.00'));
    });

    expect(screen.getByText('Ingresos $200.00')).toBeTruthy();
    expect(screen.getByText('Gastos $50.00')).toBeTruthy();
  });

  it('hides the readout amounts when the user asked to hide amounts', async () => {
    const screen = await render(
      <IncomeExpenseChart points={POINTS} mode="weekly" onModeChange={jest.fn()} hidden />,
    );

    expect(screen.getByText('Ingresos ••••')).toBeTruthy();
    expect(screen.getByText('Gastos ••••')).toBeTruthy();
  });

  it('lets the person switch between weekly and monthly detail', async () => {
    const onModeChange = jest.fn();
    const screen = await render(
      <IncomeExpenseChart points={POINTS} mode="weekly" onModeChange={onModeChange} hidden={false} />,
    );

    fireEvent.press(screen.getByText('Mensual'));

    expect(onModeChange).toHaveBeenCalledWith('monthly');
  });
});
