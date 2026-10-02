import { MAX_BULK_RECORDS } from '../lib/validation';

interface BulkActionBarProps {
  selectedCount: number;
  canAssign: boolean;
  onClear: () => void;
  onDelete: () => void;
  onAssign: () => void;
}

export function BulkActionBar({ selectedCount, canAssign, onClear, onDelete, onAssign }: BulkActionBarProps) {
  if (selectedCount === 0) return null;
  const overCap = selectedCount > MAX_BULK_RECORDS;
  return (
    <div className="mb-4 rounded-lg border border-primary-200 bg-primary-50 px-4 py-3 flex flex-wrap items-center gap-3" role="region" aria-label="Bulk actions">
      <p className="text-sm font-medium text-primary-900" aria-live="polite">{selectedCount} lead{selectedCount === 1 ? '' : 's'} selected</p>
      {overCap ? (
        <p className="text-sm text-red-700 font-medium" role="alert">A bulk action covers at most {MAX_BULK_RECORDS} records. Clear {selectedCount - MAX_BULK_RECORDS} to continue.</p>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          {canAssign && (
            <button type="button" onClick={onAssign} className="px-3 py-1.5 text-sm font-medium text-primary-800 bg-white border border-primary-300 rounded-md hover:bg-primary-100">Assign owner</button>
          )}
          <button type="button" onClick={onDelete} className="px-3 py-1.5 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-md hover:bg-red-50">Delete</button>
        </div>
      )}
      <button type="button" onClick={onClear} className="ml-auto text-sm text-primary-700 underline hover:text-primary-900">Clear selection</button>
    </div>
  );
}
