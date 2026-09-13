import { StyleSheet, View } from 'react-native';
import { Ionicons } from '@expo/vector-icons';
import { colors, spacing } from '../../../theme';
import { Typo } from '../../ui';
import { HighlightArea } from './HighlightArea';
import { AnimatedTap } from './AnimatedTap';
import { AnimatedFile } from './AnimatedFile';
import type { BankTutorialScreen } from '../../../lib/bankTutorials/types';

interface FakeBankScreenProps {
  screen: BankTutorialScreen;
  bankTitle: string;
  brandColor: string;
  accountMask?: string;
}

const ACCOUNT_ROWS = ['Cuenta de ahorros', 'Cuenta corriente'];
const MENU_ROWS = ['Movimientos', 'Estados de cuenta', 'Transferencias'];
const PERIOD_ROWS = ['Este mes', 'Mes anterior', 'Rango personalizado'];

/**
 * La representación educativa de "la app de tu banco" -- deliberadamente
 * genérica (spec: "NO intentar copiar pixel-perfect la aplicación
 * bancaria"). Lo único que varía por banco es el color de marca y el
 * nombre; la estructura (cuentas → menú → periodo → descargar) es la misma
 * para los cuatro, porque es la estructura real que describe cada parser,
 * no una réplica de ningún banco en particular.
 *
 * Compone HighlightArea + AnimatedTap sobre la fila relevante de cada
 * pantalla -- quien use este componente (BankTutorial, Fase 3) solo decide
 * QUÉ pantalla mostrar, nunca cómo se ve el resaltado.
 */
export function FakeBankScreen({ screen, bankTitle, brandColor, accountMask = 'XXXX 1234' }: FakeBankScreenProps) {
  return (
    <View style={styles.root}>
      <View style={[styles.header, { backgroundColor: brandColor }]}>
        <Typo variant="caption" color="#FFFFFF" style={styles.headerTitle}>
          {bankTitle}
        </Typo>
      </View>

      <View style={styles.body}>{renderBody(screen, accountMask)}</View>
    </View>
  );
}

function renderBody(screen: BankTutorialScreen, accountMask: string) {
  switch (screen) {
    case 'accounts':
      return (
        <>
          <Typo variant="overline" color={colors.textSecondary}>
            TUS CUENTAS
          </Typo>
          {ACCOUNT_ROWS.map((label, index) =>
            index === 0 ? (
              <HighlightArea key={label} style={styles.rowHighlight}>
                <Row label={label} detail={accountMask} tappable />
              </HighlightArea>
            ) : (
              <Row key={label} label={label} muted />
            ),
          )}
        </>
      );

    case 'account-detail':
    case 'statements':
      return (
        <>
          <Typo variant="overline" color={colors.textSecondary}>
            {accountMask}
          </Typo>
          {MENU_ROWS.map((label) =>
            label === 'Estados de cuenta' ? (
              <HighlightArea key={label} style={styles.rowHighlight}>
                <Row label={label} tappable />
              </HighlightArea>
            ) : (
              <Row key={label} label={label} muted />
            ),
          )}
        </>
      );

    case 'period':
      return (
        <>
          <Typo variant="overline" color={colors.textSecondary}>
            SELECCIONA EL PERIODO
          </Typo>
          {PERIOD_ROWS.map((label) =>
            label === 'Este mes' ? (
              <HighlightArea key={label} style={styles.rowHighlight}>
                <Row label={label} tappable />
              </HighlightArea>
            ) : (
              <Row key={label} label={label} muted />
            ),
          )}
        </>
      );

    case 'download':
      return (
        <View style={styles.downloadWrap}>
          <HighlightArea style={styles.downloadButton}>
            <View style={styles.downloadContent}>
              <Ionicons name="download-outline" size={18} color={colors.text} />
              <Typo variant="bodyStrong" color={colors.text}>
                Descargar
              </Typo>
            </View>
            <AnimatedTap style={styles.downloadTap} />
          </HighlightArea>
          <AnimatedFile label="movimientos.xlsx" style={styles.downloadFile} />
        </View>
      );

    default:
      return null;
  }
}

function Row({ label, detail, muted, tappable }: { label: string; detail?: string; muted?: boolean; tappable?: boolean }) {
  return (
    <View style={styles.row}>
      <View style={styles.rowText}>
        <Typo variant="body" color={muted ? colors.textSecondary : colors.text}>
          {label}
        </Typo>
        {detail ? (
          <Typo variant="caption" color={colors.textSecondary}>
            {detail}
          </Typo>
        ) : null}
      </View>
      {tappable ? <AnimatedTap size={20} /> : <Ionicons name="chevron-forward" size={14} color={colors.textSecondary} />}
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
  },
  header: {
    height: 44,
    justifyContent: 'center',
    paddingHorizontal: spacing.md,
  },
  headerTitle: {
    fontWeight: '700',
  },
  body: {
    flex: 1,
    padding: spacing.md,
    gap: spacing.sm,
  },
  row: {
    flexDirection: 'row',
    alignItems: 'center',
    justifyContent: 'space-between',
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.sm,
  },
  rowHighlight: {
    marginVertical: 2,
  },
  rowText: {
    gap: 1,
  },
  downloadWrap: {
    flex: 1,
    alignItems: 'center',
    justifyContent: 'center',
    gap: spacing.lg,
  },
  downloadButton: {
    paddingVertical: spacing.sm,
    paddingHorizontal: spacing.lg,
  },
  downloadContent: {
    flexDirection: 'row',
    alignItems: 'center',
    gap: spacing.xs,
  },
  downloadTap: {
    position: 'absolute',
    right: -6,
    top: -6,
  },
  downloadFile: {
    opacity: 0.9,
  },
});
