import { forwardRef, memo, useId } from 'react';
import { ChevronDown } from 'lucide-react';
import { cn } from '../lib/cn';

export type SelectOption = {
  value: string;
  label: string;
};

export type SelectProps = Omit<React.SelectHTMLAttributes<HTMLSelectElement>, 'children'> & {
  label?: string;
  options: SelectOption[];
  placeholder?: string;
};

const SelectBase = forwardRef<HTMLSelectElement, SelectProps>(({ id, label, options, placeholder, className, ...props }, ref) => {
  const generatedId = useId();
  const selectId = id ?? generatedId;

  return (
    <div className="min-w-0">
      {label && (
        <label htmlFor={selectId} className="mb-1.5 block text-xs font-bold uppercase tracking-wide text-neutral-500">
          {label}
        </label>
      )}
      <div className="relative">
        <select
          ref={ref}
          id={selectId}
          className={cn(
            'h-12 w-full appearance-none rounded-lg border border-neutral-200 bg-white px-4 pr-10 text-sm font-medium text-neutral-900 transition-colors duration-fast focus:border-brand-500 focus:outline-none focus:ring-4 focus:ring-brand-100 disabled:bg-neutral-100 disabled:text-neutral-400',
            className
          )}
          {...props}
        >
          {placeholder && <option value="">{placeholder}</option>}
          {options.map((option) => (
            <option key={option.value} value={option.value}>
              {option.label}
            </option>
          ))}
        </select>
        <ChevronDown className="pointer-events-none absolute right-3 top-1/2 size-4 -translate-y-1/2 text-neutral-400" />
      </div>
    </div>
  );
});

SelectBase.displayName = 'Select';
export const Select = memo(SelectBase);
