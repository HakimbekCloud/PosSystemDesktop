export function ShortcutBar() {
  const shortcuts = [
    ['/', 'Qidirish'],
    ['Enter', "Qo'shish"],
    ['←/→', 'Soni'],
    ['F2', "To'lov"],
    ['F9', 'Sotish'],
    ['Esc', 'Yopish']
  ];

  return (
    <div className="pointer-events-none fixed bottom-3 left-1/2 z-50 hidden -translate-x-1/2 rounded-full border border-neutral-200 bg-white/90 px-4 py-2 shadow-md backdrop-blur desktop:flex">
      <div className="flex items-center gap-3 text-xs text-neutral-500">
        {shortcuts.map(([key, label]) => (
          <span key={key} className="flex items-center gap-1.5">
            <kbd className="rounded-md border border-neutral-200 bg-neutral-50 px-1.5 py-0.5 font-mono text-[11px] font-bold text-neutral-700">{key}</kbd>
            {label}
          </span>
        ))}
      </div>
    </div>
  );
}
