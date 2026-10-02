import { useEffect, useState } from 'react';
import { Modal } from './Modal';
import { SelectField } from './SelectField';
import type { OwnerDto } from '../types';

interface ReassignModalProps {
  isOpen: boolean;
  onClose: () => void;
  owners: OwnerDto[];
  currentOwnerId?: string | null;
  recordCount?: number;
  isLoading?: boolean;
  onConfirm: (newOwnerId: string | null) => void;
}

/** OWN-2: an admin or manager changes the owner of one record, or several at once. */
export function ReassignModal({
  isOpen,
  onClose,
  owners,
  currentOwnerId,
  recordCount,
  isLoading,
  onConfirm,
}: ReassignModalProps) {
  const [ownerId, setOwnerId] = useState(currentOwnerId ?? '');

  useEffect(() => {
    if (isOpen) setOwnerId(currentOwnerId ?? '');
  }, [isOpen, currentOwnerId]);

  if (!isOpen) return null;

  return (
    <Modal isOpen={isOpen} title="Reassign owner" onClose={onClose}>
      <div className="space-y-4">
        <p className="text-sm text-gray-600">
          {recordCount && recordCount > 1
            ? `Choose a new owner for the ${recordCount} selected records.`
            : 'Choose a new owner for this record.'}
        </p>
        <SelectField
          label="New owner"
          value={ownerId}
          placeholder="No owner (visible to everyone)"
          options={owners
            .filter((o) => o.isActive)
            .map((o) => ({ value: o.id, label: o.displayName }))}
          onChange={(e) => setOwnerId(e.target.value)}
        />
        <div className="flex justify-end gap-3 pt-2">
          <button
            type="button"
            onClick={onClose}
            className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Cancel
          </button>
          <button
            type="button"
            disabled={isLoading}
            onClick={() => onConfirm(ownerId || null)}
            className="flex items-center gap-2 px-4 py-2 bg-primary-600 text-white text-sm font-medium rounded-md hover:bg-primary-700 disabled:opacity-60"
          >
            {isLoading && (
              <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
            )}
            Reassign
          </button>
        </div>
      </div>
    </Modal>
  );
}
