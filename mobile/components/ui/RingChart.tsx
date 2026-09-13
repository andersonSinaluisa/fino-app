import type { ReactNode } from 'react';
import { StyleSheet, View } from 'react-native';
import { colors } from '../../theme';
import type { DonutSegment } from '../../utils/donutChart';

interface RingChartProps {
  segments: DonutSegment[];
  size: number;
  strokeWidth: number;
  trackColor?: string;
  holeColor?: string;
  children?: ReactNode;
}

/**
 * Anillo de proporciones dibujado solo con Views -- el proyecto evita sumar
 * react-native-svg por un único gráfico (mismo criterio que el line chart de
 * "Tu mes" en Estadísticas). Cada segmento se pinta como una "cuña": un medio
 * círculo recortado (overflow hidden) con un cuadrado rotado por dentro, mitad
 * transparente y mitad del color del segmento. Los segmentos se pintan del
 * ángulo acumulado más grande al más chico (ver utils/donutChart.ts) para que
 * cada porción más chica quede encima y no la tape la anterior.
 */
export function RingChart({
  segments,
  size,
  strokeWidth,
  trackColor = colors.surfaceSecondary,
  holeColor = colors.surface,
  children,
}: RingChartProps) {
  const holeSize = Math.max(0, size - strokeWidth * 2);
  const ordered = [...segments].reverse();

  return (
    <View style={[styles.circle, { width: size, height: size, borderRadius: size / 2, backgroundColor: trackColor }]}>
      {ordered.map((segment) => (
        <Wedge key={segment.id} size={size} angle={segment.cumulativeAngle} color={segment.color} />
      ))}

      <View
        style={[
          styles.hole,
          {
            width: holeSize,
            height: holeSize,
            borderRadius: holeSize / 2,
            top: strokeWidth,
            left: strokeWidth,
            backgroundColor: holeColor,
          },
        ]}
      >
        {children}
      </View>
    </View>
  );
}

function Wedge({ size, angle, color }: { size: number; angle: number; color: string }) {
  const clamped = Math.max(0, Math.min(360, angle));

  if (clamped <= 0) {
    return null;
  }

  // 0-180°: only the right half needs a colored wedge. Past 180°, the right
  // half is fully covered and the remainder spills into the left half.
  const rightAngle = Math.min(clamped, 180);
  const leftAngle = Math.max(0, clamped - 180);

  return (
    <>
      <HalfWedge size={size} half="right" angle={rightAngle} color={color} />
      {leftAngle > 0 ? <HalfWedge size={size} half="left" angle={leftAngle} color={color} /> : null}
    </>
  );
}

function HalfWedge({ size, half, angle, color }: { size: number; half: 'left' | 'right'; angle: number; color: string }) {
  const isRight = half === 'right';

  return (
    <View
      style={[
        styles.halfClip,
        { left: isRight ? size / 2 : 0, top: 0, width: size / 2, height: size },
      ]}
    >
      <View
        style={[
          styles.pieSquare,
          { width: size, height: size, left: isRight ? -size / 2 : 0, transform: [{ rotate: `${angle}deg` }] },
        ]}
      >
        <View
          style={[
            styles.pieHalf,
            { width: size / 2, height: size, left: isRight ? 0 : size / 2, backgroundColor: color },
          ]}
        />
      </View>
    </View>
  );
}

const styles = StyleSheet.create({
  circle: {
    position: 'relative',
    overflow: 'hidden',
  },
  halfClip: {
    position: 'absolute',
    overflow: 'hidden',
  },
  pieSquare: {
    position: 'absolute',
  },
  pieHalf: {
    position: 'absolute',
    top: 0,
  },
  hole: {
    position: 'absolute',
    alignItems: 'center',
    justifyContent: 'center',
  },
});
