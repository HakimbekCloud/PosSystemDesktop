import { forwardRef, memo } from 'react';
import { cn } from '../lib/cn';

type ButtonVariant = 'primary' | 'secondary' | 'success' | 'danger' | 'ghost';
type ButtonSize = 'sm' | 'md' | 'lg' | 'icon';

export type ButtonProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: ButtonVariant;
  size?: ButtonSize;
  leftIcon?: React.ReactNode;
  rightIcon?: React.ReactNode;
  fullWidth?: boolean;
};

const variants: Record<ButtonVariant, string> = {
  primary: 'bg-brand-500 text-white shadow-brand hover:bg-brand-600 active:bg-brand-700',
  secondary: 'border border-neutral-200 bg-white text-neutral-800 hover:bg-neutral-50 active:bg-neutral-100',
  success: 'bg-success-600 text-white shadow-md hover:bg-success-700 active:bg-success-700',
  danger: 'bg-danger-600 text-white hover:bg-danger-700 active:bg-danger-700',
  ghost: 'bg-transparent text-neutral-700 hover:bg-neutral-100 active:bg-neutral-200'
};

const sizes: Record<ButtonSize, string> = {
  sm: 'h-9 gap-2 rounded-lg px-3 text-sm',
  md: 'h-11 gap-2 rounded-xl px-4 text-sm',
  lg: 'h-14 gap-2 rounded-xl px-6 text-base',
  icon: 'size-10 rounded-xl p-0'
};

const ButtonBase = forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant = 'secondary', size = 'md', leftIcon, rightIcon, fullWidth, children, type = 'button', ...props }, ref) => (
    <button
      ref={ref}
      type={type}
      className={cn(
        'inline-flex shrink-0 items-center justify-center font-semibold transition duration-fast focus:outline-none focus:ring-4 focus:ring-brand-100 disabled:pointer-events-none disabled:opacity-50',
        variants[variant],
        sizes[size],
        fullWidth && 'w-full',
        className
      )}
      {...props}
    >
      {leftIcon && <span className="grid place-items-center [&_svg]:size-4">{leftIcon}</span>}
      {children}
      {rightIcon && <span className="grid place-items-center [&_svg]:size-4">{rightIcon}</span>}
    </button>
  )
);

ButtonBase.displayName = 'Button';
export const Button = memo(ButtonBase);
