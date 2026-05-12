import { Banknote, Check, CreditCard, Delete } from 'lucide-react';
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
      <div className="pos-panel-header">
        <div>
          <div className="text-xs font-bold uppercase tracking-wide text-neutral-500">To'lov</div>
          <div className="mt-1 text-sm font-semibold text-neutral-900">Kassa yakunlash</div>
        </div>
      </div>

      <div className="pos-panel-body flex-1 space-y-5">
        <div>
          <div className="mb-2 text-xs font-bold uppercase tracking-wide text-neutral-500">To'lov usuli</div>
          <div className="grid grid-cols-2 gap-2">
            <PaymentMethodButton active={method === 'cash'} label="Naqd" icon={<Banknote />} onClick={() => onMethodChange('cash')} />
            <PaymentMethodButton active={method === 'card'} label="Karta" icon={<CreditCard />} onClick={() => onMethodChange('card')} />
          </div>
        </div>

        <div>
          <label className="mb-2 block text-xs font-bold uppercase tracking-wide text-neutral-500">To'lov summasi</label>
          <div className="flex items-center gap-2">
            <input className="h-14 min-w-0 flex-1 rounded-xl border border-neutral-200 bg-white px-4 text-right text-2xl font-bold cashier-focus" defaultValue={money(paidAmount)} />
            <span className="text-sm font-medium text-neutral-500">so'm</span>
          </div>
        </div>

        <div className="rounded-xl border border-neutral-200 bg-white p-4">
          <div className="flex items-center justify-between text-sm">
            <span className="text-neutral-500">Qaytim</span>
            <span className={change < 0 ? 'font-bold text-danger-600' : 'font-bold text-success-600'}>{money(change)} so'm</span>
          </div>
        </div>

        <Keypad />
      </div>

      <div className="pos-sticky-actions">
        <button className="pos-action-success w-full gap-2">
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
        'flex h-12 items-center justify-center gap-2 rounded-xl border text-sm font-bold transition-colors duration-fast cashier-focus',
        active ? 'border-brand-500 bg-brand-50 text-brand-600' : 'border-neutral-200 bg-white text-neutral-700 hover:bg-neutral-50'
      ].join(' ')}
    >
      <span className="[&_svg]:size-5">{icon}</span>
      {label}
    </button>
  );
}

function Keypad() {
  const keys = ['7', '8', '9', '4', '5', '6', '1', '2', '3', '00', '0', 'back'];

  return (
    <div className="hidden tablet:block">
      <div className="mb-2 text-xs font-bold uppercase tracking-wide text-neutral-500">Tezkor klaviatura</div>
      <div className="grid grid-cols-3 gap-2">
        {keys.map((key) => (
          <button key={key} className="grid h-11 place-items-center rounded-xl border border-neutral-200 bg-white text-base font-bold text-neutral-800 transition-colors duration-fast hover:bg-brand-50 cashier-focus">
            {key === 'back' ? <Delete className="size-5" /> : key}
          </button>
        ))}
      </div>
    </div>
  );
}
