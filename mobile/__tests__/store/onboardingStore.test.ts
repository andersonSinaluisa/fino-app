import * as SecureStore from 'expo-secure-store';
import { useOnboardingStore } from '../../store/onboardingStore';

const getItemAsync = SecureStore.getItemAsync as jest.Mock;
const setItemAsync = SecureStore.setItemAsync as jest.Mock;

describe('useOnboardingStore', () => {
  beforeEach(() => {
    getItemAsync.mockReset().mockResolvedValue(null);
    setItemAsync.mockReset().mockResolvedValue(undefined);
    useOnboardingStore.setState({ status: 'loading', hasSeenOnboarding: false });
  });

  it('starts loading and defaults to not-seen once restored with nothing stored', async () => {
    expect(useOnboardingStore.getState().status).toBe('loading');

    await useOnboardingStore.getState().restore();

    expect(useOnboardingStore.getState().status).toBe('ready');
    expect(useOnboardingStore.getState().hasSeenOnboarding).toBe(false);
  });

  it('restores hasSeenOnboarding as true once the flag was previously saved', async () => {
    getItemAsync.mockResolvedValue('1');

    await useOnboardingStore.getState().restore();

    expect(useOnboardingStore.getState().hasSeenOnboarding).toBe(true);
  });

  it('fails closed (not-seen) when SecureStore throws', async () => {
    getItemAsync.mockRejectedValue(new Error('keychain unavailable'));

    await useOnboardingStore.getState().restore();

    expect(useOnboardingStore.getState().status).toBe('ready');
    expect(useOnboardingStore.getState().hasSeenOnboarding).toBe(false);
  });

  it('markSeen flips the flag immediately and persists it', () => {
    useOnboardingStore.getState().markSeen();

    expect(useOnboardingStore.getState().hasSeenOnboarding).toBe(true);
    expect(setItemAsync).toHaveBeenCalledWith('fino.hasSeenOnboarding', '1');
  });
});
