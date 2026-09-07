---
name: Warm Human Fintech
colors:
  surface: '#fbf9f3'
  surface-dim: '#dcdad4'
  surface-bright: '#fbf9f3'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#f5f3ed'
  surface-container: '#f0eee8'
  surface-container-high: '#eae8e2'
  surface-container-highest: '#e4e2dd'
  on-surface: '#1b1c18'
  on-surface-variant: '#464741'
  inverse-surface: '#30312d'
  inverse-on-surface: '#f3f1eb'
  outline: '#777771'
  outline-variant: '#c7c7bf'
  surface-tint: '#5f5e5c'
  primary: '#020302'
  on-primary: '#ffffff'
  primary-container: '#1d1d1b'
  on-primary-container: '#868582'
  inverse-primary: '#c8c6c3'
  secondary: '#4b6700'
  on-secondary: '#ffffff'
  secondary-container: '#c6f26a'
  on-secondary-container: '#506e00'
  tertiary: '#000301'
  on-tertiary: '#ffffff'
  tertiary-container: '#002216'
  on-tertiary-container: '#489273'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#e5e2de'
  primary-fixed-dim: '#c8c6c3'
  on-primary-fixed: '#1c1c1a'
  on-primary-fixed-variant: '#474744'
  secondary-fixed: '#c6f26a'
  secondary-fixed-dim: '#abd551'
  on-secondary-fixed: '#141f00'
  on-secondary-fixed-variant: '#374e00'
  tertiary-fixed: '#a6f2ce'
  tertiary-fixed-dim: '#8ad6b3'
  on-tertiary-fixed: '#002115'
  on-tertiary-fixed-variant: '#005139'
  background: '#fbf9f3'
  on-background: '#1b1c18'
  surface-variant: '#e4e2dd'
typography:
  display:
    fontFamily: Plus Jakarta Sans
    fontSize: 40px
    fontWeight: '700'
    lineHeight: 48px
    letterSpacing: -0.03em
  headline-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 38px
    letterSpacing: -0.025em
  headline-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 30px
    letterSpacing: -0.02em
  headline-sm:
    fontFamily: Plus Jakarta Sans
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 26px
    letterSpacing: -0.015em
  title-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 17px
    fontWeight: '600'
    lineHeight: 22px
    letterSpacing: -0.01em
  body-lg:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
    letterSpacing: -0.011em
  body-md:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '400'
    lineHeight: 20px
    letterSpacing: -0.006em
  body-sm:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '400'
    lineHeight: 16px
    letterSpacing: 0em
  label-lg:
    fontFamily: Plus Jakarta Sans
    fontSize: 14px
    fontWeight: '600'
    lineHeight: 18px
    letterSpacing: 0.01em
  label-md:
    fontFamily: Plus Jakarta Sans
    fontSize: 12px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.02em
  currency-display:
    fontFamily: Plus Jakarta Sans
    fontSize: 36px
    fontWeight: '700'
    lineHeight: 42px
    letterSpacing: -0.03em
rounded:
  sm: 0.5rem
  DEFAULT: 1rem
  md: 1.5rem
  lg: 2rem
  xl: 3rem
  full: 9999px
spacing:
  space-2xs: 0.25rem
  space-xs: 0.5rem
  space-sm: 0.75rem
  space-md: 1rem
  space-lg: 1.25rem
  space-xl: 1.5rem
  space-2xl: 2rem
  space-3xl: 2.5rem
  gutter-mobile: 1rem
  margin-mobile: 1.25rem
  max-width-mobile: 24.375rem
---

## Brand & Style

This design system establishes a warm, tactile, and human-centric approach to consumer personal finance in Latin America. Rejecting the sterile, corporate blue palettes and crypto-adjacent dark interfaces typical of legacy banking apps, the identity prioritizes clarity, calm reassurance, and daily approachability.

The emotional core is defined by soft structure: organic ivory tones replace harsh clinical whites, dense graphite grounds the visual weight, and energetic lime accents bring intentional moments of celebration without visual fatigue. The experience feels closer to an editorial lifestyle journal than an intimidating transactional ledger.

