import { useEffect } from 'react';
import { Redirect, useLocalSearchParams } from 'expo-router';
import { useQuickEntryStore } from '../store/quickEntryStore';
import { useAuthStore } from '../store/authStore';

/**
 * §28-30: el punto de entrada externo al registro rápido.
 *
 * Esta pantalla no dibuja nada. Existe para que TODO lo que vive fuera de la app
 * --el widget de Android, el de iOS, el atajo de mantener pulsado el icono, un App
 * Intent, Siri-- tenga una sola dirección a la que apuntar: `fino:///registrar`.
 * Abre el sheet y devuelve a la persona a Inicio, así que cerrar el sheet la deja
 * dentro de Fino con normalidad en vez de en una pantalla huérfana con un botón de
 * "atrás" que no lleva a ningún sitio.
 *
 * Es también lo que hace cierta la promesa del §28: "separar la operación central
 * CreateQuickTransaction de la UI". Un punto de entrada nuevo se añade apuntando
 * aquí, sin tocar ni la creación del movimiento ni el sheet.
 */
export default function QuickEntryEntryPoint() {
  const params = useLocalSearchParams<{ voz?: string }>();
  const status = useAuthStore((state) => state.status);
  const open = useQuickEntryStore((state) => state.open);

  const withVoice = params.voz === '1';

  useEffect(() => {
    // Nunca antes de saber quién es: abrir el sheet sobre la pantalla de login
    // pediría registrar un gasto a alguien que todavía no ha entrado.
    if (status !== 'authenticated') {
      return;
    }

    open('shortcut', { withVoice });
  }, [open, status, withVoice]);

  if (status === 'anonymous') {
    return <Redirect href="/(auth)/login" />;
  }

  if (status === 'loading') {
    // La sesión todavía se está restaurando desde SecureStore. Un instante sin
    // pintar nada es mejor que un parpadeo de login para alguien que sí tiene
    // sesión válida.
    return null;
  }

  return <Redirect href="/(tabs)" />;
}
