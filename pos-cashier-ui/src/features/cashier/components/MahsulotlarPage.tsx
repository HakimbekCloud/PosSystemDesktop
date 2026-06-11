import { useMemo, useState } from 'react';
import {
  AlertTriangle,
  Download,
  Eye,
  Grid3X3,
  List,
  Package,
  Pencil,
  Plus,
  Printer,
  RotateCcw,
  Search,
  Trash2,
  Upload,
  Warehouse,
  XCircle,
} from 'lucide-react';
import { money } from '../format';

type AdminProduct = {
  id: string;
  name: string;
  code: string;
  barcode: string;
  cat: string;
  price: number;
  cost: number;
  stock: number;
  unit: string;
  status: 'active' | 'low' | 'out';
};

const PRODUCTS: AdminProduct[] = [
  { id: '1',  name: 'Coca-Cola 0.5L',   code: 'CC001', barcode: '4870204999999', cat: 'Ichimliklar',      price: 8000,  cost: 5500,  stock: 120, unit: 'dona', status: 'active' },
  { id: '2',  name: 'Sprite 0.5L',      code: 'SP001', barcode: '4870204888888', cat: 'Ichimliklar',      price: 7500,  cost: 5000,  stock: 85,  unit: 'dona', status: 'active' },
  { id: '3',  name: 'Pepsi 0.5L',       code: 'PP001', barcode: '4870204777777', cat: 'Ichimliklar',      price: 7000,  cost: 4800,  stock: 3,   unit: 'dona', status: 'low'    },
  { id: '4',  name: 'Aqua 1.5L',        code: 'AQ001', barcode: '4870204666666', cat: 'Ichimliklar',      price: 5000,  cost: 3200,  stock: 200, unit: 'dona', status: 'active' },
  { id: '5',  name: 'Non (katta)',       code: 'BR001', barcode: '4870204555555', cat: 'Non mahsulotlari', price: 4000,  cost: 2800,  stock: 50,  unit: 'dona', status: 'active' },
  { id: '6',  name: 'Tort "Medovik"',   code: 'CK001', barcode: '4870204444444', cat: 'Non mahsulotlari', price: 45000, cost: 32000, stock: 8,   unit: 'dona', status: 'low'    },
  { id: '7',  name: 'Sut 1L',           code: 'ML001', barcode: '4870204333333', cat: 'Sut mahsulotlari', price: 12000, cost: 9000,  stock: 40,  unit: 'litr', status: 'active' },
  { id: '8',  name: 'Qatiq 500g',       code: 'YG001', barcode: '4870204222222', cat: 'Sut mahsulotlari', price: 9000,  cost: 6500,  stock: 6,   unit: 'dona', status: 'low'    },
  { id: '9',  name: 'Tuxum (10 dona)',  code: 'EG001', barcode: '4870204111111', cat: 'Tuxum',            price: 22000, cost: 18000, stock: 0,   unit: 'quti', status: 'out'    },
  { id: '10', name: 'Guruch 1kg',       code: 'RC001', barcode: '4870204000000', cat: 'Don mahsulotlari', price: 15000, cost: 11000, stock: 80,  unit: 'kg',   status: 'active' },
  { id: '11', name: 'Un 2kg',           code: 'FL001', barcode: '4870203999999', cat: 'Don mahsulotlari', price: 18000, cost: 14000, stock: 60,  unit: 'kg',   status: 'active' },
  { id: '12', name: 'Shakar 1kg',       code: 'SG001', barcode: '4870203888888', cat: 'Don mahsulotlari', price: 14000, cost: 10500, stock: 100, unit: 'kg',   status: 'active' },
  { id: '13', name: "O'simlik yogi 1L", code: 'OL001', barcode: '4870203777777', cat: 'Moy',              price: 25000, cost: 19000, stock: 45,  unit: 'litr', status: 'active' },
  { id: '14', name: 'Makaron 400g',     code: 'PS001', barcode: '4870203666666', cat: 'Don mahsulotlari', price: 8500,  cost: 6200,  stock: 70,  unit: 'dona', status: 'active' },
  { id: '15', name: 'Pomidor 1kg',      code: 'TM001', barcode: '4870203555555', cat: 'Sabzavotlar',      price: 12000, cost: 8000,  stock: 0,   unit: 'kg',   status: 'out'    },
  { id: '16', name: 'Kartoshka 1kg',    code: 'PT001', barcode: '4870203444444', cat: 'Sabzavotlar',      price: 6000,  cost: 4000,  stock: 150, unit: 'kg',   status: 'active' },
  { id: '17', name: 'Piyoz 1kg',        code: 'ON001', barcode: '4870203333333', cat: 'Sabzavotlar',      price: 4500,  cost: 3000,  stock: 90,  unit: 'kg',   status: 'active' },
  { id: '18', name: 'Olma 1kg',         code: 'AP001', barcode: '4870203222222', cat: 'Mevalar',          price: 18000, cost: 13000, stock: 40,  unit: 'kg',   status: 'active' },
];

const ALL_CATS = ['Barchasi', ...Array.from(new Set(PRODUCTS.map(p => p.cat))).sort()];

