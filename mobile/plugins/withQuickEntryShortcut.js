const {
  withAndroidManifest,
  withDangerousMod,
  withStringsXml,
  AndroidConfig,
} = require('expo/config-plugins');
const fs = require('node:fs');
const path = require('node:path');

/**
 * §30 ("Android Shortcut"): "preparar equivalente 'Registrar gasto' desde long
 * press del icono de FINO. Puede abrir directamente QuickCashEntry."
 *
 * Plugin de configuración local, sin dependencias nuevas (`expo/config-plugins`
 * viene dentro del propio paquete `expo`), en la misma línea que
 * withShareIntent.js.
 *
 * Los atajos son ESTÁTICOS (declarados en res/xml/shortcuts.xml) y no dinámicos a
 * propósito: un atajo dinámico habría que registrarlo desde JavaScript al arrancar,
 * lo que significa que no existiría hasta que alguien abriera la app al menos una
 * vez, y desaparecería si Android matara el proceso. Un atajo estático está ahí
 * desde la instalación y no depende de que la app haya corrido nunca.
 *
 * Los dos atajos abren `fino:///registrar`, el mismo deep link que usan el widget y
 * (en iOS) el App Intent -- una sola dirección para todos los puntos de entrada
 * externos.
 */

const SHORTCUTS_XML = `<?xml version="1.0" encoding="utf-8"?>
<!--
  Generado por plugins/withQuickEntryShortcut.js. No editar a mano: prebuild lo
  reescribe.

  §30: "Registrar gasto" desde el long press del icono de Fino. El segundo atajo es
  el equivalente por voz del §13. Ambos apuntan al mismo deep link que el widget,
  fino:///registrar, para que exista un único camino de entrada externo.
-->
<shortcuts xmlns:android="http://schemas.android.com/apk/res/android">
    <shortcut
        android:shortcutId="registrar_efectivo"
        android:enabled="true"
        android:icon="@mipmap/ic_launcher"
        android:shortcutShortLabel="@string/shortcut_quick_entry_short"
        android:shortcutLongLabel="@string/shortcut_quick_entry_long">
        <intent
            android:action="android.intent.action.VIEW"
            android:targetPackage="{{PACKAGE}}"
            android:targetClass="{{PACKAGE}}.MainActivity"
            android:data="fino:///registrar" />
    </shortcut>

    <shortcut
        android:shortcutId="registrar_por_voz"
        android:enabled="true"
        android:icon="@mipmap/ic_launcher"
        android:shortcutShortLabel="@string/shortcut_voice_entry_short"
        android:shortcutLongLabel="@string/shortcut_voice_entry_long">
        <intent
            android:action="android.intent.action.VIEW"
            android:targetPackage="{{PACKAGE}}"
            android:targetClass="{{PACKAGE}}.MainActivity"
            android:data="fino:///registrar?voz=1" />
    </shortcut>
</shortcuts>
`;

const STRINGS = [
  ['shortcut_quick_entry_short', 'Registrar'],
  ['shortcut_quick_entry_long', 'Registrar gasto en efectivo'],
  ['shortcut_voice_entry_short', 'Dictar'],
  ['shortcut_voice_entry_long', 'Registrar gasto por voz'],
];

function withQuickEntryShortcut(config) {
  config = withShortcutsXml(config);
  config = withShortcutStrings(config);
  config = withShortcutsMetadata(config);
  return config;
}

/** Escribe res/xml/shortcuts.xml con el nombre de paquete real ya sustituido. */
function withShortcutsXml(config) {
  return withDangerousMod(config, [
    'android',
    (config) => {
      const packageName = config.android?.package;

      if (!packageName) {
        // Sin package no se puede apuntar a MainActivity. Se avisa y se sigue: un
        // atajo que falta no debe impedir compilar la app entera.
        console.warn('[fino-quick-entry-shortcut] No hay android.package; se omiten los atajos.');
        return config;
      }

      const xmlDir = path.join(config.modRequest.platformProjectRoot, 'app', 'src', 'main', 'res', 'xml');
      fs.mkdirSync(xmlDir, { recursive: true });
      fs.writeFileSync(
        path.join(xmlDir, 'shortcuts.xml'),
        SHORTCUTS_XML.replaceAll('{{PACKAGE}}', packageName),
        'utf8',
      );

      return config;
    },
  ]);
}

/**
 * Las etiquetas van en strings.xml y no incrustadas en shortcuts.xml porque
 * Android exige un recurso de cadena (`@string/...`) en shortcutShortLabel: un
 * literal ahí no compila. De paso quedan traducibles.
 */
function withShortcutStrings(config) {
  // `withStringsXml` es una exportación de primer nivel de expo/config-plugins;
  // solo `setStringItem` vive bajo AndroidConfig.Strings.
  return withStringsXml(config, (config) => {
    for (const [name, value] of STRINGS) {
      config.modResults = AndroidConfig.Strings.setStringItem(
        [{ $: { name, translatable: 'false' }, _: value }],
        config.modResults,
      );
    }

    return config;
  });
}

/** Enlaza shortcuts.xml con MainActivity mediante el meta-data que Android espera. */
function withShortcutsMetadata(config) {
  return withAndroidManifest(config, (config) => {
    const application = config.modResults.manifest.application?.[0];
    const mainActivity = application?.activity?.find(
      (activity) => activity.$['android:name'] === '.MainActivity',
    );

    if (!mainActivity) {
      console.warn('[fino-quick-entry-shortcut] No se encontró MainActivity; se omite el meta-data.');
      return config;
    }

    mainActivity['meta-data'] = mainActivity['meta-data'] ?? [];

    const already = mainActivity['meta-data'].some(
      (entry) => entry.$['android:name'] === 'android.app.shortcuts',
    );

    // Idempotente: prebuild puede correr sobre un android/ ya generado.
    if (!already) {
      mainActivity['meta-data'].push({
        $: { 'android:name': 'android.app.shortcuts', 'android:resource': '@xml/shortcuts' },
      });
    }

    return config;
  });
}

module.exports = withQuickEntryShortcut;
