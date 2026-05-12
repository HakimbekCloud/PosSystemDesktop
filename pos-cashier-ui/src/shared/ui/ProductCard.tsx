import { memo } from 'react';
import { Check, Package } from 'lucide-react';
import { cn } from '../lib/cn';
import { Badge } from './Badge';

export type ProductCardProps = {
  name: string;
  price: string;
  stock: string;
  selected?: boolean;
  badge?: string;
  imageUrl?: string;
  onClick?: () => void;
};

function ProductCardBase({ name, price, stock, selected, badge, imageUrl, onClick }: ProductCardProps) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-pressed={selected}
      className={cn(
        'pos-card relative min-h-[184px] p-4 text-left focus:outline-none focus:ring-4 focus:ring-brand-100',
        selected && 'pos-card-selected'
      )}
    >
      {selected && (
        <span className="absolute right-3 top-3 grid size-6 place-items-center rounded-full bg-brand-500 text-white">
          <Check className="size-4" />
        </span>
      )}
      <div className="mb-4 flex items-start justify-between gap-3">
        <div className="grid size-14 place-items-center overflow-hidden rounded-2xl bg-neutral-100 text-neutral-700">
          {imageUrl ? <img src={imageUrl} alt="" className="size-full object-cover" /> : <Package className="size-7" />}
        </div>
        {badge && <Badge tone="warning">{badge}</Badge>}
      </div>
      <div className="line-clamp-2 min-h-10 text-sm font-bold text-neutral-900">{name}</div>
      <div className="mt-2 text-base font-bold text-brand-600">{price}</div>
      <div className="mt-2 text-xs text-neutral-500">{stock}</div>
    </button>
  );
}

export const ProductCard = memo(ProductCardBase);
