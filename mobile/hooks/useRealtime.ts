import { useEffect, useRef } from 'react';
import { AppState, type AppStateStatus } from 'react-native';
import { useQueryClient } from '@tanstack/react-query';
import * as SignalR from '@microsoft/signalr';
import { config } from '../services/config';
import { queryKeys } from './queries';
import { useAuthStore } from '../store/authStore';

/**
 * Keeps the app in step with the backend.
 *
 * Two layers, on purpose:
 *   1. SignalR, for "a movement just arrived" without the user doing anything.
 *   2. A refetch whenever the app comes back to the foreground.
 *
 * The second one is the floor: if the socket never connects — a restrictive
 * network, a proxy that kills websockets, a bundling surprise on a real device —
 * the app still refreshes, just less immediately. Real time is an enhancement
 * here, never something the product depends on.
 */
export function useRealtime(): void {
  const client = useQueryClient();
  const status = useAuthStore((state) => state.status);
  const tokens = useAuthStore((state) => state.tokens);
  const connectionRef = useRef<SignalR.HubConnection | null>(null);

  const accessToken = tokens?.accessToken;

  useEffect(() => {
    if (status !== 'authenticated' || !accessToken) {
      return;
    }

    const refreshEverything = () => {
      void client.invalidateQueries({ queryKey: queryKeys.summary });
      void client.invalidateQueries({ queryKey: queryKeys.accounts });
      void client.invalidateQueries({ queryKey: ['transactions'] });
      void client.invalidateQueries({ queryKey: queryKeys.insights });
    };

    // --- layer 2: foreground refresh -------------------------------------
    const onAppStateChange = (next: AppStateStatus) => {
      if (next === 'active') {
        refreshEverything();
      }
    };

    const subscription = AppState.addEventListener('change', onAppStateChange);

    // --- layer 1: realtime signal ----------------------------------------
    // The hub carries no financial data: it says "something changed" and the
    // client re-fetches over the authenticated REST API.
    const connection = new SignalR.HubConnectionBuilder()
      .withUrl(config.realtimeUrl, {
        accessTokenFactory: () => useAuthStore.getState().tokens?.accessToken ?? '',
      })
      .withAutomaticReconnect()
      .configureLogging(SignalR.LogLevel.None)
      .build();

    connection.on('transactionsChanged', refreshEverything);
    connection.on('importProgress', () => {
      void client.invalidateQueries({ queryKey: queryKeys.summary });
    });

    connectionRef.current = connection;

    connection.start().catch(() => {
      // Silent by design: the foreground refresh above already covers this.
    });

    return () => {
      subscription.remove();
      connectionRef.current = null;
      void connection.stop().catch(() => undefined);
    };
  }, [status, accessToken, client]);
}
