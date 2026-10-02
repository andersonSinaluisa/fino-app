import { StyleSheet, View } from 'react-native';
import { colors, spacing } from '../../theme';
import { Badge, Typo } from '../ui';
import { formatCurrency } from '../../utils/format';
import { STATEMENT_STATUS_PRESENTATION, formatCycleRange, formatDueDate } from '../../utils/creditCards';
import type { CreditCardStatement } from '../../types/api';

interface StatementRowProps {
  statement: CreditCardStatement;
  hidden: boolean;
}

/**
 * Un estado de cuenta: total, mínimo, pagado y pendiente, tal como los manda el
 * backend. "Oficial" cuando las cifras vienen del banco (importadas o escritas
 * por la persona); si no, son las que Fino calcula de los movimientos.
 */
export function StatementRow({ statement, hidden }: StatementRowProps) {
  const money = (value: number) => formatCurrency(value, { hidden });
  const status = STATEMENT_STATUS_PRESENTATION[statement.status];

  return (
    <View style={styles.row}>
      <View style={styles.between}>
        <View style={styles.flex}>
          <Typo variant="bodyStrong">
            {statement.isCurrent ? 'Ciclo actual' : `Corte ${formatDueDate(statement.closingDate)}`}
          </Typo>
          <Typo variant="caption" color={colors.textSecondary}>
            {formatCycleRange(statement.periodStart, statement.closingDate)} · paga hasta {formatDueDate(statement.dueDate)}
          </Typo>
        </View>
        <Badge label={status.label} tone={status.tone} />
      </View>

      <View style={styles.grid}>
        <Figure label={statement.isCurrent ? 'Lleva' : 'Total'} value={money(statement.statementBalance)} />
        {statement.minimumPayment !== null ? <Figure label="Mínimo" value={money(statement.minimumPayment)} /> : null}
        {!statement.isCurrent ? <Figure label="Pagado" value={money(statement.amountPaid)} /> : null}
        {!statement.isCurrent ? <Figure label="Pendiente" value={money(statement.pending)} strong /> : null}
      </View>

      {statement.isDeclared ? (
        <Typo variant="caption" color={colors.textSecondary}>
          Cifras oficiales del banco{statement.declaredSource === 'Imported' ? ' (estado importado)' : ''}.
        </Typo>
      ) : null}
    </View>
  );
}

function Figure({ label, value, strong = false }: { label: string; value: string; strong?: boolean }) {
  return (
    <View style={styles.figure}>
      <Typo variant="overline" color={colors.textSecondary}>
        {label.toUpperCase()}
      </Typo>
      <Typo variant={strong ? 'bodyStrong' : 'body'} tabular>
        {value}
      </Typo>
    </View>
  );
}

const styles = StyleSheet.create({
  row: {
    paddingVertical: spacing.md,
    gap: spacing.sm,
  },
  between: {
    flexDirection: 'row',
    alignItems: 'flex-start',
    justifyContent: 'space-between',
    gap: spacing.md,
  },
  flex: {
    flex: 1,
    gap: 2,
  },
  grid: {
    flexDirection: 'row',
    flexWrap: 'wrap',
    gap: spacing.lg,
  },
  figure: {
    minWidth: 70,
    gap: 2,
  },
});
