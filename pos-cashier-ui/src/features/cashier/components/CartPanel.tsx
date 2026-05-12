import { Minus, Plus, ShoppingCart, Trash2, UserPlus, X } from 'lucide-react';
import type { CartLine } from '../types';
import { money } from '../format';

type CartPanelProps = {
  lines: CartLine[];
};

export function CartPanel({ lines }: CartPanelProps) {
  const subtotal = lines.reduce((sum, line) => sum + line.unitPrice * line.quantity, 0);

  return (
    <section className="pos-panel pos-cart-panel flex min-w-0 flex-col">
      <div className="pos-panel-header">
        <div className="min-w-0 flex-1">
          <div className="text-xs font-bold uppercase tracking-wide text-neutral-500">Mijoz</div>
          <div className="mt-1 truncate text-sm font-semibold italic text-neutral-400">Mijoz tanlanmagan</div>
        </div>
        <button className="grid size-10 place-items-center rounded-xl border border-neutral-200 bg-white text-neutral-700 transition-colors duration-fast hover:bg-brand-50 hover:text-brand-600 cashier-focus">
          <UserPlus className="size-5" />
        </button>
        <button className="grid size-10 place-items-center rounded-xl border border-neutral-200 bg-white text-neutral-500 transition-colors duration-fast hover:bg-danger-50 hover:text-danger-600 cashier-focus">
          <X className="size-5" />
        </button>
      </div>

      <div className="border-b border-neutral-200 bg-white p-4">
        <input className="pos-search-bar h-11" placeholder="Mijoz qidirish..." aria-label="Customer search" />
      </div>

      <div className="min-h-0 flex-1 overflow-auto">
        {lines.length === 0 ? (
          <div className="grid h-full min-h-[320px] place-items-center">
            <div className="text-center">
              <div className="mx-auto mb-3 grid size-20 place-items-center rounded-full border border-neutral-200 bg-neutral-50">
                <ShoppingCart className="size-10 text-neutral-300" />
              </div>
              <div className="font-semibold text-neutral-700">Savat bo'sh</div>
              <div className="mt-1 text-sm text-neutral-500">Mahsulot qo'shish uchun skanerlang yoki qidiring.</div>
            </div>
          </div>
        ) : (
          <div className="pos-table-shell border-x-0 border-t-0">
            <div className="pos-table-header grid-cols-[1fr_112px_88px_32px]">
              <span>Mahsulot</span>
              <span className="text-center">Soni</span>
              <span className="text-right">Jami</span>
              <span />
            </div>
            {lines.map((line) => (
              <div key={line.id} className="pos-table-row grid-cols-[1fr_112px_88px_32px] gap-2">
                <div className="min-w-0">
                  <div className="truncate font-semibold">{line.productName}</div>
                  <div className="text-xs text-neutral-500">{money(line.unitPrice)} so'm</div>
                </div>
                <div className="grid grid-cols-[28px_1fr_28px] items-center overflow-hidden rounded-lg border border-neutral-200 bg-neutral-50">
                  <button className="grid h-8 place-items-center hover:bg-neutral-100 cashier-focus" aria-label="Decrease quantity">
                    <Minus className="size-4" />
                  </button>
                  <div className="text-center text-sm font-bold">{line.quantity}</div>
                  <button className="grid h-8 place-items-center hover:bg-neutral-100 cashier-focus" aria-label="Increase quantity">
                    <Plus className="size-4" />
                  </button>
                </div>
                <div className="text-right font-bold text-brand-600">{money(line.unitPrice * line.quantity)}</div>
                <button className="grid size-8 place-items-center rounded-lg text-neutral-400 hover:bg-danger-50 hover:text-danger-600 cashier-focus">
                  <X className="size-4" />
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      <div className="pos-sticky-actions">
        <div className="space-y-3">
          <div className="flex items-center justify-between text-sm">
            <span className="text-neutral-500">Subtotal</span>
            <span className="font-semibold">{money(subtotal)} so'm</span>
          </div>
          <div className="flex items-center justify-between gap-3 text-sm">
            <span className="text-neutral-500">Chegirma</span>
            <div className="flex items-center gap-2">
              <input className="h-10 w-28 rounded-lg border border-neutral-200 bg-white px-3 text-right font-semibold cashier-focus" defaultValue="0" />
              <span className="text-neutral-500">so'm</span>
            </div>
          </div>
          <div className="border-t border-neutral-200 pt-3">
            <div className="flex items-center justify-between">
              <span className="text-lg font-bold text-brand-800">JAMI</span>
              <span className="text-2xl font-bold text-brand-600">{money(subtotal)} <span className="text-sm">so'm</span></span>
            </div>
          </div>
          <button className="flex h-11 w-full items-center justify-center gap-2 rounded-xl border border-danger-100 text-sm font-semibold text-danger-600 transition-colors duration-fast hover:bg-danger-50 cashier-focus">
            <Trash2 className="size-4" />
            Savatni tozalash
          </button>
        </div>
      </div>
    </section>
  );
}
