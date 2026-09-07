// Keeps unit tests free of native module noise: only the modules the tested
// logic actually touches are stubbed.
jest.mock('expo-secure-store', () => ({
  getItemAsync: jest.fn(async () => null),
  setItemAsync: jest.fn(async () => undefined),
  deleteItemAsync: jest.fn(async () => undefined),
}));

jest.mock('expo-constants', () => ({
  __esModule: true,
  default: { expoConfig: { extra: { apiBaseUrl: 'http://localhost:5080' } } },
}));

jest.mock('expo-haptics', () => ({
  selectionAsync: jest.fn(async () => undefined),
  impactAsync: jest.fn(async () => undefined),
  notificationAsync: jest.fn(async () => undefined),
}));

/**
 * @expo/vector-icons pulls in expo-font, which needs the native runtime. The
 * icon itself is never what a component test asserts on, so every icon set
 * renders as an inert view carrying its name.
 */
jest.mock('@expo/vector-icons', () => {
  const React = require('react');
  const { View } = require('react-native');

  const Icon = ({ name, ...rest }: { name?: string }) =>
    React.createElement(View, { accessibilityLabel: name, ...rest });

  return new Proxy(
    { __esModule: true },
    {
      get: (target: Record<string, unknown>, prop: string) =>
        prop in target ? target[prop] : Icon,
    },
  );
});
