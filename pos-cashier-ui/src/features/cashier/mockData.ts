import type { CartLine, Product } from './types';

export const categories = ['Barchasi', 'Ichimliklar', 'Oziq-ovqat', "Go'sht", 'Non mahsulotlari', 'Maishiy', 'Boshqa'];

export const products: Product[] = [
  { id: 'p1', name: '123333', price: 18900, stock: 1240, unit: 'm', category: 'Barchasi', barcode: '860000001', hot: true },
  { id: 'p2', name: 'Olmacha', price: 15000, stock: 530, unit: 'm', category: 'Oziq-ovqat', barcode: '860000002', hot: true },
  { id: 'p3', name: 'baklajan jan', price: 14000, stock: 46, unit: 'kg', category: 'Oziq-ovqat', barcode: '860000003' },
  { id: 'p4', name: 'barcode product', price: 15000, stock: 90, unit: 'm', category: 'Boshqa', barcode: '860000004' },
  { id: 'p5', name: 'jhgfvdcxs', price: 123, stock: 320, unit: 'pcs', category: 'Boshqa', barcode: '860000005' },
  { id: 'p6', name: 'kjhgf dsa', price: 142, stock: 123123091, unit: 'pcs', category: 'Boshqa', barcode: '860000006' },
  { id: 'p7', name: 'nimadiriladir', price: 15000, stock: 380, unit: 'pcs', category: 'Maishiy', barcode: '860000007' },
  { id: 'p8', name: 'olma', price: 14000, stock: 50, unit: 'kg', category: 'Oziq-ovqat', barcode: '860000008', hot: true }
];

export const cartLines: CartLine[] = [
  { id: 'c1', productName: 'Olmacha', unitPrice: 15000, quantity: 2 },
  { id: 'c2', productName: 'olma', unitPrice: 14000, quantity: 1.5 }
];