export function MahsulotlarPage() {
  const [query, setQuery]             = useState('');
  const [cat, setCat]                 = useState('Barchasi');
  const [statusFilter, setStatusFilter] = useState('Barchasi');
  const [view, setView]               = useState<'list' | 'grid'>('list');

  const filtered = useMemo(() => {
    const q = query.trim().toLowerCase();
    return PRODUCTS.filter(p => {
      if (cat !== 'Barchasi' && p.cat !== cat) return false;
      if (statusFilter === 'Faol' && p.status !== 'active') return false;
      if (statusFilter === 'Kam qoldiq' && p.status !== 'low') return false;
      if (statusFilter === 'Tugagan' && p.status !== 'out') return false;
      if (q && !p.name.toLowerCase().includes(q) && !p.code.toLowerCase().includes(q) && !p.barcode.includes(q)) return false;
      return true;
    });
  }, [query, cat, statusFilter]);

  const totalValue = PRODUCTS.reduce((s, p) => s + p.stock * p.cost, 0);
  const lowCount   = PRODUCTS.filter(p => p.status === 'low').length;
  const outCount   = PRODUCTS.filter(p => p.status === 'out').length;

  return (
    <div className="adm-page">

      {/* ── PAGE HEADER ── */}
      <div className="adm-page-head">
        <div>
          <h1 className="text-2xl font-bold text-neutral-900">Mahsulotlar</h1>
          <p className="mt-1 text-sm text-neutral-500">
            Katalogda{' '}
            <span className="font-semibold text-neutral-800">{PRODUCTS.length}</span> ta nomenklatura ·{' '}
            <span className="font-semibold text-brand-600">{filtered.length}</span> ta ko'rsatilmoqda
          </p>
        </div>
        <div className="flex items-center gap-2">
          <button className="adm-btn-secondary">
            <Upload className="size-4" />Import
          </button>
          <button className="adm-btn-secondary">
            <Download className="size-4" />Eksport
          </button>
          <button className="adm-btn-secondary" title="Yangilash" style={{ width: 40, padding: 0, justifyContent: 'center' }}>
            <RotateCcw className="size-4" />
          </button>
          <button className="adm-btn-primary">
            <Plus className="size-4" />Yangi mahsulot
          </button>
        </div>
      </div>

      {/* ── PAGE BODY ── */}
      <div className="adm-page-body">

        {/* Stats */}
        <div className="adm-stat-grid">
          <StatCard label="JAMI MAHSULOTLAR" value={PRODUCTS.length} unit="ta"
            icon={<Package className="size-5 text-brand-600" />} iconBg="bg-brand-50" />
          <StatCard label="OMBOR QIYMATI" value={money(totalValue) + " so'm"}
            icon={<Warehouse className="size-5 text-blue-500" />} iconBg="bg-blue-50" text />
          <StatCard label="KAM QOLDIQ" value={lowCount} unit="ta"
            icon={<AlertTriangle className="size-5 text-yellow-600" />} iconBg="bg-yellow-50" />
          <StatCard label="TUGAGAN" value={outCount} unit="ta"
            icon={<XCircle className="size-5 text-red-600" />} iconBg="bg-red-50" />
        </div>

        {/* Filter row */}
        <div className="flex items-center gap-3 mb-4">
          <div className="relative flex-1">
            <Search className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 size-4 text-neutral-400" />
            <input
              value={query}
              onChange={e => setQuery(e.target.value)}
              className="adm-search"
              placeholder="Nomi, kodi yoki barkod bo'yicha qidirish..."
            />
          </div>
          <select value={statusFilter} onChange={e => setStatusFilter(e.target.value)} className="adm-select">
            <option>Barchasi</option>
            <option>Faol</option>
            <option>Kam qoldiq</option>
            <option>Tugagan</option>
          </select>
          <div className="flex overflow-hidden rounded-lg border border-neutral-200 bg-white">
            <button
              onClick={() => setView('list')}
              className={`flex size-10 items-center justify-center transition-colors duration-fast focus:outline-none ${view === 'list' ? 'bg-brand-50 text-brand-700' : 'text-neutral-400 hover:bg-neutral-50'}`}
              title="Ro'yxat"
            >
              <List className="size-4" />
            </button>
            <button
              onClick={() => setView('grid')}
              className={`flex size-10 items-center justify-center transition-colors duration-fast focus:outline-none ${view === 'grid' ? 'bg-brand-50 text-brand-700' : 'text-neutral-400 hover:bg-neutral-50'}`}
              title="Karta"
            >
              <Grid3X3 className="size-4" />
            </button>
          </div>
        </div>

        {/* Category chips */}
        <div className="mb-5 flex flex-wrap gap-2">
          {ALL_CATS.map(c => (
            <button key={c} onClick={() => setCat(c)} className={`adm-chip ${cat === c ? 'adm-chip-active' : ''}`}>
              {c}
              {c !== 'Barchasi' && (
                <span className="opacity-60">· {PRODUCTS.filter(p => p.cat === c).length}</span>
              )}
            </button>
          ))}
        </div>

        {/* Empty state */}
        {filtered.length === 0 && (
          <div className="flex flex-col items-center justify-center py-24 text-neutral-400">
            <Package className="mb-3 size-12 opacity-30" />
            <p className="text-sm">Hech narsa topilmadi</p>
          </div>
        )}

        {/* List view */}
        {filtered.length > 0 && view === 'list' && (
          <div className="adm-table-shell">
            <table className="adm-table">
              <thead>
                <tr>
                  <th>Mahsulot</th>
                  <th>Kategoriya</th>
                  <th style={{ textAlign: 'right' }}>Sotish narxi</th>
                  <th style={{ textAlign: 'right' }}>Tannarx</th>
                  <th style={{ textAlign: 'right' }}>Qoldiq</th>
                  <th>Holati</th>
                  <th style={{ width: 116 }}></th>
                </tr>
              </thead>
              <tbody>
                {filtered.map(p => (
                  <tr key={p.id}>
                    <td>
                      <div className="flex items-center gap-3">
                        <div className="flex size-9 shrink-0 items-center justify-center rounded-lg bg-neutral-100 text-neutral-500">
                          <Package className="size-4" />
                        </div>
                        <div>
                          <div className="font-semibold text-neutral-900">{p.name}</div>
                          <div className="font-mono text-xs text-neutral-400">{p.code}</div>
                        </div>
                      </div>
                    </td>
                    <td className="text-neutral-500">{p.cat}</td>
                    <td className="text-right font-semibold text-brand-600">{money(p.price)}</td>
                    <td className="text-right font-mono text-sm text-neutral-400">{money(p.cost)}</td>
                    <td className="text-right">
                      <span className={`font-bold ${p.status === 'out' ? 'text-red-600' : p.status === 'low' ? 'text-yellow-600' : 'text-neutral-900'}`}>
                        {money(p.stock)}
                      </span>{' '}
                      <span className="text-neutral-400">{p.unit}</span>
                    </td>
                    <td><StatusBadge status={p.status} /></td>
                    <td>
                      <div className="flex items-center justify-end gap-0.5">
                        <button className="adm-row-action" title="Ko'rish"><Eye className="size-4" /></button>
                        <button className="adm-row-action" title="Tahrirlash"><Pencil className="size-4" /></button>
                        <button className="adm-row-action" title="Chop etish"><Printer className="size-4" /></button>
                        <button className="adm-row-action-danger" title="O'chirish"><Trash2 className="size-4" /></button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}

        {/* Grid view */}
        {filtered.length > 0 && view === 'grid' && (
          <div className="grid gap-4" style={{ gridTemplateColumns: 'repeat(auto-fill, minmax(218px, 1fr))' }}>
            {filtered.map(p => (
              <div key={p.id} className="rounded-xl border border-neutral-200 bg-white p-4 shadow-xs transition-all duration-fast hover:border-brand-300 hover:shadow-sm">
                <div className="mb-3 flex items-start justify-between">
                  <div className="flex size-14 items-center justify-center rounded-xl bg-neutral-100 text-neutral-500">
                    <Package className="size-6" />
                  </div>
                  <StatusBadge status={p.status} />
                </div>
                <div className="min-h-9 font-bold leading-tight text-neutral-900">{p.name}</div>
                <div className="mt-1 font-mono text-xs text-neutral-400">{p.code}</div>
                <div className="mt-3 text-base font-bold text-brand-600">{money(p.price)} so'm</div>
                <div className="mt-1 text-xs text-neutral-500">
                  Qoldiq:{' '}
                  <span className={`font-semibold ${p.status === 'out' ? 'text-red-600' : p.status === 'low' ? 'text-yellow-600' : 'text-neutral-800'}`}>
                    {money(p.stock)} {p.unit}
                  </span>
                </div>
              </div>
            ))}
          </div>
        )}

      </div>
    </div>
  );
}

function StatCard({
  label, value, unit, icon, iconBg, text = false,
}: {
  label: string;
  value: number | string;
  unit?: string;
  icon: React.ReactNode;
  iconBg: string;
  text?: boolean;
}) {
  return (
    <div className="relative overflow-hidden rounded-xl border border-neutral-200 bg-white p-5 shadow-xs">
      <div className="text-xs font-semibold uppercase tracking-wide text-neutral-500">{label}</div>
      {text ? (
        <div className="mt-3 text-lg font-bold text-neutral-900 pr-14">{value}</div>
      ) : (
        <div className="mt-3 flex items-baseline gap-1">
          <span className="text-3xl font-bold text-neutral-900">{value}</span>
          {unit && <span className="text-sm text-neutral-400">{unit}</span>}
        </div>
      )}
      <div className={`absolute right-5 top-5 flex size-11 items-center justify-center rounded-xl ${iconBg}`}>
        {icon}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: 'active' | 'low' | 'out' }) {
  if (status === 'active') return <span className="adm-badge adm-badge-success">Faol</span>;
  if (status === 'low')    return <span className="adm-badge adm-badge-warning">Kam qoldiq</span>;
  return                          <span className="adm-badge adm-badge-danger">Tugagan</span>;
}
