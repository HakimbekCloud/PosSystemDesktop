import { Package, ScanBarcode } from 'lucide-react';
import type { Product } from '../types';
import { money } from '../format';

type ProductGridProps = {
  products: Product[];
  selectedProductId: string;
  onSelectProduct: (productId: string) => void;
};

export function ProductGrid({ products, selectedProductId, onSelectProduct }: ProductGridProps) {
  return (
    <div className="pos-panel-body flex-1">
      <div className="mb-4 flex items-center justify-between gap-3">
        <div>
          <h1 className="text-lg font-bold text-neutral-900">Sotuv paneli</h1>
          <p className="text-sm text-neutral-500">Qidirish, skaner yoki tezkor tanlash orqali mahsulot qo'shing.</p>
        </div>
        <div className="hidden items-center gap-2 rounded-full border border-brand-100 bg-brand-25 px-3 py-2 text-sm font-semibold text-brand-700 desktop:flex">
          <ScanBarcode className="size-4" />
          Barcode ready
        </div>
      </div>

      {products.length === 0 ? (
        <div className="grid min-h-[320px] place-items-center rounded-xl border border-dashed border-neutral-300 bg-white">
          <div className="text-center">
            <Package className="mx-auto mb-3 size-10 text-neutral-300" />
            <div className="font-semibold text-neutral-700">Mahsulot topilmadi</div>
            <div className="mt-1 text-sm text-neutral-500">Barkodni skanerlang yoki qidiruvni o'zgartiring.</div>
          </div>
        </div>
      ) : (
        <div className="grid justify-start gap-4 [grid-template-columns:repeat(auto-fill,minmax(164px,190px))]">
          {products.map((product) => (
            <button
              key={product.id}
              onClick={() => onSelectProduct(product.id)}
              className={[
                'pos-card min-h-[184px] w-full p-4 text-left cashier-focus',
                selectedProductId === product.id ? 'pos-card-selected' : ''
              ].join(' ')}
            >
              <div className="mb-4 flex items-start justify-between gap-3">
                <div className="grid size-14 place-items-center rounded-2xl bg-neutral-100 text-neutral-700">
                  <Package className="size-7" />
                </div>
                {product.hot && (
                  <span className="rounded-full bg-warning-50 px-2 py-1 text-xs font-bold text-warning-600">Tez</span>
                )}
              </div>
              <div className="line-clamp-2 min-h-10 text-sm font-bold text-neutral-900">{product.name}</div>
              <div className="mt-2 text-base font-bold text-brand-600">{money(product.price)} so'm</div>
              <div className="mt-2 text-xs text-neutral-500">
                Stok: <span className="font-semibold text-neutral-700">{money(product.stock)}</span> {product.unit}
              </div>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}
