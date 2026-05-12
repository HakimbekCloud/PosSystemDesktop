import { memo } from 'react';
import { cn } from '../lib/cn';

type BadgeTone = 'neutral' | 'brand' | 'success' | 'warning' | 'danger';

export type BadgeProps = React.HTMLAttributes<HTMLSpanElement> & {
  tone?: BadgeTone;
  dot?: boolean;
};

const tones: Record<BadgeTone, string> = {
  neutral: 'bg-neutral-100 text-neutral-700',
  brand: 'bg-brand-50 text-brand-700',
  success: 'bg-success-50 text-success-700',
  warning: 'bg-warning-50 text-warning-600',
  danger: 'bg-danger-50 text-danger-700'
};

function BadgeBase({ tone = 'neutral', dot, className, children, ...props }: BadgeProps) {
  return (
    <span className={cn('inline-flex h-7 items-center gap-1.5 rounded-full px-2.5 text-xs font-bold', tones[tone], className)} {...props}>
      {dot && <span className="size-2 rounded-full bg-current" />}
      {children}
    </span>
  );
}

export const Badge = memo(BadgeBase);
