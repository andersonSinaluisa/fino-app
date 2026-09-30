import { fireEvent, render } from '@testing-library/react-native';
import { AvailableMoneySection } from '../../components/home/AvailableMoneySection';
import type { CommittedMoney, HomeSummary } from '../../types/api';

const summary = { accountCount: 2 } as HomeSummary;

function committed(overrides: Partial<CommittedMoney> = {}): CommittedMoney {
  return {
    currentMoney: 668.89,
    committed: 97.59,
    available: 571.3,
    overcommitted: 0,
    isOvercommitted: false,
    currency: 'USD',
    sources: [],
    daysRemainingInMonth: 2,
    dailyAvailable: 285.65,
    ...overrides,
  };
}

/**
 * Home ya no calcula nada: pinta exactamente lo que devuelve
 * GET /finance/committed y deja tocar "Comprometido" para ver su origen.
 */
describe('AvailableMoneySection', () => {
  it('muestra Tu dinero, Comprometido y Disponible del backend', async () => {
    const screen = await render(
      <AvailableMoneySection summary={summary} committed={committed()} hidden={false} onOpenCommitted={jest.fn()} />,
    );

    expect(screen.getByText('$668.89')).toBeTruthy();
    expect(screen.getByText('$97.59')).toBeTruthy();
    expect(screen.getAllByText('$571.30')).toHaveLength(2);
  });

  it('Comprometido se puede tocar para ver de dónde sale', async () => {
    const onOpen = jest.fn();
    const screen = await render(
      <AvailableMoneySection summary={summary} committed={committed()} hidden={false} onOpenCommitted={onOpen} />,
    );

    fireEvent.press(screen.getByLabelText('Comprometido $97.59'));
    expect(onOpen).toHaveBeenCalled();
  });

  it('avisa cuando lo comprometido supera lo que hay, sin mostrar Disponible negativo', async () => {
    const screen = await render(
      <AvailableMoneySection
        summary={summary}
        committed={committed({ currentMoney: 100, committed: 150, available: 0, overcommitted: 50, isOvercommitted: true })}
        hidden={false}
        onOpenCommitted={jest.fn()}
      />,
    );

    expect(screen.getByText('Tienes $50.00 más comprometidos de lo que tienes disponible.')).toBeTruthy();
    expect(screen.queryByText(/-\$/)).toBeNull();
  });
});
