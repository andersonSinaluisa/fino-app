import { resolveBaseUrlForRuntime } from '../services/config';

describe('resolveBaseUrlForRuntime', () => {
  it('keeps an explicit environment URL untouched', () => {
    expect(
      resolveBaseUrlForRuntime(
        { expoConfig: { extra: { apiBaseUrl: 'http://localhost:5080' }, hostUri: '192.168.1.20:8081' } },
        'ios',
        'https://api.example.com/',
      ),
    ).toBe('https://api.example.com');
  });

  it('maps localhost to the Android emulator host', () => {
    expect(
      resolveBaseUrlForRuntime(
        { expoConfig: { extra: { apiBaseUrl: 'http://localhost:5080' } } },
        'android',
      ),
    ).toBe('http://10.0.2.2:5080');
  });

  it('maps localhost to Metro LAN host for Expo Go on iPhone', () => {
    expect(
      resolveBaseUrlForRuntime(
        { expoConfig: { extra: { apiBaseUrl: 'http://localhost:5080' }, hostUri: '192.168.100.110:8081' } },
        'ios',
      ),
    ).toBe('http://192.168.100.110:5080');
  });
});
