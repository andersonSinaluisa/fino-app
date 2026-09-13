import { useState } from 'react';
import { useEffect } from 'react';
import { Pressable, StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, radius, spacing } from '../../theme';
import { Badge, Button, Typo } from '../ui';
import { useToast } from '../ui/Toast';
import { useConfirmWithdrawal, useRejectWithdrawal } from '../../hooks/queries';
import { formatCurrency, formatShortDate } from '../../utils/format';
import type { WithdrawalCandidate } from '../../types/api';
import { AnalyticsEvent, AnalyticsSource, toAnalyticsSymbol, track } from '../../services/analytics';

/**
 * §4: "¿Retiraste este dinero en efectivo?"
 *
 * La tarjeta se parece deliberadamente a la de transferencias, porque conceptualmente
 * ES la misma pregunta: ¿este movimiento salió de tu patrimonio, o solo cambió de
 * sitio? La respuesta afecta a si cuenta como gasto en todas partes.
 *
 * Lo que la tarjeta NO hace: no divide importes ni adivina comisiones. Cuando hay
 * varias coincidencias posibles con efectivo ya registrado, lo dice y no elige --
 * §13 prohíbe conciliar automáticamente en caso de ambigüedad.
 */
interface WithdrawalCardProps {
  candidate: WithdrawalCandidate;
  /** Se llama tras confirmar o descartar, para que la lista se reordene. */
  onResolved?: () => void;
}

