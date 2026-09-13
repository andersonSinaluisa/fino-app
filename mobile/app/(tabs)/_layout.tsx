import { Redirect, Tabs, usePathname } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';
import { Platform, StyleSheet, View } from 'react-native';
import { colors, typography } from '../../theme';
import { useAuthStore } from '../../store/authStore';
import { usePushRegistration } from '../../hooks/usePushRegistration';
import { useAnalyticsUserProperties } from '../../hooks/useAnalyticsUserProperties';
import { useRealtime } from '../../hooks/useRealtime';
import { usePendingShare } from '../../hooks/usePendingShare';
import { usePendingIntent } from '../../hooks/usePendingIntent';
import { QuickEntryFab } from '../../components/quick-entry/QuickEntryFab';
import { QuickCashEntrySheet } from '../../components/quick-entry/QuickCashEntrySheet';

/**
 * §1 y §26: el botón "+" vive AQUÍ, una sola vez, y no dentro de cada pantalla.
 * Aparece en Inicio y en Movimientos, que son las dos pantallas donde el §1 lo pide;
 * en Estadísticas, Cuentas y Perfil no, porque ahí sería ruido -- nadie llega a
 * Estadísticas para registrar un gasto.
 */
const FAB_ROUTES: Readonly<Record<string, 'home' | 'movements'>> = {
  '/': 'home',
  '/movimientos': 'movements',
};

const TAB_BAR_HEIGHT = Platform.OS === 'ios' ? 86 : 66;

export default function TabsLayout() {
  const status = useAuthStore((state) => state.status);
  const pathname = usePathname();

  usePushRegistration();
  useAnalyticsUserProperties();
  useRealtime();
  // Recoge un archivo compartido desde fuera de la app. Va aquí, en el layout de
  // las pestañas, porque tiene que estar vivo toda la sesión autenticada y no
  // depender de qué pantalla esté encima.
  usePendingShare();
  // §29: abre el sheet cuando la persona llegó desde Siri o desde Atajos.
  usePendingIntent();

  if (status === 'anonymous') {
    return <Redirect href="/(auth)/login" />;
  }

  const fabSource = FAB_ROUTES[pathname];

  return (
    <View style={styles.root}>
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor: colors.text,
        tabBarInactiveTintColor: colors.textSecondary,
        tabBarStyle: styles.bar,
        tabBarLabelStyle: styles.label,
        tabBarItemStyle: styles.item,
        sceneStyle: { backgroundColor: colors.background },
      }}
    >
      <Tabs.Screen
        name="index"
        options={{
          title: 'Inicio',
          tabBarIcon: ({ color, focused }) => (
            <Ionicons name={focused ? 'home' : 'home-outline'} size={22} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="movimientos"
        options={{
          title: 'Movimientos',
          tabBarIcon: ({ color, focused }) => (
            <Ionicons name={focused ? 'swap-vertical' : 'swap-vertical-outline'} size={22} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="estadisticas"
        options={{
          title: 'Estadísticas',
          tabBarIcon: ({ color, focused }) => (
            <Ionicons name={focused ? 'stats-chart' : 'stats-chart-outline'} size={22} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="cuentas"
        options={{
          title: 'Cuentas',
          tabBarIcon: ({ color, focused }) => (
            <Ionicons name={focused ? 'wallet' : 'wallet-outline'} size={22} color={color} />
          ),
        }}
      />
      <Tabs.Screen
        name="perfil"
        options={{
          title: 'Perfil',
          tabBarIcon: ({ color, focused }) => (
            <Ionicons name={focused ? 'person' : 'person-outline'} size={22} color={color} />
          ),
        }}
      />
    </Tabs>

      {fabSource ? <QuickEntryFab source={fabSource} bottomOffset={TAB_BAR_HEIGHT} /> : null}

      {/* El sheet se monta una vez, fuera de las pestañas, para que abrirlo desde
          cualquier sitio (el FAB, un atajo del sistema, un widget) no dependa de
          qué pantalla esté encima. */}
      <QuickCashEntrySheet />
    </View>
  );
}

const styles = StyleSheet.create({
  root: {
    flex: 1,
  },
  bar: {
    backgroundColor: colors.surface,
    borderTopWidth: 1,
    borderTopColor: colors.border,
    height: TAB_BAR_HEIGHT,
    paddingTop: 8,
  },
  label: {
    fontSize: typography.overline.fontSize,
    fontWeight: '600',
    letterSpacing: 0.2,
    marginTop: 2,
  },
  item: {
    paddingVertical: 4,
  },
});
