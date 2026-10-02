import { Link } from 'react-router-dom';
import type { ConflictRef, DuplicateMatchDto } from '../types';
import { detailPath } from '../lib/utils';

const reasonText: Record<DuplicateMatchDto['reason'], string> = {
  email: 'same email address',
  domain: 'same web domain',
  phone: 'same phone number',
  similar_name: 'very similar name',
};

interface DuplicateWarningProps {
  matches: DuplicateMatchDto[];
  /** DUP-2 / AC-5: the warning does not block the save. */
  onDismiss: () => void;
  onSaveAnyway: () => void;
  isSaving?: boolean;
}

export function DuplicateWarning({
  matches,
  onDismiss,
  onSaveAnyway,
  isSaving,
}: DuplicateWarningProps) {
  if (matches.length === 0) return null;

  return (
    <div
      className="rounded-md border border-yellow-300 bg-yellow-50 p-4"
      role="alert"
      aria-live="polite"
    >
      <h2 className="text-sm font-semibold text-yellow-900">
        {matches.length === 1
          ? 'This may already exist'
          : `${matches.length} records look similar`}
      </h2>
      <p className="mt-1 text-sm text-yellow-800">
        You can still save — check these first in case one of them is the same record.
      </p>
      <ul className="mt-3 space-y-1.5">
        {matches.map((match) => (
          <li key={`${match.entityType}-${match.id}-${match.reason}`} className="text-sm">
            <Link
              to={detailPath(match.entityType, match.id)}
              target="_blank"
              rel="noreferrer"
              className="font-medium text-primary-700 underline hover:text-primary-900"
            >
              {match.label}
            </Link>
            <span className="text-yellow-800">
              {' '}
              — {reasonText[match.reason]}
              {match.matchedValue ? ` (${match.matchedValue})` : ''}
            </span>
          </li>
        ))}
      </ul>
      <div className="mt-4 flex flex-wrap gap-3">
        <button
          type="button"
          onClick={onSaveAnyway}
          disabled={isSaving}
          className="flex items-center gap-2 px-4 py-2 bg-yellow-600 text-white text-sm font-medium rounded-md hover:bg-yellow-700 disabled:opacity-60"
        >
          {isSaving && (
            <span className="animate-spin h-3.5 w-3.5 border-2 border-white/30 border-t-white rounded-full" />
          )}
          Save anyway
        </button>
        <button
          type="button"
          onClick={onDismiss}
          disabled={isSaving}
          className="px-4 py-2 text-sm font-medium text-yellow-900 bg-white border border-yellow-300 rounded-md hover:bg-yellow-100 disabled:opacity-60"
        >
          Keep editing
        </button>
      </div>
    </div>
  );
}

/**
 * DUP-1 / AC-3 / AC-14: a refused save or restore, naming the record that is
 * already using the value, with a link to it.
 */
export function ConflictBanner({
  message,
  conflict,
}: {
  message: string;
  conflict?: ConflictRef;
}) {
  return (
    <div className="rounded-md border border-red-300 bg-red-50 p-4" role="alert">
      <p className="text-sm font-medium text-red-800">{message}</p>
      {conflict && (
        <p className="mt-1 text-sm text-red-800">
          Already used by{' '}
          <Link
            to={detailPath(conflict.type, conflict.id)}
            className="font-medium underline hover:text-red-900"
          >
            {conflict.label}
          </Link>
          .
        </p>
      )}
    </div>
  );
}

/** CON-6 / COM-3 / AC-6: someone else saved this record first. */
export function VersionConflictBanner({ onReload }: { onReload: () => void }) {
  return (
    <div className="rounded-md border border-red-300 bg-red-50 p-4" role="alert">
      <h2 className="text-sm font-semibold text-red-900">Someone else changed this record</h2>
      <p className="mt-1 text-sm text-red-800">
        Your copy is out of date, so nothing was saved. Reload to see the current values, then make
        your change again.
      </p>
      <button
        type="button"
        onClick={onReload}
        className="mt-3 px-4 py-2 bg-red-600 text-white text-sm font-medium rounded-md hover:bg-red-700"
      >
        Reload this record
      </button>
    </div>
  );
}
