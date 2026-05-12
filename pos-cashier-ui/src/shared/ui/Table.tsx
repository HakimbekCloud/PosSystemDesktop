import { memo } from 'react';
import { cn } from '../lib/cn';

export type TableColumn<T> = {
  key: string;
  header: string;
  className?: string;
  render: (row: T) => React.ReactNode;
};

export type TableProps<T> = {
  rows: T[];
  columns: TableColumn<T>[];
  getRowKey: (row: T) => string;
  gridTemplateColumns: string;
  emptyState?: React.ReactNode;
};

function TableBase<T>({ rows, columns, getRowKey, gridTemplateColumns, emptyState }: TableProps<T>) {
  if (rows.length === 0 && emptyState) {
    return <>{emptyState}</>;
  }

  return (
    <div className="pos-table-shell">
      <div className="pos-table-header" style={{ gridTemplateColumns }}>
        {columns.map((column) => (
          <div key={column.key} className={column.className}>
            {column.header}
          </div>
        ))}
      </div>
      {rows.map((row) => (
        <div key={getRowKey(row)} className="pos-table-row gap-2" style={{ gridTemplateColumns }}>
          {columns.map((column) => (
            <div key={column.key} className={cn('min-w-0', column.className)}>
              {column.render(row)}
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

export const Table = memo(TableBase) as typeof TableBase;
