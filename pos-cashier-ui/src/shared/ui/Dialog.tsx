import { memo, useEffect } from 'react';
import { X } from 'lucide-react';
import { Button } from './Button';

export type DialogProps = {
  open: boolean;
  title: string;
  description?: string;
  onClose: () => void;
  children: React.ReactNode;
  footer?: React.ReactNode;
};

function DialogBase({ open, title, description, onClose, children, footer }: DialogProps) {
  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onClose();
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [onClose, open]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-[80] grid place-items-center bg-neutral-950/40 p-4 backdrop-blur-sm" role="presentation">
      <section role="dialog" aria-modal="true" aria-labelledby="dialog-title" className="w-full max-w-lg overflow-hidden rounded-2xl bg-white shadow-lg">
        <header className="flex items-start gap-4 border-b border-neutral-200 p-5">
          <div className="min-w-0 flex-1">
            <h2 id="dialog-title" className="text-lg font-bold text-neutral-900">{title}</h2>
            {description && <p className="mt-1 text-sm text-neutral-500">{description}</p>}
          </div>
          <Button aria-label="Close dialog" variant="ghost" size="icon" onClick={onClose}>
            <X className="size-5" />
          </Button>
        </header>
        <div className="p-5">{children}</div>
        {footer && <footer className="border-t border-neutral-200 bg-neutral-50 p-4">{footer}</footer>}
      </section>
    </div>
  );
}

export const Dialog = memo(DialogBase);
