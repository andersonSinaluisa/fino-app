import { useEffect } from 'react';
import { analytics } from '../services/analytics/service';
import { toSourceCountBucket } from '../services/analytics/buckets';
import { useAuthStore } from '../store/authStore';
import { useDeviceStore } from '../store/deviceStore';
import { useAccounts } from './queries';

/** ProviderCodes.Cash en el backend. La cuenta de efectivo se identifica así. */
const CASH_PROVIDER_CODE = 'EFECTIVO';

/**
 * §5: propiedades de usuario, para poder comparar cohortes.
 *
 * Sin esto, "¿los usuarios que tienen notificaciones activadas retienen más?"
 * no se puede responder: harían falta los eventos Y el atributo, y el
 * atributo no sale de ningún evento.
 *
 * Todas son señales de PRODUCTO. Ninguna dice cuánto dinero hay: cuántas
 * cuentas tiene va en tramos, no como número, y lo que va es si existe una
 * cuenta de efectivo, no su saldo.
 *
 * Se monta en el layout de las tabs, que solo existe con sesión iniciada, y
 * reutiliza la query de cuentas que esas pantallas ya tienen en caché -- no
 * dispara una petición extra.
 */
export function useAnalyticsUserProperties(): void {
  const onboarding = useAuthStore((state) => state.user?.onboarding);
  const preferences = useDeviceStore((state) => state.preferences);
  const { data: accounts } = useAccounts();

  const activeAccounts = accounts?.filter((account) => !account.isArchived);
  const accountCount = activeAccounts?.length;
  const hasImportedStatement = Boolean(onboarding?.hasImportedData);
  const onboardingCompleted = Boolean(onboarding?.firstImportCompletedAt ?? onboarding?.skippedAt);
  const pushEnabled = preferences?.pushEnabled;
  const pulsesEnabled = preferences?.notifyOnPulses;

  useEffect(() => {
    if (accountCount === undefined) {
      return;
    }

    analytics.setUserProperties({
      onboardingCompleted,
      hasImportedStatement,
      // La cuenta EFECTIVO la crea el backend la primera vez que alguien
      // registra efectivo (CashAccountProvisioner), con el providerCode
      // EFECTIVO de ProviderCodes.Cash. Su existencia es exactamente la señal
      // "esta persona usa el camino de efectivo".
      hasCashAccount: Boolean(activeAccounts?.some((account) => account.providerCode === CASH_PROVIDER_CODE)),
      hasNotificationsEnabled: pushEnabled ?? false,
      hasPulseEnabled: pulsesEnabled ?? false,
      numberOfConnectedSourcesBucket: toSourceCountBucket(accountCount),
    });
    // `activeAccounts` cambia de identidad en cada render; lo que importa es
    // el conteo y las señales derivadas.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [accountCount, hasImportedStatement, onboardingCompleted, pushEnabled, pulsesEnabled]);
}
