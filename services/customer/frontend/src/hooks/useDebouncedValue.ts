import { useEffect, useState } from 'react';

/**
 * Debounces a changing value — used by the quick-search boxes (LST-4) and the
 * tag suggestion lookup (TAG-3) so typing does not fire a request per keystroke.
 */
export function useDebouncedValue<T>(value: T, delayMs = 300): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delayMs);
    return () => window.clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
