import { memo } from 'react';
import { Check } from 'lucide-react';
import { Button } from './Button';
import { Dialog, type DialogProps } from './Dialog';

export type PaymentModalProps = Omit<DialogProps, 'title' | 'children'> & {
  total: string;
  change: string;
};

function PaymentModalBase({ total, change, footer, ...props }: PaymentModalProps) {
  return (
    <Dialog
      title="To'lovni tasdiqlash"
      description="Summa va qaytimni tekshirib, sotishni yakunlang."
      footer={
        footer ?? (
          <Button variant="success" size="lg" fullWidth leftIcon={<Check />}>
            SOTISH
          </Button>
        )
      }
      {...props}
    >
      <div className="space-y-3">
        <div className="flex items-center justify-between rounded-xl border border-neutral-200 bg-neutral-50 p-4">
          <span className="text-sm font-medium text-neutral-500">Jami</span>
          <span className="text-2xl font-bold text-brand-600">{total}</span>
        </div>
        <div className="flex items-center justify-between rounded-xl border border-success-100 bg-success-50 p-4">
          <span className="text-sm font-medium text-success-700">Qaytim</span>
          <span className="text-xl font-bold text-success-700">{change}</span>
        </div>
      </div>
    </Dialog>
  );
}

export const PaymentModal = memo(PaymentModalBase);
