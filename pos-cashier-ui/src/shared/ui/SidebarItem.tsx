import { memo } from 'react';
import { cn } from '../lib/cn';

export type SidebarItemProps = React.ButtonHTMLAttributes<HTMLButtonElement> & {
  icon: React.ReactNode;
  label: string;
  active?: boolean;
  collapsed?: boolean;
};

function SidebarItemBase({ icon, label, active, collapsed, className, ...props }: SidebarItemProps) {
  return (
    <button
      type="button"
      aria-current={active ? 'page' : undefined}
      title={collapsed ? label : undefined}
      className={cn('pos-nav-item', active && 'pos-nav-item-active', collapsed && 'justify-center px-0', className)}
      {...props}
    >
      <span className="shrink-0 [&_svg]:size-5">{icon}</span>
      {!collapsed && <span className="truncate text-left">{label}</span>}
    </button>
  );
}

export const SidebarItem = memo(SidebarItemBase);
