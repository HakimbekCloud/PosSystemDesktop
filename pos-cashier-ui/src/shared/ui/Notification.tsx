import { memo } from 'react';
import { AlertTriangle, CheckCircle2, Info, XCircle } from 'lucide-react';
import { cn } from '../lib/cn';

type NotificationTone = 'info' | 'success' | 'warning' | 'danger';

export type NotificationProps = {
  tone?: NotificationTone;
  title: string;
  message?: string;
};

const tones: Record<NotificationTone, string> = {
  info: 'border-info-100 bg-info-50 text-info-600',
  success: 'border-success-100 bg-success-50 text-success-700',
  warning: 'border-warning-100 bg-warning-50 text-warning-600',
  danger: 'border-danger-100 bg-danger-50 text-danger-700'
};

const icons = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  danger: XCircle
};

function NotificationBase({ tone = 'info', title, message }: NotificationProps) {
  const Icon = icons[tone];
  return (
    <div role="status" className={cn('flex gap-3 rounded-xl border p-4 shadow-sm', tones[tone])}>
      <Icon className="mt-0.5 size-5 shrink-0" />
      <div>
        <div className="font-bold">{title}</div>
        {message && <div className="mt-1 text-sm opacity-85">{message}</div>}
      </div>
    </div>
  );
}

export const Notification = memo(NotificationBase);
