import { useState } from 'react';
import { Alert, Pressable, StyleSheet, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Card, Input, Screen, Typo } from '../../components/ui';
import { ApiError } from '../../services/apiClient';
import { useStartEmailConnection } from '../../hooks/queries';

interface ProviderOption {
  kind: 'Forwarding' | 'Gmail' | 'Outlook';
  title: string;
  description: string;
  icon: keyof typeof Ionicons.glyphMap;
  /**
   * Entregable 23 ("Preparar email ingestion, sin OAuth"): Gmail y Outlook
   * necesitan un flujo de consentimiento real que no existe todavía
   * (Entregables 24/25) -- se muestran para que la persona sepa que están
   * planeados, pero intentar conectarlos hoy siempre termina en el mismo 409
   * que ya devuelve el servidor. Reenvío no necesita nada de eso.
   */
  comingSoon: boolean;
}

const PROVIDERS: ProviderOption[] = [
  {
    kind: 'Forwarding',
    title: 'Reenvío de correo',
    description:
      'Reenvía las notificaciones de tu banco a una dirección de Fino. No le das acceso a tu cuenta de correo.',
    icon: 'arrow-redo-outline',
    comingSoon: false,
  },
  {
    kind: 'Gmail',
    title: 'Gmail',
    description: 'Autoriza a Fino a leer, solo de forma automática, las notificaciones bancarias de tu Gmail.',
    icon: 'logo-google',
    comingSoon: true,
  },
  {
    kind: 'Outlook',
    title: 'Outlook / Microsoft 365',
    description: 'Autoriza a Fino a leer, solo de forma automática, las notificaciones bancarias de tu Outlook.',
    icon: 'logo-microsoft',
    comingSoon: true,
  },
];

export default function ConnectEmailScreen() {
  const router = useRouter();
  const start = useStartEmailConnection();

  const [selected, setSelected] = useState<ProviderOption | null>(null);
  const [emailAddress, setEmailAddress] = useState('');
  const [inboundAddress, setInboundAddress] = useState<string | null>(null);

  const submit = () => {
    if (!selected) {
      return;
    }

    start.mutate(
      { providerKind: selected.kind, emailAddress: emailAddress.trim() },
      {
        onSuccess: (connection) => {
          if (connection.inboundAddress) {
            // Forwarding: nothing left to wait on -- the connection is already
            // Connected (Entregable 23) and this address is usable right now.
            setInboundAddress(connection.inboundAddress);
          } else {
            router.back();
          }
        },
        onError: (error) => {
          Alert.alert(
            'No pudimos conectar tu correo',
            error instanceof ApiError ? error.message : 'Inténtalo de nuevo más tarde.',
          );
        },
      },
    );
  };

  if (inboundAddress) {
    return (
      <Screen>
        <View style={styles.header}>
          <Ionicons name="checkmark-circle-outline" size={36} color={colors.success} />
          <Typo variant="title">Ya casi está</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            Configura el reenvío automático de tu banco en tu correo hacia esta dirección. En cuanto llegue una
            notificación, Fino la detecta sola.
          </Typo>
        </View>

        <Card>
          <Typo variant="subheading" tabular>
            {inboundAddress}
          </Typo>
        </Card>

        <View style={styles.footer}>
          <Button label="Listo" onPress={() => router.back()} />
        </View>
      </Screen>
    );
  }

  if (selected) {
    return (
      <Screen>
        <Pressable onPress={() => setSelected(null)} hitSlop={12} style={styles.back}>
          <Ionicons name="chevron-back" size={20} color={colors.text} />
          <Typo variant="caption" color={colors.textSecondary}>
            Elegir otro
          </Typo>
        </Pressable>

        <View style={styles.header}>
          <Typo variant="title">{selected.title}</Typo>
          <Typo variant="body" color={colors.textSecondary}>
            {selected.description}
          </Typo>
        </View>

        {selected.comingSoon ? (
          <Card>
            <Typo variant="body" color={colors.textSecondary}>
              Esta conexión necesita credenciales de {selected.title} que este servidor todavía no tiene
              configuradas. Mientras tanto, el reenvío de correo ya funciona.
            </Typo>
          </Card>
        ) : (
          <View style={styles.form}>
            <Input
              label="¿A qué correo te llegan hoy las notificaciones de tu banco?"
              value={emailAddress}
              onChangeText={setEmailAddress}
              autoCapitalize="none"
              autoComplete="email"
              keyboardType="email-address"
              placeholder="tu@correo.com"
              hint="Solo lo usamos para identificar esta conexión: el reenvío llega directo a Fino."
            />

            <Button
              label="Conectar"
              onPress={submit}
              loading={start.isPending}
              disabled={emailAddress.trim().length === 0}
            />
          </View>
        )}
      </Screen>
    );
  }

  return (
    <Screen>
      <Pressable onPress={() => router.back()} hitSlop={12} style={styles.back}>
        <Ionicons name="chevron-back" size={20} color={colors.text} />
        <Typo variant="caption" color={colors.textSecondary}>
          Volver
        </Typo>
      </Pressable>

      <View style={styles.header}>
        <Typo variant="title">Conectar correo</Typo>
        <Typo variant="body" color={colors.textSecondary}>
          Fino lee únicamente las notificaciones bancarias -- el resto de tu correo nunca se toca ni se guarda.
        </Typo>
      </View>

      <View style={styles.list}>
        {PROVIDERS.map((provider) => (
          <Card key={provider.kind} onPress={() => setSelected(provider)}>
            <View style={styles.option}>
              <View style={styles.optionIcon}>
                <Ionicons name={provider.icon} size={20} color={colors.text} />
              </View>

              <View style={styles.optionBody}>
                <Typo variant="subheading">{provider.title}</Typo>
                <Typo variant="caption" color={colors.textSecondary}>
                  {provider.description}
                </Typo>
              </View>

              {provider.comingSoon ? <Badge label="Próximamente" /> : null}
              <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} />
            </View>
          </Card>
        ))}
      </View>
    </Screen>
  );
}

const styles = StyleSheet.create({
  back: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
    marginBottom: spacing.lg,
  },
  header: {
    gap: spacing.xs,
    marginBottom: spacing.xl,
  },
  list: {
    gap: spacing.md,
  },
  option: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
  },
  optionIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  optionBody: {
    flex: 1,
    gap: 2,
  },
  form: {
    gap: spacing.lg,
  },
  footer: {
    marginTop: spacing.xl,
  },
});
