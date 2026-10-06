import { useState } from 'react';
import { Modal } from './Modal';
import { OwnerSelectHint } from './OwnerSelectHint';
import { useOwners } from '../hooks/useOwners';

interface AssignModalProps {
  isOpen: boolean;
  onClose: () => void;
  dealCount: number;
  isLoading: boolean;
  onConfirm: (ownerId: string | null) => void;
}

export function AssignModal({ isOpen, onClose, dealCount, isLoading, onConfirm }: AssignModalProps) {
  const [ownerId, setOwnerId] = useState('');
  const { owners, isLoading: ownersLoading, isFetching: ownersFetching, refetch } = useOwners({ fresh: true, enabled: isOpen });
  const activeOwners = owners.filter((o) => o.isActive);
  return (
    <Modal isOpen={isOpen} title={`Assign ${dealCount} deal${dealCount === 1 ? '' : 's'}`} onClose={onClose}>
      <div className="space-y-4">
        <div>
          <label htmlFor="assign-owner" className="block text-sm font-medium text-gray-700 mb-1">New owner</label>
          <select
            id="assign-owner"
            value={ownerId}
            onChange={(e) => setOwnerId(e.target.value)}
            className="block w-full px-3 py-2 border border-gray-300 rounded-md text-sm focus:outline-none focus:ring-2 focus:ring-primary-500"
          >
            <option value="">Unassigned</option>
            {activeOwners.map((o) => (
              <option key={o.id} value={o.id}>{o.displayName}</option>
            ))}
          </select>
          <OwnerSelectHint
            ownerCount={activeOwners.length}
            isLoading={ownersLoading}
            isFetching={ownersFetching}
            onRefresh={() => void refetch()}
          />
        </div>
        <div className="flex justify-end gap-3">
          <button type="button" onClick={onClose} disabled={isLoading} className="px-4 py-2 text-sm font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 disabled:opacity-50">Cancel</button>
          <button
            type="button"
            onClick={() => onConfirm(ownerId || null)}
            disabled={isLoading}
            className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700 disabled:opacity-50 flex items-center gap-2"
          >
            {isLoading && <span className="animate-spin h-3 w-3 border-2 border-white/30 border-t-white rounded-full" />}
            Assign
          </button>
        </div>
      </div>
    </Modal>
  );
}
