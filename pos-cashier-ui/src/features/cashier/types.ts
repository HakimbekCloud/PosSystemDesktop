export type Product = {
  id: string;
  name: string;
  price: number;
  stock: number;
  unit: string;
  category: string;
  barcode: string;
  hot?: boolean;
};

export type CartLine = {
  id: string;
  productName: string;
  unitPrice: number;
  quantity: number;
};

export type PaymentMethod = 'cash' | 'card';
