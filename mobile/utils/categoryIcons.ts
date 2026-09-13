import type { Ionicons } from '@expo/vector-icons';

type IoniconName = keyof typeof Ionicons.glyphMap;

/**
 * Category.Icon (backend, Nexo.Domain.Categories) is a small semantic vocabulary
 * ("utensils", "shopping-cart", "zap"...) chosen independently of any particular
 * icon font -- it was never actually rendered on mobile before (every movement
 * showed the same up/down arrow regardless of category). Ionicons is the family
 * already used everywhere else in the app, so movements read consistently next
 * to every other screen instead of mixing icon styles.
 *
 * Keep this in sync with ReferenceDataSeeder.SeedCategoriesAsync's Icon column.
 * An icon string with no entry here (a category added on the backend before its
 * glyph is mapped) falls back to a generic dot rather than crashing the list.
 */
const ICON_MAP: Record<string, IoniconName> = {
  utensils: 'restaurant-outline',
  'shopping-cart': 'cart-outline',
  car: 'car-outline',
  zap: 'flash-outline',
  film: 'film-outline',
  heart: 'heart-outline',
  book: 'book-outline',
  'shopping-bag': 'bag-outline',
  repeat: 'repeat-outline',
  'arrow-left-right': 'swap-horizontal-outline',
  percent: 'receipt-outline',
  'trending-up': 'trending-up-outline',
  circle: 'ellipse-outline',
  // Categorías personalizadas: el resto del vocabulario que el backend acepta
  // (Nexo.Domain.Categories.CategoryAppearance.Icons) para una categoría que
  // la persona crea ella misma -- mismo principio que el bloque de arriba,
  // nunca se manda un nombre de Ionicons directamente desde el picker.
  home: 'home-outline',
  gift: 'gift-outline',
  briefcase: 'briefcase-outline',
  paw: 'paw-outline',
  airplane: 'airplane-outline',
  cafe: 'cafe-outline',
  fitness: 'barbell-outline',
  'musical-notes': 'musical-notes-outline',
  'game-controller': 'game-controller-outline',
  cash: 'cash-outline',
  people: 'people-outline',
  construct: 'construct-outline',
};

const FALLBACK: IoniconName = 'ellipse-outline';

export function iconForCategory(icon: string | null | undefined): IoniconName {
  if (!icon) {
    return FALLBACK;
  }

  return ICON_MAP[icon] ?? FALLBACK;
}

/**
 * Categorías personalizadas: las opciones que el picker de ícono muestra,
 * en el mismo orden que el backend valida (CategoryAppearance.Icons) -- una
 * sola fuente de verdad para "qué íconos existen", tomada de ICON_MAP en vez
 * de una segunda lista que se podría desincronizar.
 */
export const CATEGORY_ICON_KEYS: string[] = Object.keys(ICON_MAP);

/** Categorías personalizadas: misma paleta que ya usan las categorías del sistema (ReferenceDataSeeder), para que una categoría propia nunca desentone. */
export const CATEGORY_COLORS: string[] = [
  '#E4A853', '#8DD9B6', '#7FB3E8', '#C7F36B', '#D8A0E8', '#D8665B',
  '#9AA8E8', '#E8B4A0', '#B6A0E8', '#ECE9E1', '#A67C52', '#4E9F73', '#74766F',
];