Targeted primarily at everyday mobile users navigating unified personal banking, transfers, and daily budgeting, the visual language balances the geometric optimism of modern display typography with the rigorous, systematic utility of neutral body copy.

## Colors

The palette is anchored in an ivory foundation paired with deep graphite ink, avoiding pure digital blacks or cold neutral grays.

- **Background & Canvas:** `#F5F3ED` establishes an organic, paper-like warmth that reduces glare and screen fatigue during frequent daily check-ins.
- **Surfaces:** `#FFFFFF` serves as the primary card surface for maximum legibility of critical numerical data. `#ECE9E1` acts as the muted structural surface for form controls, search bars, and nested card containers.
- **Primary Ink & Text:** `#1D1D1B` provides crisp, high-contrast readability for balances, merchant names, and primary calls to action. Muted text is handled by `#74766F`, a warm-undertone gray that retains soft contrast against ivory.
- **Accents:** `#C7F36B` (energizing soft lime) highlights actionable triggers, progress celebrations, and high-priority states. `#8DD9B6` (soft mint) introduces a tranquil secondary counterpoint for balance growth and income tags.
- **Functional Semantics:** Built with softer chromatic saturation—`#4E9F73` for positive cashflow, `#E4A853` for pending alerts and limits, and `#D8665B` for expenses and negative transactions, preventing visual panic.

## Typography

The typographic hierarchy divides structural duty between two complementary typefaces:

1. **Headlines & Display (`Plus Jakarta Sans`):** Selected for its friendly geometric curves and open counters. It lends balance displays, hero metrics, and modal headers an approachable, human character. Letter spacing is tightly kerned on larger display sizes to preserve optical density.
2. **Body & Tabular Readouts (`Inter`):** Utilized for transaction records, account numbers, contextual descriptions, and form helper text. Inter guarantees tall x-height legibility and tabular digit stability when rendering monetary balances across dense listing screens.
3. **Labels & Microcopy (`Plus Jakarta Sans`):** Compact uppercase or semi-bold tags, chips, and button labels employ Jakarta to keep interaction triggers expressive and visually distinct from informational body text.

## Layout & Spacing

This design system is engineered around a primary mobile viewport base of 390px × 844px, scaling seamlessly to larger handheld devices and constrained tablet sheets.

- **Grid Architecture:** A 4-column fluid mobile grid with 16px gutters and 20px outer margin boundaries ensures touch elements sit comfortably within safe thumb zones while maintaining consistent vertical alignment.
- **Rhythm:** An 8pt base grid governs all layout containers, module heights, and list item spacing, with a 4pt sub-unit allocated strictly for dense interior alignments (such as avatar-to-text gaps or input icon paddings).
- **Zonal Distribution:**
  - *Top Nav & Status:* Fixed 48px header height plus device safe-area-inset-top.
  - *Main Content Canvas:* Scrollable vertical stream bounded by 20px horizontal margins, with 24px vertical separation between distinct logical modules (e.g., account carousel to recent activity).
  - *Bottom Action Bar:* 64px floating or docked interaction strip elevated above the device safe-area-inset-bottom, reserved for core navigational pathways.

## Elevation & Depth

Visual depth is achieved through ambient warmth, tonal layering, and ultra-soft diffusion rather than harsh drop shadows or rigid black outlines:

- **Tonal Base Layering:** The foundational background rests at `#F5F3ED`. Layered containers rise above this baseline using `#FFFFFF` for primary cards and `#ECE9E1` for secondary inset wells.
- **Warm Ambient Shadows:** When an element requires physical detachment from the background canvas, shadows utilize an organic, warm-tinted formula rather than neutral black:
  - *Resting Card Elevation (`shadow-sm`):* `0px 4px 20px -2px rgba(29, 29, 27, 0.04), 0px 2px 6px -1px rgba(29, 29, 27, 0.02)`.
  - *Floating Interactive Elements (`shadow-float`):* `0px 12px 32px -4px rgba(29, 29, 27, 0.08), 0px 4px 12px -2px rgba(29, 29, 27, 0.03)`.
- **Low-Contrast Ghost Boundaries:** Cards and surfaces employ a hairline 1px solid border set to `rgba(29, 29, 27, 0.05)` or `#ECE9E1` to retain crisp perimeter definition against light backgrounds in variable ambient lighting conditions.

