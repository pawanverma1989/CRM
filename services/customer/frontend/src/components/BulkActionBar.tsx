import { MAX_BULK_RECORDS } from '../lib/validation';
import type { EntityType } from '../types';

interface BulkActionBarProps {
  recordType: EntityType;
  selectedCount: number;
  canMerge: boolean;
  canReassign: boolean;
  onClear: () => void;
  onDelete: () => void;
  onReassign: () => void;
  onMerge: () => void;
}

/**
 * Bulk actions for both lists. DEL-4 and OWN-2 cap a batch at 500 records, and
 * DUP-4 merges exactly two records of one type.
 */
export function BulkActionBar({
  recordType,
  selectedCount,
  canMerge,
  canReassign,
  onClear,
  onDelete,
  onReassign,
  onMerge,
}: BulkActionBarProps) {
  if (selectedCount === 0) return null;

  const overCap = selectedCount > MAX_BULK_RECORDS;
  const noun = recordType === 'contact' ? 'contact' : 'company';

  return (
    <div
      className="mb-4 rounded-lg border border-primary-200 bg-primary-50 px-4 py-3 flex flex-wrap items-center gap-3"
      role="region"
      aria-label="Bulk actions"
    >
      <p className="text-sm font-medium text-primary-900" aria-live="polite">
        {selectedCount} {noun}
        {selectedCount === 1 ? '' : 's'} selected
      </p>

      {overCap ? (
        <p className="text-sm text-red-700 font-medium" role="alert">
          A bulk action covers at most {MAX_BULK_RECORDS} records. Clear{' '}
          {selectedCount - MAX_BULK_RECORDS} to continue.
        </p>
      ) : (
        <div className="flex flex-wrap items-center gap-2">
          {canReassign && (
            <button
              type="button"
              onClick={onReassign}
              className="px-3 py-1.5 text-sm font-medium text-primary-800 bg-white border border-primary-300 rounded-md hover:bg-primary-100"
            >
              Reassign owner
            </button>
          )}
          {canMerge && (
            <button
              type="button"
              onClick={onMerge}
              disabled={selectedCount !== 2}
              title={selectedCount !== 2 ? 'Select exactly two records to merge' : undefined}
              className="px-3 py-1.5 text-sm font-medium text-primary-800 bg-white border border-primary-300 rounded-md hover:bg-primary-100 disabled:opacity-50 disabled:hover:bg-white"
            >
              Merge
            </button>
          )}
          <button
            type="button"
            onClick={onDelete}
            className="px-3 py-1.5 text-sm font-medium text-red-700 bg-white border border-red-300 rounded-md hover:bg-red-50"
          >
            Delete
          </button>
        </div>
      )}

      <button
        type="button"
        onClick={onClear}
        className="ml-auto text-sm text-primary-700 underline hover:text-primary-900"
      >
        Clear selection
      </button>
      {canMerge && selectedCount !== 2 && !overCap && (
        <p className="w-full text-xs text-primary-800">
          Merging needs exactly two records of the same type.
        </p>
      )}
    </div>
  );
}
