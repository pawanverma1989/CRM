import { useEffect, useId, useRef, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getCompanies } from '../api/companies';
import { useDebouncedValue } from '../hooks/useDebouncedValue';

interface CompanyPickerProps {
  label?: string;
  value: string | null;
  /** The selected company's name, so an edit form can show it before the user types. */
  valueLabel?: string | null;
  onChange: (companyId: string | null, label: string | null) => void;
  error?: string;
  hint?: string;
  disabled?: boolean;
  required?: boolean;
}

/**
 * An accessible combobox for CON-2 ("linked to one company or to none") and the
 * contacts list's company filter (LST-2). Search is server-side (NFR-1: up to
 * 100,000 companies), so a plain <select> would not scale.
 */
export function CompanyPicker({
  label = 'Company',
  value,
  valueLabel,
  onChange,
  error,
  hint,
  disabled,
  required,
}: CompanyPickerProps) {
  const inputId = useId();
  const listId = `${inputId}-listbox`;
  const [draft, setDraft] = useState(valueLabel ?? '');
  const [isOpen, setIsOpen] = useState(false);
  const [highlighted, setHighlighted] = useState(0);
  const containerRef = useRef<HTMLDivElement>(null);
  const debounced = useDebouncedValue(draft.trim(), 250);

  useEffect(() => {
    if (!isOpen) setDraft(valueLabel ?? '');
  }, [valueLabel, isOpen]);

  const { data } = useQuery({
    queryKey: ['companies-picker', debounced],
    queryFn: () => getCompanies({ q: debounced, page: 1, pageSize: 10, sort: 'name', direction: 'asc' }),
    enabled: isOpen && debounced.length > 0,
    staleTime: 30_000,
  });

  const options = data?.data ?? [];

  useEffect(() => {
    const onClickOutside = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setIsOpen(false);
        setDraft(valueLabel ?? '');
      }
    };
    document.addEventListener('mousedown', onClickOutside);
    return () => document.removeEventListener('mousedown', onClickOutside);
  }, [valueLabel]);

  const selectCompany = (id: string, name: string) => {
    onChange(id, name);
    setDraft(name);
    setIsOpen(false);
  };

  const clearSelection = () => {
    onChange(null, null);
    setDraft('');
    setIsOpen(false);
  };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!isOpen && (e.key === 'ArrowDown' || e.key === 'ArrowUp')) {
      setIsOpen(true);
      return;
    }
    if (!isOpen) return;
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      setHighlighted((h) => Math.min(h + 1, Math.max(options.length - 1, 0)));
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      setHighlighted((h) => Math.max(h - 1, 0));
    } else if (e.key === 'Enter') {
      if (options[highlighted]) {
        e.preventDefault();
        selectCompany(options[highlighted].id, options[highlighted].name);
      }
    } else if (e.key === 'Escape') {
      setIsOpen(false);
      setDraft(valueLabel ?? '');
    }
  };

  return (
    <div ref={containerRef} className="relative">
      <label htmlFor={inputId} className="block text-sm font-medium text-gray-700 mb-1">
        {label}
        {required && (
          <span className="text-red-600 ml-0.5" aria-hidden="true">
            *
          </span>
        )}
      </label>
      <div className="flex gap-2">
        <input
          id={inputId}
          role="combobox"
          aria-expanded={isOpen}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-invalid={error ? true : undefined}
          autoComplete="off"
          disabled={disabled}
          value={draft}
          placeholder="Search companies…"
          onFocus={() => setIsOpen(true)}
          onChange={(e) => {
            setDraft(e.target.value);
            setIsOpen(true);
            setHighlighted(0);
            if (value) onChange(null, null);
          }}
          onKeyDown={onKeyDown}
          className={`block w-full px-3 py-2 border rounded-md shadow-sm text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 disabled:bg-gray-50 disabled:text-gray-500 ${
            error ? 'border-red-300 text-red-900' : 'border-gray-300 text-gray-900'
          }`}
        />
        {value && !disabled && (
          <button
            type="button"
            onClick={clearSelection}
            className="px-3 py-2 text-sm text-gray-600 border border-gray-300 rounded-md hover:bg-gray-50"
          >
            Clear
          </button>
        )}
      </div>
      {isOpen && debounced.length > 0 && (
        <ul
          id={listId}
          role="listbox"
          className="absolute z-10 mt-1 w-full max-h-56 overflow-auto rounded-md border border-gray-200 bg-white shadow-lg text-sm"
        >
          {options.length === 0 && <li className="px-3 py-2 text-gray-500">No companies found</li>}
          {options.map((opt, i) => (
            <li
              key={opt.id}
              role="option"
              aria-selected={i === highlighted}
              onMouseDown={(e) => {
                e.preventDefault();
                selectCompany(opt.id, opt.name);
              }}
              className={`px-3 py-2 cursor-pointer ${
                i === highlighted ? 'bg-primary-50 text-primary-900' : 'text-gray-900'
              }`}
            >
              {opt.name}
            </li>
          ))}
        </ul>
      )}
      {hint && !error && <p className="mt-1 text-xs text-gray-500">{hint}</p>}
      {error && <p className="mt-1 text-xs text-red-600">{error}</p>}
    </div>
  );
}
