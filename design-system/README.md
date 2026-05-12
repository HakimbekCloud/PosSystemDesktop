# ShefPos POS Design System

This foundation defines the UI rules for a modern enterprise POS experience. It is page-free by design: use it to build screens, WPF resources, or Tailwind views without changing business logic.

## Design Principles

- POS-first: primary actions must be visible, large, and reachable without hunting.
- Cashier speed: search, scan, quantity edits, totals, and checkout take priority over decoration.
- Enterprise clarity: dense information is acceptable when hierarchy, spacing, and states stay consistent.
- Desktop-first: optimize for 1366px+ cashier terminals, then adapt to tablet widths.
- Keyboard-friendly: every input and action needs a visible focus state and predictable tab order.

## Token Architecture

Use `tokens.json` as the source of truth and `tailwind.preset.js` for Tailwind projects.

- Color roles: `brand`, `neutral`, `success`, `warning`, `danger`, `info`, plus semantic `light` and `dark` surfaces.
- Typography: Inter/SF/Segoe stack, with `xs` through `3xl`; use `sm` and `base` for operational UI.
- Spacing: 4px base scale. Prefer `2`, `3`, `4`, and `5` for controls and panels.
- Radius: `lg` for buttons and inputs, `xl` for cards and panels, `full` for pills.
- Shadows: use `xs` and `sm` by default; reserve `brand` for primary actions.

## Component Naming

Use composable names with POS intent:

- `AppShell`, `TopBar`, `SideNav`, `StatusPill`
- `ProductSearch`, `CategoryTabs`, `ProductCard`, `ProductGrid`
- `CartPanel`, `CartTable`, `QuantityStepper`, `TotalsSummary`
- `PaymentPanel`, `PaymentMethodToggle`, `AmountInput`, `CheckoutButton`
- `EmptyState`, `IconButton`, `ActionButton`, `Popover`, `ModalSheet`

Variants should follow `component / intent / size / state`, for example:

- `Button.primary.lg.hover`
- `ProductCard.default.selected`
- `StatusPill.sync.online`
- `PaymentMethodToggle.cash.active`

## Layout Structure

Use a stable three-area POS shell:

```text
TopBar: 68px
SideNav: 184px desktop, 72px tablet compact
Main POS area:
  Product workspace: fluid
  Cart panel: 360-400px
  Payment panel: 280-320px
```

Desktop layout keeps products, cart, and payment visible at once. Tablet layout can collapse the side nav and stack payment under cart, but checkout must remain sticky.

## Interaction Rules

- Minimum touch target: 44px; checkout target: 56px+.
- Focus ring: brand blue border with soft blue shadow.
- Hover: subtle background lift, never layout shift.
- Selected: use a blue border, tinted background, and explicit check/active indicator.
- Disabled: reduce contrast but keep labels readable.
- Motion: 120-180ms for hover/press; 260ms only for panels/popovers.

## Tailwind Usage

Import the preset in a Tailwind project:

```js
module.exports = {
  presets: [require('./design-system/tailwind.preset')],
  content: ['./src/**/*.{ts,tsx,html}']
};
```

Example component classes:

```html
<button class="h-12 rounded-lg bg-brand-500 px-5 font-semibold text-white shadow-brand transition hover:bg-brand-600 focus:outline-none focus:ring-4 focus:ring-brand-100">
  SOTISH
</button>
```

## POS Component Defaults

- Product cards: `rounded-xl`, `border-neutral-200`, `shadow-sm`, selected with `border-brand-500`.
- Search inputs: 46-48px height, leading icon, clear placeholder, strong focus ring.
- Category tabs: pill buttons, active filled brand, inactive white with neutral border.
- Cart rows: compact but readable, 48px minimum height, right-aligned money.
- Totals: subtotal muted, `JAMI` bold brand, largest number in the checkout area.
- Payment buttons: segmented, 48px height, active brand tint plus border.

## Layout Architecture

Use `layout-architecture.md` for module hierarchy and `layouts.css` for Tailwind component classes. These files define the app shell, sidebar, top navigation, cashier workspace, inventory workspace, analytics workspace, sticky action zones, and tablet adaptation without adding business logic.

For a fuller implementation blueprint, use `layout-blueprint.md`. For machine-readable layout regions, use `layout-regions.json`.
