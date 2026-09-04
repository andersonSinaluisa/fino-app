import { useEffect, useState } from 'react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { SafeAreaProvider } from 'react-native-safe-area-context';
import { StatusBar } from 'expo-status-bar';
import { Stack } from 'expo-router';
import { colors } from '../theme';
import { useAuthStore } from '../store/authStore';

export default function RootLayout() {
  const restore = useAuthStore((state) => state.restore);

  const [client] = useState(
    () =>
      new QueryClient({
        defaultOptions: {
          queries: {
            // Financial data is worth a quick refetch, not an aggressive one.
            staleTime: 30_000,
            retry: 1,
            refetchOnWindowFocus: false,
          },
        },
      }),
  );

  useEffect(() => {
    void restore();
  }, [restore]);

  return (
    <SafeAreaProvider>
      <QueryClientProvider client={client}>
        <StatusBar style="dark" />
        <Stack
          screenOptions={{
            headerShown: false,
            contentStyle: { backgroundColor: colors.background },
            animation: 'slide_from_right',
          }}
        >
          <Stack.Screen name="index" />
          <Stack.Screen name="(auth)" />
          <Stack.Screen name="(tabs)" />
          <Stack.Screen name="movimiento/[id]" options={{ presentation: 'card' }} />
          <Stack.Screen name="cuentas/agregar" options={{ presentation: 'modal' }} />
          <Stack.Screen name="cuentas/importar" options={{ presentation: 'modal' }} />
        </Stack>
      </QueryClientProvider>
    </SafeAreaProvider>
  );
}
