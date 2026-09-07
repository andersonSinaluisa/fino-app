// Custom entry point (replaces the default "expo-router/entry" package
// specifier in package.json's "main") -- needed only so the Android home-
// screen widgets' headless task handler and configuration screen can be
// registered once, at process start, exactly as react-native-android-widget's
// docs describe for an Expo Router app. Everything else about how the app
// boots is unchanged: this still does the exact same "import 'expo-router/entry'"
// expo-router itself would have done as the app's main entry.
import 'expo-router/entry';
import { Platform } from 'react-native';

if (Platform.OS === 'android') {
  // Required with plain `require`, not a static `import`, so this native
  // module is never even loaded into the iOS bundle's module graph.
  const { registerWidgetTaskHandler, registerWidgetConfigurationScreen } = require('react-native-android-widget');
  const { widgetTaskHandler } = require('./lib/widgets/android/taskHandler');
  const { FinoWidgetConfigurationScreen } = require('./lib/widgets/android/configurationScreen');

  registerWidgetTaskHandler(widgetTaskHandler);
  registerWidgetConfigurationScreen(FinoWidgetConfigurationScreen);
}
