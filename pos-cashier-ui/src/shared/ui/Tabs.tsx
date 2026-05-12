import { memo } from 'react';
import { cn } from '../lib/cn';

export type TabItem = {
  value: string;
  label: string;
};

export type TabsProps = {
  items: TabItem[];
  value: string;
  onValueChange: (value: string) => void;
  ariaLabel: string;
};

function TabsBase({ items, value, onValueChange, ariaLabel }: TabsProps) {
  return (
    <div role="tablist" aria-label={ariaLabel} className="flex items-center gap-2 overflow-x-auto">
      {items.map((item) => {
        const active = item.value === value;
        return (
          <button
            key={item.value}
            role="tab"
            aria-selected={active}
            tabIndex={active ? 0 : -1}
            onClick={() => onValueChange(item.value)}
            className={cn(
              'h-10 whitespace-nowrap rounded-full border px-4 text-sm font-semibold transition-colors duration-fast focus:outline-none focus:ring-4 focus:ring-brand-100',
              active
                ? 'border-brand-500 bg-brand-500 text-white'
                : 'border-neutral-200 bg-white text-neutral-600 hover:border-brand-200 hover:bg-brand-50 hover:text-brand-600'
            )}
          >
            {item.label}
          </button>
        );
      })}
    </div>
  );
}

export const Tabs = memo(TabsBase);
