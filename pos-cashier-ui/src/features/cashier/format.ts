export function money(value: number) {
  return new Intl.NumberFormat('uz-UZ').format(value);
}
