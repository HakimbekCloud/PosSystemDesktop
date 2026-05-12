import { memo } from 'react';
import { cn } from '../lib/cn';

type Trend = 'up' | 'down' | 'flat';

export type StatCardProps = {
  label: string;
  value: string;
  hint?: string;
  trend?: Trend;
  icon?: React.ReactNode;
};

const trendClass: Record<Trend, string> = {
  up: 'text-success-600',
  down: 'text-danger-600',
  flat: 'text-neutral-500'
};

function StatCardBase({ label, value, hint, trend = 'flat', icon }: StatCardProps) {
  return (
    <article className="rounded-xl border border-neutral-200 bg-white p-4 shadow-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="text-xs font-bold uppercase tracking-wide text-neutral-500">{label}</div>
          <div className="mt-2 truncate text-2xl font-bold text-neutral-900">{value}</div>
        </div>
        {icon && <div className="grid size-10 place-items-center rounded-xl bg-brand-50 text-brand-600 [&_svg]:size-5">{icon}</div>}
      </div>
      {hint && <div className={cn('mt-3 text-sm font-medium', trendClass[trend])}>{hint}</div>}
    </article>
  );
}

export const StatCard = memo(StatCardBase);
