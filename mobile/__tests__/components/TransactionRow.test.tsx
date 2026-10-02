import { render, fireEvent } from '@testing-library/react-native';
import { TransactionRow } from '../../components/transactions/TransactionRow';
import type { TransactionListItem } from '../../types/api';

function movement(overrides: Partial<TransactionListItem> = {}): TransactionListItem {
  return {
    id: 'tx-1',
    financialAccountId: 'acc-1',
    accountAlias: 'Banco Pichincha',
    providerCode: 'PICHINCHA',
    brandColor: '#FFD100',
    transactionDate: new Date(2026, 2, 4, 18, 12).toISOString(),
    amount: 48.2,
    signedAmount: -48.2,
    currency: 'USD',
    direction: 'Expense',
    description: 'SUPERMAXI ALBORADA',
    merchant: 'Supermaxi Alborada',
    categoryId: 'cat-1',
    categoryName: 'Supermercado',
    categoryIcon: 'shopping-cart',
    categoryColor: '#8DD9B6',
    status: 'Posted',
    source: 'Import',
    isInternalTransfer: false,
    isSplit: false,
    splits: [],
    cardMovementType: null,
    ...overrides,
  };
}

describe('TransactionRow', () => {
  it('shows an expense with its sign and its category', async () => {
    const screen = await render(<TransactionRow transaction={movement()} />);

    expect(screen.getByText('Supermaxi Alborada')).toBeTruthy();
    expect(screen.getByText('Supermercado')).toBeTruthy();
    expect(screen.getByText('-$48.20')).toBeTruthy();
  });

  it('shows income with a plus sign', async () => {
    const income = movement({
      direction: 'Income',
      amount: 1420,
      signedAmount: 1420,
      merchant: null,
      description: 'Acreditación rol de pagos',
      categoryName: 'Ingresos',
    });

    const screen = await render(<TransactionRow transaction={income} />);

    expect(screen.getByText('+$1,420.00')).toBeTruthy();
  });

  it('hides the amount when the user asked to, without hiding the merchant', async () => {
    const screen = await render(<TransactionRow transaction={movement()} hidden />);

    expect(screen.queryByText('-$48.20')).toBeNull();
    expect(screen.getByText('••••')).toBeTruthy();
    expect(screen.getByText('Supermaxi Alborada')).toBeTruthy();
  });

  it('tells the user when a movement is parked as a possible duplicate', async () => {
    const screen = await render(<TransactionRow transaction={movement({ status: 'NeedsReview' })} />);

    expect(screen.getByText('Posible duplicado')).toBeTruthy();
  });

  it('marks an email-detected movement as pending confirmation', async () => {
    const pending = movement({ status: 'Pending', source: 'Email' });
    const screen = await render(<TransactionRow transaction={pending} />);

    expect(screen.getByText('Por confirmar')).toBeTruthy();
  });

  it('falls back to the description when there is no merchant', async () => {
    const screen = await render(<TransactionRow transaction={movement({ merchant: null })} />);

    expect(screen.getByText('SUPERMAXI ALBORADA')).toBeTruthy();
  });

  it('says "Sin categoría" rather than showing nothing', async () => {
    const uncategorised = movement({ categoryId: null, categoryName: null });
    const screen = await render(<TransactionRow transaction={uncategorised} />);

    expect(screen.getByText('Sin categoría')).toBeTruthy();
  });

  it('labels a confirmed internal transfer instead of its category (Entregable 13)', async () => {
    const transfer = movement({ isInternalTransfer: true, categoryName: null });
    const screen = await render(<TransactionRow transaction={transfer} />);

    expect(screen.getByText('Transferencia interna')).toBeTruthy();
    expect(screen.queryByText('Sin categoría')).toBeNull();
  });

  it('hands the whole movement back on press', async () => {
    const onPress = jest.fn();
    const transaction = movement();

    const screen = await render(<TransactionRow transaction={transaction} onPress={onPress} />);
    fireEvent.press(screen.getByLabelText('SUPERMAXI ALBORADA, -$48.20'));

    expect(onPress).toHaveBeenCalledWith(transaction);
  });
  it('un movimiento dividido es UNA fila con sus categorías', async () => {
    const divided = movement({
      merchant: 'Transf Directa Chilan',
      amount: 220,
      signedAmount: -220,
      categoryId: null,
      categoryName: null,
      categoryIcon: null,
      categoryColor: null,
      isSplit: true,
      splits: [
        { id: 's1', categoryId: 'c1', categoryName: 'Esposa', categoryIcon: 'people', categoryColor: '#D8665B', amount: 70, note: null },
        { id: 's2', categoryId: 'c2', categoryName: 'Comida', categoryIcon: 'utensils', categoryColor: '#E4A853', amount: 150, note: null },
      ],
    });

    const screen = await render(<TransactionRow transaction={divided} />);

    expect(screen.getByText('Esposa + Comida')).toBeTruthy();
    expect(screen.getAllByText('-$220.00')).toHaveLength(1);
  });

  it('un pago de tarjeta dice lo que es y no se pinta como ingreso', async () => {
    const payment = movement({
      direction: 'Income',
      amount: 220,
      signedAmount: 220,
      merchant: 'Pago desde Banco Pichincha',
      categoryName: null,
      categoryIcon: null,
      categoryColor: null,
      isInternalTransfer: true,
      cardMovementType: 'Payment',
    });

    const screen = await render(<TransactionRow transaction={payment} />);

    expect(screen.getByText('Pago a la tarjeta')).toBeTruthy();
    expect(screen.queryByText('Transferencia interna')).toBeNull();
  });

  it('una devolución de tarjeta se ve como devolución de su categoría', async () => {
    const refund = movement({
      direction: 'Income',
      amount: 30,
      signedAmount: 30,
      merchant: 'Devolucion Restaurante',
      categoryName: 'Comida',
      cardMovementType: 'Refund',
    });

    const screen = await render(<TransactionRow transaction={refund} />);

    expect(screen.getByText('Devolución · Comida')).toBeTruthy();
  });
});
