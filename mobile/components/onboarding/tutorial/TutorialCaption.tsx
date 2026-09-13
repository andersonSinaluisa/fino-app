import { useEffect, useRef } from 'react';
import { Animated, StyleSheet } from 'react-native';
import { colors } from '../../../theme';
import { Typo } from '../../ui';
import { useReduceMotion } from '../../../hooks/useReduceMotion';

interface TutorialCaptionProps {
  text: string;
}

/**
 * El texto bajo la animación ("Paso 1 de 4" + instrucción). Cambia con un
 * crossfade corto en vez de saltar de golpe cuando `text` cambia -- 150-250ms,
 * el rango de "microinteracciones de UI" del spec, no de demostración. Con
 * Reduce Motion, el texto simplemente cambia sin fundido.
 */
export function TutorialCaption({ text }: TutorialCaptionProps) {
  const reduceMotion = useReduceMotion();
  const opacity = useRef(new Animated.Value(1)).current;
  const previousText = useRef(text);

  useEffect(() => {
    if (previousText.current === text) {
      return;
    }
    previousText.current = text;

    if (reduceMotion) {
      return;
    }

    opacity.setValue(0);
    Animated.timing(opacity, { toValue: 1, duration: 200, useNativeDriver: true }).start();
  }, [text, reduceMotion, opacity]);

  return (
    <Animated.View style={reduceMotion ? undefined : { opacity }}>
      <Typo variant="subheading" color={colors.text} align="center" style={styles.text}>
        {text}
      </Typo>
    </Animated.View>
  );
}

const styles = StyleSheet.create({
  text: {
    paddingHorizontal: 12,
  },
});
