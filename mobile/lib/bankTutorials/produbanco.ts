import type { BankImportConfig } from './types';

/**
 * Produbanco -- ver types.ts para qué significa `verified`. Los 4 pasos son la
 * estructura real (cuentas → estados de cuenta → periodo → descargar), pero
 * los nombres exactos de menú dentro de la app de Produbanco todavía no se
 * confirmaron contra capturas reales.
 */
export const produbanco: BankImportConfig = {
  bankId: 'PRODUBANCO',
  displayName: 'Produbanco',
  supportedFormats: ['csv', 'xls', 'xlsx'],
  fileHint: 'No necesitas modificarlo. FINO lo organiza por ti.',
  tutorial: {
    bankId: 'PRODUBANCO',
    title: 'Produbanco',
    tutorialVersion: 1,
    verified: false,
    lastUpdated: '2026-09-09',
    steps: [
      {
        screen: 'accounts',
        target: 'account',
        instruction: 'Abre Produbanco y selecciona tu cuenta',
      },
      {
        screen: 'account-detail',
        target: 'statements',
        instruction: 'Busca "Estados de cuenta" o "Consultas"',
      },
      {
        screen: 'period',
        target: 'current-month',
        instruction: 'Selecciona el periodo actual',
      },
      {
        screen: 'download',
        target: 'download',
        instruction: 'Descarga el archivo',
      },
    ],
  },
};
