import { fireEvent, render } from '@testing-library/react-native';
import { Text } from 'react-native';
import { ConnectionOption } from '../../components/onboarding/ConnectionOption';

describe('ConnectionOption', () => {
  it('reports checked/unchecked accessibility state from the selected prop', async () => {
    const screen = await render(
      <ConnectionOption
        icon="business-outline"
        iconBackground="#F6F4EB"
        iconColor="#8A5A00"
        title="Bancos locales"
        tagLabel="Lectura protegida"
        tagTone="positive"
        description="Pichincha, Guayaquil"
        selected={false}
        onSelect={() => undefined}
      />,
    );

    expect(screen.getByRole('radio').props.accessibilityState.checked).toBe(false);
  });

  it('calls onSelect when tapped', async () => {
    const onSelect = jest.fn();
    const screen = await render(
      <ConnectionOption
        icon="wallet-outline"
        iconBackground="#E8F8F0"
        iconColor="#1E7A63"
        title="Billeteras digitales"
        tagLabel="Instantáneo"
        tagTone="accent"
        description="Sincroniza tus transacciones"
        selected={false}
        onSelect={onSelect}
      />,
    );

    fireEvent.press(screen.getByText('Billeteras digitales'));

    expect(onSelect).toHaveBeenCalledTimes(1);
  });

  it('renders the optional footer content', async () => {
    const screen = await render(
      <ConnectionOption
        icon="document-text-outline"
        iconBackground="#EFF6FF"
        iconColor="#1D5FC7"
        title="Importar archivo"
        tagLabel="CSV o Excel"
        tagTone="neutral"
        description="Sube el extracto"
        selected
        onSelect={() => undefined}
        footer={<Text>100% privado</Text>}
      />,
    );

    expect(screen.getByText('100% privado')).toBeTruthy();
    expect(screen.getByRole('radio').props.accessibilityState.checked).toBe(true);
  });
});
