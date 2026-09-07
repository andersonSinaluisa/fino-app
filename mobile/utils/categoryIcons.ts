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
};

const FALLBACK: IoniconName = 'ellipse-outline';

export function iconForCategory(icon: string | null | undefined): IoniconName {
  if (!icon) {
    return FALLBACK;
  }

  return ICON_MAP[icon] ?? FALLBACK;
}
