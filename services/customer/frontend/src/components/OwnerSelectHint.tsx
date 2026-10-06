interface OwnerSelectHintProps {
  /** Number of selectable (active) owners currently loaded. */
  ownerCount: number;
  isLoading: boolean;
  isFetching: boolean;
  onRefresh: () => void;
}

/** Helper text under an owner select: new users arrive via events, so offer a manual refresh. */
export function OwnerSelectHint({ ownerCount, isLoading, isFetching, onRefresh }: OwnerSelectHintProps) {
  const isEmpty = !isLoading && ownerCount === 0;
  return (
    <p className="mt-1 text-xs text-gray-500">
      {isEmpty
        ? 'No users available yet. If users were just created, click Refresh.'
        : "Don't see a new user? New users appear here within a few seconds."}{' '}
      <button
        type="button"
        onClick={onRefresh}
        disabled={isFetching}
        className="font-medium text-primary-600 hover:text-primary-700 hover:underline focus:outline-none focus:ring-2 focus:ring-primary-500 rounded disabled:opacity-60 disabled:no-underline"
      >
        {isFetching ? 'Refreshing…' : 'Refresh'}
      </button>
      <span role="status" aria-live="polite" className="sr-only">
        {isFetching ? 'Refreshing users' : ''}
      </span>
    </p>
  );
}
