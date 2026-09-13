import type { BankImportConfig } from './types';

/**
 * Banco Pichincha -- ver types.ts para qué significa `verified`. Los 4 pasos son la
 * estructura real (cuentas → estados de cuenta → periodo → descargar), pero
 * los nombres exactos de menú dentro de la app de Banco Pichincha todavía no se
 * confirmaron contra capturas reales.
 */
export const pichincha: BankImportConfig = {
  bankId: 'PICHINCHA',
  displayName: 'Banco Pichincha',
  supportedFormats: ['csv', 'xls', 'xlsx'],
  fileHint: 'No necesitas modificarlo. FINO lo organiza por ti.',
  tutorial: {
    bankId: 'PICHINCHA',
    title: 'Banco Pichincha',
    tutorialVersion: 1,
    verified: false,
    lastUpdated: '2026-09-09',
    steps: [
      {
        screen: 'accounts',
        target: 'account',
        instruction: 'Abre Banco Pichincha y selecciona tu cuenta',
      },
      {
        screen: 'account-detail',
        target: 'statements',
        instruction: 'Busca "Movimientos" o "Estados de cuenta"',
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
