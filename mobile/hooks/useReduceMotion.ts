import { useEffect, useState } from 'react';
import { AccessibilityInfo } from 'react-native';

/**
 * Onboarding funcional: every animated primitive in components/onboarding/tutorial
 * reads this instead of assuming motion is welcome. When the OS "Reduce Motion"
 * setting is on, loops and demonstrations become static states/simple fades --
 * never skipped content, just presented without movement (see each primitive's
 * own remarks for what it swaps to).
 */
export function useReduceMotion(): boolean {
  const [reduceMotion, setReduceMotion] = useState(false);

  useEffect(() => {
    let mounted = true;

    AccessibilityInfo.isReduceMotionEnabled()
      .then((enabled) => {
        if (mounted) {
          setReduceMotion(enabled);
        }
      })
      .catch(() => undefined);

    const subscription = AccessibilityInfo.addEventListener('reduceMotionChanged', (enabled) => {
      setReduceMotion(enabled);
    });

    return () => {
      mounted = false;
      subscription.remove();
    };
  }, []);

  return reduceMotion;
}
