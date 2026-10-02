import { DEFAULT_PAGE_SIZE, MAX_PAGE_SIZE, PAGE_SIZE_OPTIONS } from '../lib/validation';

interface PaginationProps {
  page: number;
  pageSize: number;
  total: number;
  isFetching?: boolean;
  onPageChange: (page: number) => void;
  onPageSizeChange: (pageSize: number) => void;
  /** Plural noun for the counter, e.g. "leads". */
  noun: string;
}

/** 50 rows per page by default, up to a maximum of 200. */
export function Pagination({
  page,
  pageSize,
  total,
  isFetching = false,
  onPageChange,
  onPageSizeChange,
  noun,
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const firstRow = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const lastRow = Math.min(page * pageSize, total);

  return (
    <div className="px-4 sm:px-6 py-3 border-t border-gray-200 flex flex-wrap items-center justify-between gap-3">
      <div className="flex items-center gap-3">
        <p className="text-sm text-gray-600" aria-live="polite">
          {isFetching
            ? 'Loading…'
            : `${firstRow}–${lastRow} of ${total} ${noun}`}
        </p>
        <label className="text-sm text-gray-600 flex items-center gap-1.5">
          <span className="hidden sm:inline">Rows</span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            className="px-2 py-1 border border-gray-300 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            aria-label={`Rows per page (default ${DEFAULT_PAGE_SIZE}, maximum ${MAX_PAGE_SIZE})`}
          >
            {PAGE_SIZE_OPTIONS.map((size) => (
              <option key={size} value={size}>
                {size}
              </option>
            ))}
          </select>
        </label>
      </div>
      <div className="flex items-center gap-2">
        <button
          type="button"
          onClick={() => onPageChange(page - 1)}
          disabled={page <= 1}
          className="px-3 py-1.5 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-40 disabled:hover:bg-white"
        >
          Previous
        </button>
        <span className="text-sm text-gray-600">
          Page {page} of {totalPages}
        </span>
        <button
          type="button"
          onClick={() => onPageChange(page + 1)}
          disabled={page >= totalPages}
          className="px-3 py-1.5 text-sm text-primary-700 border border-primary-300 rounded-md hover:bg-primary-50 disabled:opacity-40 disabled:hover:bg-white"
        >
          Next
        </button>
      </div>
    </div>
  );
}
