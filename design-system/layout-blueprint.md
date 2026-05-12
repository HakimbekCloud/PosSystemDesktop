# Full POS Layout Blueprint

This blueprint turns the design-system tokens into a complete application layout architecture. It defines structure and UX hierarchy only; business logic, data fetching, pricing, sync, and commands belong outside this layer.

## Global Shell

```text
AppShell
  TopNavigation
  Body
    SidebarNavigation
    WorkspaceRouter
      CashierWorkspace
      InventoryWorkspace
      AnalyticsWorkspace
```

The shell is persistent. Switching modules should replace only the workspace, not the top bar or sidebar. This preserves cashier orientation and avoids full-screen context loss.

## Top Navigation

Top navigation is a 68px operational status bar.

Left group:

- Brand mark and `ShefPos`
- Optional store/register label

Center group:

- Sync status pill
- Online/offline indicator
- Pending sync/error badges

Right group:

- Quick tools: sync, settings, history, network monitor
- User account
- Clock/date
- Logout

UX rule: status must be visible but secondary to cashier work. Avoid oversized controls in the top bar.

## Sidebar Navigation

Desktop sidebar width is `184px`. Tablet compact width is `72px`. Mobile uses bottom navigation.

Navigation order:

1. Sotuv
2. Mahsulotlar
3. Ombor
4. Hisobotlar
5. Mijozlar
6. Yetkazib beruvchilar
7. Xodimlar
8. Sozlamalar

The active item uses brand tint, brand text, and a strong icon. Inactive items stay neutral and readable.

## Cashier Workspace

Desktop structure:

```text
CashierWorkspace
  ProductPanel: fluid
  CartPanel: 370px
  PaymentPanel: 290px
```

ProductPanel:

- Sticky `ProductSearch`
- `CategoryTabs` fast-access row
- Optional view controls and page size controls
- `ProductGrid` with selected, hover, and out-of-stock states

CartPanel:

- Customer selector
- Cart table with product, quantity, and line total
- Sticky totals area
- Clear cart secondary action

PaymentPanel:

- Payment method segmented control
- Paid amount input
- Change display
- Tablet keypad slot
- Sticky checkout button

Cashier hierarchy:

1. Scan/search product
2. Add/edit cart quantity
3. Confirm total
4. Enter payment
5. Checkout

## Inventory Workspace

Desktop structure:

```text
InventoryWorkspace
  FilterPanel: 300px
  InventoryTablePanel: fluid
  DetailActionPanel: 360px
```

FilterPanel contains search, barcode lookup, product type, stock state, sync state, and low-stock quick filters.

InventoryTablePanel contains a dense table with sticky header, row selection, stock quantity, unit, price, last sync, and status.

DetailActionPanel contains selected product summary, edit action, stock adjustment action, price list controls, and sync metadata.

Tablet rule: hide the detail panel behind a slide-over drawer when there is not enough horizontal space.

## Analytics Workspace

Analytics uses a 12-column grid and prioritizes operational decisions.

Top row:

- Today sales
- Order count
- Average basket
- Refunds/voids
- Pending sync
- Low-stock alerts

Middle row:

- Sales trend chart: 8 columns
- Category breakdown: 4 columns

Bottom row:

- Recent sales
- Exceptions
- Cashier performance

Keep date range and export actions sticky in the analytics header.

## Multi-Panel Rules

Every panel follows this structure:

```text
Panel
  PanelHeader
  FastAccessBar?
  PanelBody
  StickyActions?
```

Rules:

- Panel headers contain orientation, not marketing copy.
- Fast-access bars contain controls used repeatedly during the shift.
- Sticky actions are reserved for checkout, save, apply filter, or confirm operations.
- Internal panel scrolling is allowed; full app scrolling should be avoided on desktop.

## Tablet And Mobile Adaptation

Tablet:

- Sidebar collapses to icons.
- Cashier layout becomes products left, cart/payment stacked right.
- Checkout stays visible at the bottom of the payment region.
- Product grid reduces metadata before reducing tap target size.

Mobile:

- Bottom navigation replaces sidebar.
- Workspaces become single column.
- Cart and payment may become bottom sheets.
- Checkout remains sticky and reachable with one thumb.

## Fast Cashier Interaction Model

Primary keyboard flow:

1. `/` focuses product search.
2. Scanner input always routes to product lookup unless a modal owns focus.
3. `Enter` confirms the focused product or active checkout action.
4. Arrow keys adjust focused cart quantity.
5. `Esc` closes popovers and drawers.
6. `F2` focuses paid amount.
7. `F9` focuses checkout.

Minimum target sizes:

- Standard controls: `44px`
- Primary checkout: `56px`
- Product cards: `160px x 176px` minimum on desktop
- Quantity controls: `28px` minimum, preferably `32px`

## File Map

- `tokens.json`: source design tokens
- `tailwind.preset.js`: Tailwind token bridge
- `layouts.css`: reusable component and layout classes
- `layout-architecture.md`: layout principles and class map
- `layout-regions.json`: machine-readable layout regions
- `layout-blueprint.md`: full module blueprint and UX hierarchy
