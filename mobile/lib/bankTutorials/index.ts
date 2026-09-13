import { ACCEPTED_EXTENSIONS } from '../importFileTypes';
import { guayaquil } from './guayaquil';
import { pichincha } from './pichincha';
import { pacifico } from './pacifico';
import { produbanco } from './produbanco';
import type { BankImportConfig } from './types';

export type { BankImportConfig, BankTutorialConfig, BankTutorialStep, BankTutorialScreen } from './types';

/**
 * Registro por código de proveedor -- la MISMA clave que ya usa el backend
 * (Nexo.Domain.Providers.ProviderCodes) y que ya llega en cada `Provider.code`
 * de useProviders(). Agregar un banco nuevo es agregar un archivo aquí y una
 * línea en este objeto; ninguna pantalla necesita `if (bankId === ...)`.
 */
const registry: Record<string, BankImportConfig> = {
  [guayaquil.bankId]: guayaquil,
  [pichincha.bankId]: pichincha,
  [pacifico.bankId]: pacifico,
  [produbanco.bankId]: produbanco,
};

if (__DEV__) {
  for (const config of Object.values(registry)) {
    const unsupported = config.supportedFormats.filter((format) => !ACCEPTED_EXTENSIONS.includes(format));
    if (unsupported.length > 0) {
      // Un banco nunca debe prometer un formato que el importador real rechazaría.
      console.warn(
        `[bankTutorials] ${config.bankId} declara formatos que ACCEPTED_EXTENSIONS no acepta: ${unsupported.join(', ')}`,
      );
    }
  }
}

/** El tutorial para un `Provider.code` dado, o null si todavía no tiene uno (banco sin tutorial armado aún -- no es un error, solo falta contenido). */
export function getBankImportConfig(providerCode: string): BankImportConfig | null {
  return registry[providerCode.toUpperCase()] ?? null;
}

/** Todos los bancos con tutorial armado -- para cualquier pantalla que necesite listarlos todos en vez de resolver uno. */
export function listBankImportConfigs(): BankImportConfig[] {
  return Object.values(registry);
}
