import { useMemo, useState } from 'react';
import { BarChart3, Boxes, CalendarDays, CreditCard, Gamepad2, History, LogOut, Package, Search, Settings, ShoppingCart, Signal, Truck, User, Users, Wifi, Wrench } from 'lucide-react';
import { categories, cartLines, products } from './mockData';
import type { PaymentMethod } from './types';
import { ProductGrid } from './components/ProductGrid';
import { CartPanel } from './components/CartPanel';
import { PaymentPanel } from './components/PaymentPanel';
import { ShortcutBar } from './components/ShortcutBar';

export function CashierShell() {
  const [query, setQuery] = useState('');
  const [category, setCategory] = useState('Barchasi');
  const [paymentMethod, setPaymentMethod] = useState<PaymentMethod>('cash');
  const [selectedProductId, setSelectedProductId] = useState('p1');

  const filteredProducts = useMemo(() => {
    const normalized = query.trim().toLowerCase();
    return products.filter((product) => {
      const matchesCategory = category === 'Barchasi' || product.category === category;
      const matchesQuery =
        normalized.length === 0 ||
        product.name.toLowerCase().includes(normalized) ||
        product.barcode.includes(normalized);

      return matchesCategory && matchesQuery;
    });
  }, [category, query]);

  return (
    <div className="pos-app-shell">
      <TopNavigation />
      <div className="pos-main-shell">
        <SidebarNavigation />
        <main className="pos-workspace pos-workspace-cashier">
          <section className="pos-panel pos-products-panel flex min-w-0 flex-col">
            <div className="pos-panel-header gap-4">
              <div className="relative min-w-0 flex-1">
                <Search className="pointer-events-none absolute left-4 top-1/2 size-5 -translate-y-1/2 text-neutral-400" />
                <input
                  value={query}
                  onChange={(event) => setQuery(event.target.value)}
                  className="pos-search-bar pl-12"
                  placeholder="Mahsulot qidirish (nomi, kodi, barkod)..."
                  aria-label="Product search"
                />
              </div>
              <button className="pos-action-primary h-12 w-12 px-0 text-2xl" aria-label="Add product">
                +
              </button>
            </div>

            <div className="pos-fast-access-bar">
              {categories.map((item) => (
                <button
                  key={item}
                  onClick={() => setCategory(item)}
                  className={[
                    'h-10 whitespace-nowrap rounded-full border px-4 text-sm font-semibold transition-colors duration-fast cashier-focus',
                    item === category
                      ? 'border-brand-500 bg-brand-500 text-white shadow-sm'
                      : 'border-neutral-200 bg-white text-neutral-600 hover:border-brand-200 hover:bg-brand-50 hover:text-brand-600'
                  ].join(' ')}
                >
                  {item}
                </button>
              ))}
            </div>

            <ProductGrid
              products={filteredProducts}
              selectedProductId={selectedProductId}
              onSelectProduct={setSelectedProductId}
            />
          </section>

          <CartPanel lines={cartLines} />

          <PaymentPanel
            method={paymentMethod}
            onMethodChange={setPaymentMethod}
            subtotal={51000}
            discount={0}
            paidAmount={60000}
          />
        </main>
      </div>
      <ShortcutBar />
    </div>
  );
}

function TopNavigation() {
  return (
    <header className="pos-topbar">
      <div className="pos-topbar-section">
        <div className="flex size-10 items-center justify-center rounded-xl bg-white/15">
          <ShoppingCart className="size-5" />
        </div>
        <div>
          <div className="text-lg font-bold leading-5">ShefPos</div>
          <div className="text-xs text-brand-100">Terminal 01</div>
        </div>
      </div>

      <div className="pos-topbar-section hidden tablet:flex">
        <div className="pos-status-pill">
          <span className="size-2 rounded-full bg-success-500" />
          Sinxronlanmoqda...
        </div>
        <div className="pos-status-pill">
          <Wifi className="size-4" />
          Onlayn
        </div>
      </div>

      <div className="pos-topbar-section">
        <IconTopButton label="History" icon={<History />} />
        <IconTopButton label="Calendar" icon={<CalendarDays />} />
        <IconTopButton label="Tools" icon={<Wrench />} />
        <IconTopButton label="Game" icon={<Gamepad2 />} />
        <IconTopButton label="Settings" icon={<Settings />} />
        <div className="hidden items-center gap-2 rounded-full border border-white/15 bg-white/15 px-3 py-2 text-sm font-semibold desktop:flex">
          <User className="size-4" />
          admin
        </div>
        <div className="hidden rounded-full border border-white/15 bg-white/15 px-3 py-2 font-mono text-sm desktop:block">
          16:43:41
        </div>
        <button className="inline-flex h-10 items-center gap-2 rounded-xl bg-danger-600 px-4 text-sm font-bold text-white transition-colors duration-fast hover:bg-danger-700">
          <LogOut className="size-4" />
          Chiqish
        </button>
      </div>
    </header>
  );
}

function IconTopButton({ icon, label }: { icon: React.ReactNode; label: string }) {
  return (
    <button aria-label={label} className="hidden size-10 place-items-center rounded-xl text-white/90 transition-colors duration-fast hover:bg-white/15 tablet:grid">
      <span className="[&_svg]:size-5">{icon}</span>
    </button>
  );
}

function SidebarNavigation() {
  const items = [
    ['Sotuv', ShoppingCart, true],
    ['Mahsulotlar', Package],
    ['Ombor', Boxes],
    ['Hisobotlar', BarChart3],
    ['Mijozlar', Users],
    ['Yetkazib beruvchilar', Truck],
    ['Xodimlar', User],
    ['Sozlamalar', Settings]
  ] as const;

  return (
    <aside className="pos-sidebar">
      <nav className="pos-sidebar-nav">
        {items.map(([label, Icon, active]) => (
          <button key={label} className={`pos-nav-item ${active ? 'pos-nav-item-active' : ''}`}>
            <Icon className="size-5 shrink-0" />
            <span className="pos-sidebar-label text-left">{label}</span>
          </button>
        ))}
      </nav>

      <div className="pos-sidebar-status">
        <div className="mb-2 flex items-center gap-2 font-semibold text-neutral-800">
          <Signal className="size-4 text-success-600" />
          <span className="pos-sidebar-status-text">Onlayn</span>
        </div>
        <div className="pos-sidebar-status-text text-xs text-neutral-500">Tizim holati</div>
        <div className="pos-sidebar-status-text mt-2 text-xs text-neutral-500">v1.0.0</div>
      </div>
    </aside>
  );
}