## Shapes

The shape system adopts a pronounced pill-shaped and curvilinear geometry, reinforcing the tactile, friendly visual personality:

- **Primary Containers & Cards:** Configured with large corner radii (16px to 24px), producing a soft, pebble-like aesthetic that feels organic in the hand.
- **Action Triggers & Pills:** Interactive buttons, search inputs, tags, and category chips utilize full continuous border radii (`rounded-full` / 9999px) to invite direct physical interaction.
- **Progress & Graphic Elements:** Horizontal insight meters and micro-indicators feature rounded end-caps to ensure zero sharp terminations across the UI.

## Components

### Buttons
- **Primary:** Full pill (`rounded-full`), height 52px, background `#1D1D1B`, text `#FFFFFF`, font `Plus Jakarta Sans` semi-bold (15px). Active state scales to 0.98 with subtle opacity reduction.
- **Accent / Promotional:** Full pill, height 52px, background `#C7F36B`, text `#1D1D1B`, font `Plus Jakarta Sans` bold. Reserved for direct action prompts (e.g., "Transferir", "Pagar").
- **Secondary / Subdued:** Full pill, height 44px, background `#ECE9E1`, text `#1D1D1B`, borderless.
- **Ghost:** Height 40px, transparent background, text `#74766F`, hover/focus shifts text to `#1D1D1B`.

### Cards & Account Modules
- **Primary Balance Card:** Surface `#1D1D1B` with white/mint typography, or `#FFFFFF` with warm shadow and subtle graphite details. Corner radius 24px (`rounded-3xl`), internal padding 24px. Contains currency symbol, large display balance, and secondary row with quick-action icon buttons.
- **Secondary Inset Card:** Surface `#ECE9E1`, corner radius 20px, internal padding 16px. Used for sub-accounts, savings pots, or secondary financial metrics.

### Bottom Navigation
- **Architecture:** Fixed bottom bar, height 68px (excluding safe area), background `#FFFFFF` with subtle 1px top border `rgba(29, 29, 27, 0.06)` or floating dock style with 20px lateral margins and 28px corner radius.
- **Tabs (4 Items):** *Inicio*, *Movimientos*, *Cuentas*, *Perfil*.
- **State Styling:** Inactive icons and labels render in `#74766F`. Active item renders in `#1D1D1B` with a subtle micro-pill indicator or soft accent background glow behind the icon.

### Form Inputs & Search Fields
- **Container:** Height 52px, pill-shaped (`rounded-full`) or smooth 16px radius, background `#ECE9E1`, zero initial border.
- **States:** Focused state transitions background to `#FFFFFF` with a 1.5px solid border in `#1D1D1B` and a subtle warm ambient glow.
- **Typography:** Input text in `Inter` regular (15px, `#1D1D1B`), placeholder text in `#74766F`.

### Chips & Horizontal Filtering
- **Design:** Height 36px, full pill shape (`rounded-full`), horizontal padding 16px.
- **Selected:** Background `#1D1D1B`, text `#FFFFFF`.
- **Unselected:** Background `#FFFFFF` or `#ECE9E1`, text `#74766F`, border 1px solid `rgba(29, 29, 27, 0.06)`.

### Lists & Transaction Records
- **Row Architecture:** 64px min-height, borderless, separated by 12px vertical spacing or hairline separator `#ECE9E1`.
- **Visual Anchor:** 44px circular or rounded-2xl icon tile on `#ECE9E1` or soft `#C7F36B` tint.
- **Data Arrangement:** Left stack features counterparty/merchant (`Plus Jakarta Sans` semi-bold 15px) above timestamp/category (`Inter` regular 13px `#74766F`). Right stack features signed amount (`Inter` semi-bold 15px, positive in `#4E9F73`, neutral/outgoing in `#1D1D1B`).

### Insight & Budget Progress Bars
- **Track:** Height 8px to 12px, full pill radius, background `#ECE9E1`.
- **Indicator Fill:** Dynamic gradient or solid block of `#C7F36B` or `#8DD9B6`, with smooth rounded caps at the terminus.