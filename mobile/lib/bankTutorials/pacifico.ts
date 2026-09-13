import type { BankImportConfig } from './types';

/**
 * Banco del Pacífico -- ver types.ts para qué significa `verified`. Los 4 pasos son la
 * estructura real (cuentas → estados de cuenta → periodo → descargar), pero
 * los nombres exactos de menú dentro de la app de Banco del Pacífico todavía no se
 * confirmaron contra capturas reales.
 */
export const pacifico: BankImportConfig = {
  bankId: 'PACIFICO',
  displayName: 'Banco del Pacífico',
  supportedFormats: ['csv', 'xls', 'xlsx'],
  fileHint: 'No necesitas modificarlo. FINO lo organiza por ti.',
  tutorial: {
    bankId: 'PACIFICO',
    title: 'Banco del Pacífico',
    tutorialVersion: 1,
    verified: false,
    lastUpdated: '2026-09-09',
    steps: [
      {
        screen: 'accounts',
        target: 'account',
        instruction: 'Abre Banco del Pacífico y selecciona tu cuenta',
      },
      {
        screen: 'account-detail',
        target: 'statements',
        instruction: 'Busca "Estados de cuenta"',
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
