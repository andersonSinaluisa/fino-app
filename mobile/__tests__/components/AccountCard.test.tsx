import { fireEvent, render } from '@testing-library/react-native';
import { AccountCard } from '../../components/accounts/AccountCard';
import type { Account } from '../../types/api';

function account(overrides: Partial<Account> = {}): Account {
  return {
    id: 'acc-1',
    providerCode: 'PICHINCHA',
    providerName: 'Banco Pichincha',
    brandColor: '#FFD100',
    logoKey: 'pichincha',
    alias: 'Banco Pichincha',
    accountType: 'Savings',
    mask: '4821',
    currency: 'USD',
    connectionMode: 'ManualImport',
    balance: 1240.5,
    balanceType: 'Estimated',
    lastVerifiedBalance: 1000,
    lastVerifiedAt: new Date(2026, 2, 1).toISOString(),
    lastTransactionAt: new Date(2026, 2, 4).toISOString(),
    lastSyncedAt: new Date(Date.now() - 5 * 60 * 1000).toISOString(),
    isArchived: false,
    ...overrides,
  };
}

describe('AccountCard', () => {
  it('shows the balance, the mask and the connection mode', async () => {
    const screen = await render(<AccountCard account={account()} />);

    expect(screen.getByText('Banco Pichincha')).toBeTruthy();
    expect(screen.getByText('$1,240.50')).toBeTruthy();
    expect(screen.getByText(/•••• 4821/)).toBeTruthy();
    expect(screen.getByText(/Importación/)).toBeTruthy();
  });

  it('never calls an estimated balance verified', async () => {
    const screen = await render(<AccountCard account={account({ balanceType: 'Estimated' })} />);

    expect(screen.getByText('SALDO ESTIMADO')).toBeTruthy();
    expect(screen.queryByText('SALDO VERIFICADO')).toBeNull();
  });

  it('says verified only when the balance actually is', async () => {
    const screen = await render(<AccountCard account={account({ balanceType: 'Verified' })} />);

    expect(screen.getByText('SALDO VERIFICADO')).toBeTruthy();
  });

  it('hides the balance when amounts are hidden', async () => {
    const screen = await render(<AccountCard account={account()} hidden />);

    expect(screen.queryByText('$1,240.50')).toBeNull();
    expect(screen.getByText('••••••')).toBeTruthy();
  });

  it('tells the user how stale the account is', async () => {
    const screen = await render(<AccountCard account={account()} />);

    expect(screen.getByText(/Actualizado hace 5 min/)).toBeTruthy();
  });

  it('asks for an update when the account has never had a real verified balance (Entregable 11)', async () => {
    const screen = await render(
      <AccountCard account={account({ balanceType: 'Estimated', lastVerifiedAt: null })} />,
    );

    expect(screen.getByText('NECESITA ACTUALIZACIÓN')).toBeTruthy();
    expect(screen.queryByText('SALDO ESTIMADO')).toBeNull();
  });

  it('does not ask for an update once there is a real anchor, even if it drifted', async () => {
    const screen = await render(<AccountCard account={account()} />);

    expect(screen.getByText('SALDO ESTIMADO')).toBeTruthy();
    expect(screen.queryByText('NECESITA ACTUALIZACIÓN')).toBeNull();
  });

  it('sends the account to onUpdateBalance when the balance badge is tapped', async () => {
    const onUpdateBalance = jest.fn();
    const acc = account({ balanceType: 'Estimated', lastVerifiedAt: null });

    const screen = await render(<AccountCard account={acc} onUpdateBalance={onUpdateBalance} />);
    fireEvent.press(screen.getByLabelText(`Actualizar saldo de ${acc.alias}`));

    expect(onUpdateBalance).toHaveBeenCalledWith(acc);
  });

  it('does not make the badge pressable when no update handler is given', async () => {
    const screen = await render(<AccountCard account={account({ balanceType: 'Estimated', lastVerifiedAt: null })} />);

    expect(screen.queryByLabelText(/Actualizar saldo de/)).toBeNull();
  });
});
