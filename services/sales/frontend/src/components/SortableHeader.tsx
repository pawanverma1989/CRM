import type { SortDirection } from '../api/deals';

interface SortableHeaderProps<TField extends string> {
  field: TField;
  label: string;
  activeField: TField;
  direction: SortDirection;
  onSort: (field: TField) => void;
  className?: string;
}

export function SortableHeader<TField extends string>({
  field,
  label,
  activeField,
  direction,
  onSort,
  className = '',
}: SortableHeaderProps<TField>) {
  const isActive = activeField === field;
  const ariaSort = isActive ? (direction === 'asc' ? 'ascending' : 'descending') : 'none';

  return (
    <th
      scope="col"
      aria-sort={ariaSort}
      className={`px-4 sm:px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase tracking-wider ${className}`}
    >
      <button
        type="button"
        onClick={() => onSort(field)}
        className="inline-flex items-center gap-1 uppercase tracking-wider hover:text-gray-800 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500 rounded"
      >
        {label}
        <span aria-hidden="true" className={isActive ? 'text-primary-600' : 'text-gray-300'}>
          {isActive ? (direction === 'asc' ? '▲' : '▼') : '↕'}
        </span>
      </button>
    </th>
  );
}
