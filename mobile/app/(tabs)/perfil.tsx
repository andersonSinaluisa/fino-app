import { Alert, Linking, StyleSheet, Switch, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, Screen, SectionHeader, Typo } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { usePreferencesStore } from '../../store/preferencesStore';
import { useEmailConnections } from '../../hooks/queries';
import { api } from '../../services/endpoints';
import { config } from '../../services/config';

export default function ProfileScreen() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const toggleAmounts = usePreferencesStore((state) => state.toggleAmounts);
  const { data: emailConnections } = useEmailConnections();

  const confirmDeletion = () => {
    Alert.alert(
      'Eliminar cuenta Nexo',
      'Tus movimientos y cuentas se eliminarán de forma definitiva después del período de gracia. Esta acción no se puede deshacer.',
      [
        { text: 'Cancelar', style: 'cancel' },
        {
          text: 'Eliminar',
          style: 'destructive',
          onPress: () => {
            void api.privacy
              .requestAccountDeletion()
              .then(() => logout())
              .catch(() => Alert.alert('No pudimos procesar la solicitud', 'Inténtalo de nuevo más tarde.'));
          },
        },
      ],
    );
  };

  return (
    <Screen>
      <View style={styles.header}>
        <Typo variant="title">Perfil</Typo>
        <Typo variant="caption" color={colors.textSecondary}>
          {user?.email}
        </Typo>
      </View>

      <Card>
        <Row
          icon="eye-off-outline"
          label="Ocultar cantidades"
          hint="Oculta los montos en todas las pantallas."
          right={<Switch value={hidden} onValueChange={toggleAmounts} />}
        />
      </Card>

      <View style={styles.section}>
        <SectionHeader title="Conexiones de correo" />
        <Card>
          {emailConnections && emailConnections.length > 0 ? (
            emailConnections.map((connection) => (
              <Row
                key={connection.id}
                icon="mail-outline"
                label={connection.emailAddress}
                hint={`${connection.providerKind} · ${connection.status}`}
              />
            ))
          ) : (
            <Typo variant="caption" color={colors.textSecondary}>
              Todavía no hay buzones conectados. La detección automática por correo llega en la fase 2,
              cuando existan credenciales de Gmail y Outlook configuradas en el servidor.
            </Typo>
          )}
        </Card>
      </View>

      <View style={styles.section}>
        <SectionHeader title="Privacidad" />
        <Card>
          <Row
            icon="download-outline"
            label="Exportar mis datos"
            hint="Descarga todo lo que Nexo guarda de ti."
            onPress={() => {
              void Linking.openURL(`${config.apiBaseUrl}/api/v1/privacy/export`).catch(() =>
                Alert.alert('No pudimos abrir la exportación', 'Intenta desde un navegador.'),
              );
            }}
          />
          <Divider />
          <Row
            icon="trash-outline"
            label="Eliminar mi cuenta Nexo"
            hint="Elimina cuentas, movimientos y conexiones."
            tone={colors.danger}
            onPress={confirmDeletion}
          />
        </Card>
      </View>

      <View style={styles.section}>
        <SectionHeader title="Acerca de" />
        <Card>
          <Typo variant="caption" color={colors.textSecondary}>
            Nexo no es un banco ni una billetera y no mueve dinero. Centraliza tus movimientos para
            que entiendas tus finanzas en un solo lugar. Los saldos calculados se muestran siempre
            como estimados.
          </Typo>
        </Card>
      </View>

      <View style={styles.logout}>
        <Button
          label="Cerrar sesión"
          variant="ghost"
          onPress={() => {
            void logout().then(() => router.replace('/(auth)/login'));
          }}
        />
      </View>
    </Screen>
  );
}

interface RowProps {
  icon: keyof typeof Ionicons.glyphMap;
  label: string;
  hint?: string;
  right?: React.ReactNode;
  tone?: string;
  onPress?: () => void;
}

function Row({ icon, label, hint, right, tone = colors.text, onPress }: RowProps) {
  const content = (
    <View style={styles.row}>
      <View style={styles.rowIcon}>
        <Ionicons name={icon} size={17} color={tone} />
      </View>

      <View style={styles.rowBody}>
        <Typo variant="body" color={tone}>
          {label}
        </Typo>
        {hint ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {hint}
          </Typo>
        ) : null}
      </View>

      {right ?? (onPress ? <Ionicons name="chevron-forward" size={16} color={colors.textSecondary} /> : null)}
    </View>
  );

  if (!onPress) {
    return content;
  }

  return (
    <View onTouchEnd={onPress} accessibilityRole="button">
      {content}
    </View>
  );
}

function Divider() {
  return <View style={styles.divider} />;
}

const styles = StyleSheet.create({
  header: {
    marginBottom: spacing.xl,
    gap: 2,
  },
  section: {
    marginTop: spacing.xxl,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    paddingVertical: spacing.sm,
  },
  rowIcon: {
    width: 36,
    height: 36,
    borderRadius: radius.sm,
    backgroundColor: colors.surfaceSecondary,
    alignItems: 'center',
    justifyContent: 'center',
  },
  rowBody: {
    flex: 1,
    gap: 2,
  },
  divider: {
    height: 1,
    backgroundColor: colors.border,
    marginVertical: spacing.md,
  },
  logout: {
    marginTop: spacing.xxxl,
  },
});
