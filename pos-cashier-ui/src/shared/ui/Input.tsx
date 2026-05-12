import { forwardRef, memo, useId } from 'react';
import { cn } from '../lib/cn';

export type InputProps = Omit<React.InputHTMLAttributes<HTMLInputElement>, 'size'> & {
  label?: string;
  hint?: string;
  error?: string;
  leftIcon?: React.ReactNode;
  rightSlot?: React.ReactNode;
};

const InputBase = forwardRef<HTMLInputElement, InputProps>(
  ({ id, label, hint, error, leftIcon, rightSlot, className, ...props }, ref) => {
    const generatedId = useId();
    const inputId = id ?? generatedId;
    const helpId = `${inputId}-help`;

    return (
      <div className="min-w-0">
        {label && (
          <label htmlFor={inputId} className="mb-1.5 block text-xs font-bold uppercase tracking-wide text-neutral-500">
            {label}
          </label>
        )}
        <div className="relative">
          {leftIcon && <span className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-neutral-400 [&_svg]:size-5">{leftIcon}</span>}
          <input
            ref={ref}
            id={inputId}
            aria-invalid={Boolean(error)}
            aria-describedby={hint || error ? helpId : undefined}
            className={cn(
              'h-12 w-full rounded-lg border bg-white px-4 text-sm text-neutral-900 placeholder:text-neutral-400 transition-colors duration-fast focus:outline-none focus:ring-4 disabled:bg-neutral-100 disabled:text-neutral-400',
              Boolean(leftIcon) && 'pl-11',
              Boolean(rightSlot) && 'pr-12',
              error ? 'border-danger-500 focus:border-danger-500 focus:ring-danger-100' : 'border-neutral-200 focus:border-brand-500 focus:ring-brand-100',
              className
            )}
            {...props}
          />
          {rightSlot && <div className="absolute right-2 top-1/2 -translate-y-1/2">{rightSlot}</div>}
        </div>
        {(hint || error) && (
          <p id={helpId} className={cn('mt-1.5 text-xs', error ? 'text-danger-600' : 'text-neutral-500')}>
            {error ?? hint}
          </p>
        )}
      </div>
    );
  }
);

InputBase.displayName = 'Input';
export const Input = memo(InputBase);
