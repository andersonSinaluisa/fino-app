import { Alert, Linking, Pressable, StyleSheet, Switch, View } from 'react-native';
import { useRouter } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Button, Card, Screen, SectionHeader, Typo } from '../../components/ui';
import { useAuthStore } from '../../store/authStore';
import { usePreferencesStore } from '../../store/preferencesStore';
import {
  useEmailConnections,
  useDeleteTransactions,
  useRevokeEmailConnection,
} from '../../hooks/queries';
import { api } from '../../services/endpoints';
import { config } from '../../services/config';

const providerLabel: Record<string, string> = {
  Gmail: 'Gmail',
  Outlook: 'Outlook',
  Forwarding: 'Reenvío de correo',
};

const connectionStatusLabel: Record<string, string> = {
  PendingAuthorization: 'Pendiente de autorizar',
  Connected: 'Conectado',
  NeedsReauthorization: 'Necesita reconectarse',
  Revoked: 'Desconectado',
};

export default function ProfileScreen() {
  const router = useRouter();
  const user = useAuthStore((state) => state.user);
  const logout = useAuthStore((state) => state.logout);
  const hidden = usePreferencesStore((state) => state.amountsHidden);
  const toggleAmounts = usePreferencesStore((state) => state.toggleAmounts);
  const { data: emailConnections } = useEmailConnections();
  const deleteTransactions = useDeleteTransactions();
  const revokeEmail = useRevokeEmailConnection();

  /** Toda acción destructiva pasa por aquí: mismo tono, misma salida. */
  const confirm = (title: string, body: string, action: string, onConfirm: () => void) => {
    Alert.alert(title, body, [
      { text: 'Cancelar', style: 'cancel' },
      { text: action, style: 'destructive', onPress: onConfirm },
    ]);
  };

  const confirmDeletion = () => {
    Alert.alert(
      'Eliminar cuenta Fino',
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
        <Divider />
        <Row
          icon="notifications-outline"
          label="Notificaciones"
          hint="Historial y qué quieres que te avisemos."
          onPress={() => router.push('/notificaciones')}
        />
        <Divider />
        <Row
          icon="phone-portrait-outline"
          label="Sesiones"
          hint="Dispositivos con acceso a tu cuenta ahora mismo."
          onPress={() => router.push('/sesiones')}
        />
        <Divider />
        <Row
          icon="time-outline"
          label="Actividad de la cuenta"
          hint="Inicios de sesión, cambios y exportaciones."
          onPress={() => router.push('/actividad')}
        />
      </Card>

      <View style={styles.section}>
        <SectionHeader title="Conexiones de correo" />
        <Card>
          <Row
            icon="add-circle-outline"
            label="Conectar correo"
            hint="Reenvío de correo ya funciona hoy. Gmail y Outlook llegan en la fase 2."
            onPress={() => router.push('/conectar-correo')}
          />
          {emailConnections && emailConnections.length > 0 ? (
            <>
              <Divider />
              {emailConnections.map((connection, index) => (
                <View key={connection.id}>
                  {index > 0 ? <Divider /> : null}
                  <Row
                    icon="mail-outline"
                    label={connection.emailAddress}
                    hint={`${providerLabel[connection.providerKind] ?? connection.providerKind} · ${
                      connectionStatusLabel[connection.status] ?? connection.status
                    }`}
                    tone={colors.danger}
                    onPress={() =>
                      confirm(
                        'Desconectar correo',
                        `Dejaremos de leer notificaciones de ${connection.emailAddress} y borraremos el permiso guardado. Los movimientos ya detectados se quedan.`,
                        'Desconectar',
                        () => revokeEmail.mutate(connection.id),
                      )
                    }
                  />
                </View>
              ))}
            </>
          ) : null}
        </Card>
      </View>

      <View style={styles.section}>
        <SectionHeader title="Privacidad" />
        <Card>
          <Row
            icon="download-outline"
            label="Exportar mis datos"
            hint="Descarga todo lo que Fino guarda de ti."
            onPress={() => {
              void Linking.openURL(`${config.apiBaseUrl}/api/v1/privacy/export`).catch(() =>
                Alert.alert('No pudimos abrir la exportación', 'Intenta desde un navegador.'),
              );
            }}
          />
          <Divider />
          <Row
            icon="receipt-outline"
            label="Eliminar mis movimientos"
            hint="Borra el historial y deja las cuentas en pie."
            tone={colors.danger}
            onPress={() =>
              confirm(
                'Eliminar mis movimientos',
                'Se eliminarán todos tus movimientos en todas las cuentas. Las cuentas y sus saldos verificados se mantienen. Esta acción no se puede deshacer.',
                'Eliminar',
                () =>
                  deleteTransactions.mutate(undefined, {
                    onSuccess: (result) =>
                      Alert.alert(
                        'Listo',
                        `Eliminamos ${result.transactionsDeleted} movimiento(s).`,
                      ),
                    onError: () =>
                      Alert.alert('No pudimos eliminarlos', 'Inténtalo de nuevo más tarde.'),
                  }),
              )
            }
          />
          <Divider />
          <Row
            icon="trash-outline"
            label="Eliminar mi cuenta Fino"
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
            Fino no es un banco ni una billetera y no mueve dinero. Centraliza tus movimientos para
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

  // Pressable, not a View with onTouchEnd: this is what gives the row its press
  // feedback and makes it a button to a screen reader.
  return (
    <Pressable
      onPress={onPress}
      accessibilityRole="button"
      accessibilityLabel={label}
      style={({ pressed }) => (pressed ? styles.rowPressed : undefined)}
    >
      {content}
    </Pressable>
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
  rowPressed: {
    opacity: 0.6,
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
