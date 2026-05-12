# POS Layout Architecture

This architecture extends the token system without adding business logic. It defines reusable shells, panels, and responsive rules for cashier, inventory, and analytics workflows.

## Application Shell

Use one persistent shell for all operational modules:

```text
AppShell
  TopBar
  MainShell
    SidebarNavigation
    Workspace
```

`TopBar` owns global status: sync, online/offline state, current user, clock/date, quick tools, and logout. Keep it 68px on desktop so it stays compact but touch-safe.

`SidebarNavigation` owns module switching: Sotuv, Mahsulotlar, Ombor, Hisobotlar, Mijozlar, Yetkazib beruvchilar, Xodimlar, Sozlamalar. Desktop width is 184px. Tablet compact width is 72px with labels hidden.

`Workspace` changes layout by module while preserving shared panel behavior.

## Cashier Workspace

Cashier flow must keep scan/search, cart, payment, and checkout visible:

```text
Workspace.cashier
  ProductPanel: fluid
  CartPanel: 360-400px
  PaymentPanel: 280-320px
```

Hierarchy:

- ProductPanel: search first, category fast-access second, product grid third.
- CartPanel: customer selection, cart rows, totals, clear cart action.
- PaymentPanel: payment method, amount input, change, keypad when tablet mode is active, sticky checkout.

Sticky areas:

- Product search stays at panel top.
- Cart totals stay above clear/cart actions.
- Checkout remains bottom-sticky in the payment panel.

Fast-access sections:

- Product search accepts scanner and keyboard input immediately.
- Category chips are horizontal and one-tap.
- Quantity steppers support mouse, touch, and keyboard arrows.

## Inventory Workspace

Inventory management uses a tri-panel layout:

```text
Workspace.inventory
  FilterPanel: 300px
  InventoryTablePanel: fluid
  DetailActionPanel: 360px
```

FilterPanel contains search, category/type filters, stock status filters, and sync status. InventoryTablePanel uses dense rows with sticky headers. DetailActionPanel is reserved for selected item details, edit actions, price lists, and stock adjustment commands.

## Analytics Workspace

Analytics uses a 12-column dashboard grid:

```text
Workspace.analytics
  KPI cards: 3 columns desktop
  Revenue / sales chart: 8 columns
  Category breakdown: 4 columns
  Recent activity / exceptions: full width or side panel
```

Analytics is scanning-first, not decoration-first. Put today's sales, average basket, refunds, pending sync, and low-stock alerts above charts.

## Multi-Panel Support

Panels follow the same anatomy:

```text
Panel
  PanelHeader
  FastAccessBar? optional
  PanelBody
  StickyActions? optional
```

Use `pos-panel`, `pos-panel-header`, `pos-panel-body`, and `pos-sticky-actions` from `layouts.css`.

Panel rules:

- One primary action per panel.
- Destructive actions are secondary unless the task is explicitly destructive.
- Empty states should explain the next cashier action in one short line.
- Panels may scroll internally; the app shell should not create nested page-level scrolling.

## Tablet Adaptation

At tablet width:

- Sidebar collapses to icons.
- Cashier layout becomes two columns: products left, cart/payment stacked right.
- Payment actions remain sticky.
- Product cards keep 44px+ targets and reduce metadata before reducing target size.

At narrow width:

- Sidebar becomes a bottom navigation rail.
- Workspace becomes single-column.
- Checkout remains sticky at the bottom.

## Keyboard Model

Default focus order:

1. Product search
2. Category tabs
3. Product grid
4. Cart rows and quantity controls
5. Discount
6. Payment method
7. Paid amount
8. Checkout

Required shortcuts should be reserved at the architecture level:

- `/`: focus product search
- `Enter`: add focused product or confirm checkout when checkout is focused
- `ArrowLeft` / `ArrowRight`: adjust focused cart quantity
- `Esc`: close popup, modal, or secondary panel
- `F2`: payment amount
- `F9`: checkout

## Class Map

- Shell: `pos-app-shell`, `pos-topbar`, `pos-main-shell`
- Navigation: `pos-sidebar`, `pos-sidebar-nav`, `pos-nav-item`, `pos-nav-item-active`
- Workspaces: `pos-workspace`, `pos-workspace-cashier`, `pos-workspace-inventory`, `pos-workspace-analytics`
- Panels: `pos-panel`, `pos-panel-muted`, `pos-panel-header`, `pos-panel-body`, `pos-sticky-actions`
- POS controls: `pos-search-bar`, `pos-fast-access-bar`, `pos-card`, `pos-card-selected`, `pos-action-primary`, `pos-action-success`
- Tables: `pos-table-shell`, `pos-table-header`, `pos-table-row`

## Implementation Boundary

This layer should not contain API calls, persistence, pricing logic, sync behavior, permission checks, or cart calculations. It only defines layout, hierarchy, sizing, interaction affordances, and reusable class names.
