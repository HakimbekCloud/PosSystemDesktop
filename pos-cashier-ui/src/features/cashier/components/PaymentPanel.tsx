import { Banknote, Check, CreditCard } from 'lucide-react';
import type { PaymentMethod } from '../types';
import { money } from '../format';

type PaymentPanelProps = {
  method: PaymentMethod;
  onMethodChange: (method: PaymentMethod) => void;
  subtotal: number;
  discount: number;
  paidAmount: number;
};

export function PaymentPanel({ method, onMethodChange, subtotal, discount, paidAmount }: PaymentPanelProps) {
  const total = subtotal - discount;
  const change = paidAmount - total;

  return (
    <section className="pos-panel-muted pos-payment-panel flex min-w-0 flex-col border-r-0">
      <div className="pos-panel-body flex-1 space-y-3 p-3">
        <div className="grid grid-cols-2 gap-2">
          <PaymentMethodButton active={method === 'cash'} label="Naqd" icon={<Banknote />} onClick={() => onMethodChange('cash')} />
          <PaymentMethodButton active={method === 'card'} label="Karta" icon={<CreditCard />} onClick={() => onMethodChange('card')} />
        </div>

        <div className="grid grid-cols-2 gap-2">
          <div className="min-w-0 rounded-xl bg-white p-3">
            <label className="block text-xs font-bold uppercase tracking-wide text-neutral-400">Qabul qilindi</label>
            <div className="mt-1 flex items-baseline gap-1">
              <input className="h-8 min-w-0 flex-1 bg-transparent text-left text-xl font-bold text-neutral-800 cashier-focus" defaultValue={money(paidAmount)} />
              <span className="shrink-0 text-xs font-medium text-neutral-400">so'm</span>
            </div>
          </div>

          <div className="min-w-0 rounded-xl bg-danger-50 p-3">
            <div className="text-xs font-bold uppercase tracking-wide text-neutral-400">Qaytim</div>
            <div className={['mt-1 truncate text-xl font-bold', change < 0 ? 'text-danger-600' : 'text-success-600'].join(' ')}>
              {money(change)} <span className="text-xs text-neutral-400">so'm</span>
            </div>
          </div>
        </div>
      </div>

      <div className="pos-sticky-actions p-3">
        <button className="pos-action-success h-16 w-full rounded-2xl gap-2 text-xl uppercase tracking-wide">
          <Check className="size-5" />
          SOTISH
        </button>
      </div>
    </section>
  );
}

function PaymentMethodButton({ active, label, icon, onClick }: { active: boolean; label: string; icon: React.ReactNode; onClick: () => void }) {
  return (
    <button
      onClick={onClick}
      className={[
        'flex h-11 items-center justify-center gap-2 rounded-xl border text-sm font-bold transition-colors duration-fast cashier-focus',
        active ? 'border-brand-500 bg-brand-50 text-brand-600' : 'border-neutral-200 bg-white text-neutral-700 hover:bg-neutral-50'
      ].join(' ')}
    >
      <span className="[&_svg]:size-5">{icon}</span>
      {label}
    </button>
  );
}
