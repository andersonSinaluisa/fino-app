/**
 * Nexo's visual identity.
 *
 * Deliberately not the default fintech blue: a warm paper background, near-black
 * ink, and a single high-energy lime accent used sparingly. Everything else is
 * quiet so the numbers are the loudest thing on the screen.
 */
export const colors = {
  background: '#F5F3ED',
  surface: '#FFFFFF',
  surfaceSecondary: '#ECE9E1',
  primary: '#1D1D1B',
  accent: '#C7F36B',
  accentSecondary: '#8DD9B6',
  text: '#191A18',
  textSecondary: '#74766F',
  success: '#4E9F73',
  warning: '#E4A853',
  danger: '#D8665B',
  border: 'rgba(25, 26, 24, 0.08)',
  borderStrong: 'rgba(25, 26, 24, 0.16)',
  overlay: 'rgba(25, 26, 24, 0.45)',
  onPrimary: '#F5F3ED',
  onAccent: '#1D1D1B',
} as const;

export type ColorName = keyof typeof colors;

/** 4-point scale. Generous whitespace is part of the brand, not an afterthought. */
export const spacing = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  xxl: 32,
  xxxl: 48,
} as const;

export const radius = {
  sm: 10,
  md: 16,
  lg: 22,
  xl: 28,
  pill: 999,
} as const;

/**
 * One display face for money, one text face for everything else.
 * Sizes jump in clear steps so hierarchy never depends on colour alone.
 */
export const typography = {
  display: { fontSize: 44, lineHeight: 48, fontWeight: '700', letterSpacing: -1.4 },
  title: { fontSize: 28, lineHeight: 34, fontWeight: '700', letterSpacing: -0.7 },
  heading: { fontSize: 20, lineHeight: 26, fontWeight: '700', letterSpacing: -0.3 },
  subheading: { fontSize: 17, lineHeight: 23, fontWeight: '600', letterSpacing: -0.2 },
  body: { fontSize: 15, lineHeight: 21, fontWeight: '500', letterSpacing: -0.1 },
  bodyStrong: { fontSize: 15, lineHeight: 21, fontWeight: '700', letterSpacing: -0.1 },
  caption: { fontSize: 13, lineHeight: 18, fontWeight: '500', letterSpacing: 0 },
  overline: { fontSize: 11, lineHeight: 14, fontWeight: '700', letterSpacing: 0.9 },
} as const;

export type TypographyVariant = keyof typeof typography;

/** Shadows stay barely-there; depth comes from spacing and contrast instead. */
export const elevation = {
  none: {},
  card: {
    shadowColor: '#1D1D1B',
    shadowOpacity: 0.05,
    shadowRadius: 18,
    shadowOffset: { width: 0, height: 6 },
    elevation: 2,
  },
  raised: {
    shadowColor: '#1D1D1B',
    shadowOpacity: 0.1,
    shadowRadius: 28,
    shadowOffset: { width: 0, height: 12 },
    elevation: 6,
  },
} as const;

export const motion = {
  fast: 140,
  base: 220,
  slow: 320,
} as const;

export const theme = { colors, spacing, radius, typography, elevation, motion } as const;

export type Theme = typeof theme;