export function WithdrawalCard({ candidate, onResolved }: WithdrawalCardProps) {
  const toast = useToast();
  const confirm = useConfirmWithdrawal();
  const reject = useRejectWithdrawal();
  const [remember, setRemember] = useState(false);

  const busy = confirm.isPending || reject.isPending;
  const strong = candidate.confidence === 'High';

  /**
   * §25 (Dashboard 6): el denominador del embudo de retiros.
   *
   * El detector corre en el servidor, pero lo que cuenta para producto es
   * cuántos retiros llegaron a estar DELANTE de alguien -- un candidato que
   * nadie vio no puede confirmarse ni rechazarse. `confidence` es la
   * dimensión que dice si el umbral del WithdrawalDetector está bien puesto.
   */
  const confidence = toAnalyticsSymbol(candidate.confidence);

  useEffect(() => {
    track(AnalyticsEvent.WithdrawalDetected, { confidence });
  }, [confidence]);
  const ambiguous = candidate.ambiguousMatchCount > 1;

  const handleConfirm = () => {
    // §25 (Dashboard 6): la confianza del detector es la dimensión que
    // importa aquí -- responde "¿la gente confirma los retiros que Fino
    // marca como High y rechaza los Medium?", que es lo que dice si el
    // umbral del detector está bien puesto. Ni el monto ni la cuenta salen.
    track(AnalyticsEvent.WithdrawalConfirmedAsCash, { confidence, source: AnalyticsSource.Accounts });

    confirm.mutate(
      {
        transactionId: candidate.transactionId,
        body: {
          // §12: si Fino encontró UN ingreso de efectivo que cuadra, se usa ese en
          // vez de crear otro. Con varios candidatos no se manda ninguno y el
          // servidor crea la pata, que es lo honesto: mejor un movimiento nuevo que
          // vincular el equivocado.
          cashTransactionId: ambiguous ? null : candidate.suggestedCashTransactionId,
          rememberPattern: remember && candidate.canLearnPattern,
        },
      },
      {
        onSuccess: (result) => {
          onResolved?.();
          toast.show({
            message: result.matchedExistingCashMovement
              ? `Conciliado con tu efectivo · ${formatCurrency(result.amount)}`
              : `Listo. Movimos ${formatCurrency(result.amount)} a ${result.cashAccountAlias}.`,
          });
        },
        onError: () =>
          toast.show({ message: 'No pudimos conciliar el retiro. Inténtalo otra vez.', tone: 'error' }),
      },
    );
  };

  const handleReject = () => {
    track(AnalyticsEvent.WithdrawalRejected, { confidence, source: AnalyticsSource.Accounts });

    reject.mutate(candidate.transactionId, {
      onSuccess: () => {
        onResolved?.();
        toast.show({ message: 'Anotado: lo dejamos como gasto.' });
      },
      onError: () =>
        toast.show({ message: 'No pudimos guardarlo. Inténtalo otra vez.', tone: 'error' }),
    });
  };

  return (
    <View style={styles.card} testID={`withdrawal-${candidate.transactionId}`}>
      <View style={styles.headerRow}>
        <View style={styles.mark}>
          <Ionicons name="cash-outline" size={16} color={colors.primary} />
        </View>
        <Typo variant="subheading" style={styles.flex}>
          ¿Retiraste este dinero en efectivo?
        </Typo>
      </View>

      <View style={styles.movement}>
        <View style={styles.flex}>
          <Typo variant="caption" color={colors.textSecondary}>
            {candidate.accountAlias} · {formatShortDate(candidate.transactionDate)}
          </Typo>
          <Typo variant="bodyStrong" numberOfLines={1}>
            {candidate.description}
          </Typo>
        </View>
        <Typo variant="bodyStrong" tabular>
          −{formatCurrency(candidate.amount)}
        </Typo>
      </View>

      {/* §3: la confianza cambia el TONO de la pregunta, no la pregunta. Fino nunca
          decide solo, pero tampoco finge dudar cuando el extracto dice "RETIRO ATM". */}
      <Typo variant="caption" color={colors.textSecondary}>
        {strong
          ? 'Parece un retiro en efectivo.'
          : 'Podría ser un retiro en efectivo, pero no estamos seguros.'}
      </Typo>

      {candidate.suggestedCashTransactionId && !ambiguous ? (
        <View style={styles.hint}>
          <Ionicons name="link-outline" size={16} color={colors.success} />
          <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
            Encontramos un ingreso en efectivo del mismo monto. Lo vincularemos en vez de crear otro.
          </Typo>
        </View>
      ) : null}

      {ambiguous ? (
        <View style={styles.hint}>
          <Ionicons name="alert-circle-outline" size={16} color={colors.warning} />
          <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
            Hay {candidate.ambiguousMatchCount} ingresos en efectivo que cuadran. Registraremos el
            retiro por separado para no vincular el equivocado.
          </Typo>
        </View>
      ) : null}

      {/* §19: se OFRECE automatizar, nunca se activa solo. */}
      {candidate.canLearnPattern && candidate.timesConfirmedBefore > 0 ? (
        <Pressable
          accessibilityRole="checkbox"
          accessibilityState={{ checked: remember }}
          accessibilityLabel="Recordar que estos movimientos son retiros"
          onPress={() => {
            // Tocar "recordar este patrón" es la señal más honesta de que la
            // persona LEYÓ la tarjeta en vez de despacharla de un toque.
            track(AnalyticsEvent.WithdrawalReviewed, { confidence });
            setRemember((value) => !value);
          }}
          style={styles.remember}
        >
          <Ionicons
            name={remember ? 'checkbox' : 'square-outline'}
            size={20}
            color={remember ? colors.text : colors.textSecondary}
          />
          <Typo variant="caption" color={colors.textSecondary} style={styles.flex}>
            Recordar que "{candidate.matchedTerm ?? 'esto'}" en {candidate.accountAlias} es un retiro
          </Typo>
        </Pressable>
      ) : null}

      <View style={styles.actions}>
        <Button
          label="Sí, pasó a Efectivo"
          onPress={handleConfirm}
          loading={confirm.isPending}
          loadingLabel="Conciliando..."
          disabled={busy}
        />
        <Button label="No, fue un gasto" variant="ghost" onPress={handleReject} disabled={busy} />
      </View>

      {strong ? null : <Badge label="Revisar con calma" tone="neutral" />}
    </View>
  );
}

const styles = StyleSheet.create({
  card: {
    backgroundColor: colors.surface,
    borderRadius: radius.lg,
    padding: spacing.lg,
    gap: spacing.md,
  },
  headerRow: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
  },
  mark: {
    width: 28,
    height: 28,
    borderRadius: radius.pill,
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: colors.accent,
  },
  movement: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.md,
    padding: spacing.md,
    borderRadius: radius.md,
    backgroundColor: colors.background,
  },
  hint: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    gap: spacing.sm,
  },
  remember: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.sm,
    minHeight: 44,
  },
  actions: {
    gap: spacing.sm,
  },
  flex: {
    flex: 1,
  },
});
