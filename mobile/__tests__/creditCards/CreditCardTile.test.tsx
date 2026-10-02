import { fireEvent, render } from '@testing-library/react-native';
import { CreditCardTile } from '../../components/creditCards/CreditCardTile';
import type { CreditCardSummary } from '../../types/api';

function card(overrides: Partial<CreditCardSummary> = {}): CreditCardSummary {
  return {
    id: 'card-1',
    name: 'Visa Pichincha',
    providerCode: 'PICHINCHA',
    providerName: 'Banco Pichincha',
    brandColor: '#FFD100',
    lastFour: '4582',
    currency: 'USD',
    network: 'Visa',
    isArchived: false,
    needsSetup: false,
    creditLimit: 2000,
    closingDay: 15,
    paymentDueDay: 30,
    autoReserve: true,
    currentDebt: 750.32,
    creditBalance: 0,
    availableCredit: 1249.68,
    utilizationPercent: 37.5,
    isOverLimit: false,
    deferredDebt: 0,
    nextPayment: { amount: 420, dueDate: '2026-10-30', minimumPayment: null, source: 'statement', isOverdue: false, daysUntilDue: 5 },
    committedContribution: 420,
    balanceType: 'Estimated',
    lastSyncedAt: null,
    ...overrides,
  };
}

describe('CreditCardTile', () => {
  it('separa deuda, próximo pago y cupo disponible', async () => {
    const screen = await render(<CreditCardTile card={card()} hidden={false} onPress={jest.fn()} />);

    expect(screen.getByText('$750.32')).toBeTruthy();
    expect(screen.getByText('PRÓXIMO PAGO · 30 OCT')).toBeTruthy();
    expect(screen.getByText('$420.00')).toBeTruthy();
    expect(screen.getByText('Cupo disponible $1,249.68')).toBeTruthy();
    expect(screen.getByText('Vence en 5 días')).toBeTruthy();
  });

  it('con montos ocultos no muestra ninguna cifra', async () => {
    const screen = await render(<CreditCardTile card={card()} hidden onPress={jest.fn()} />);

    expect(screen.queryByText('$750.32')).toBeNull();
    expect(screen.queryByText('$420.00')).toBeNull();
    expect(screen.queryByText(/1,249\.68/)).toBeNull();
  });

  it('una tarjeta sin datos pide completarlos y abre su detalle', async () => {
    const onPress = jest.fn();
    const legacy = card({ needsSetup: true, creditLimit: null, availableCredit: null, utilizationPercent: null, nextPayment: null });
    const screen = await render(<CreditCardTile card={legacy} hidden={false} onPress={onPress} />);

    expect(screen.getByText('COMPLETA LOS DATOS DE LA TARJETA')).toBeTruthy();
    fireEvent.press(screen.getByLabelText('Visa Pichincha, deuda $750.32'));
    expect(onPress).toHaveBeenCalled();
  });
});
