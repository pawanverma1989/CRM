import { useId, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { getTagSuggestions } from '../api/tags';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { MAX_TAGS_PER_RECORD, MAX_TAG_LENGTH, normalizeTag } from '../lib/validation';

interface TagInputProps {
  value: string[];
  onChange: (tags: string[]) => void;
  label?: string;
  error?: string;
  disabled?: boolean;
}

export function TagInput({ value, onChange, label = 'Tags', error, disabled }: TagInputProps) {
  const [draft, setDraft] = useState('');
  const inputId = useId();
  const listId = `${inputId}-suggestions`;
  const prefix = useDebouncedValue(draft.trim().toLowerCase(), 250);

  const { data: suggestions = [] } = useQuery({
    queryKey: ['tags', prefix],
    queryFn: () => getTagSuggestions(prefix),
    enabled: !disabled && prefix.length > 0,
    staleTime: 60_000,
  });

  const atLimit = value.length >= MAX_TAGS_PER_RECORD;

  const addTag = (raw: string) => {
    const tag = normalizeTag(raw);
    if (tag === '') return;
    if (value.includes(tag)) {
      setDraft('');
      return;
    }
    if (atLimit) return;
    onChange([...value, tag]);
    setDraft('');
  };

  const removeTag = (tag: string) => onChange(value.filter((t) => t !== tag));

  const onKeyDown = (event: React.KeyboardEvent<HTMLInputElement>) => {
    if (event.key === 'Enter' || event.key === ',') {
      event.preventDefault();
      addTag(draft);
    } else if (event.key === 'Backspace' && draft === '' && value.length > 0) {
      removeTag(value[value.length - 1]);
    }
  };

  const unusedSuggestions = suggestions.filter((s) => !value.includes(s)).slice(0, 10);

  return (
    <div>
      <label htmlFor={inputId} className="block text-sm font-medium text-gray-700 mb-1">
        {label}
      </label>
      <div
        className={`flex flex-wrap items-center gap-1.5 px-2 py-1.5 border rounded-md bg-white ${
          error ? 'border-red-300' : 'border-gray-300'
        } ${disabled ? 'bg-gray-50' : ''}`}
      >
        {value.map((tag) => (
          <span
            key={tag}
            className="inline-flex items-center gap-1 px-2 py-0.5 bg-primary-50 text-primary-800 text-xs font-medium rounded-full"
          >
            {tag}
            {!disabled && (
              <button
                type="button"
                onClick={() => removeTag(tag)}
                className="text-primary-500 hover:text-primary-800 leading-none"
                aria-label={`Remove tag ${tag}`}
              >
                ×
              </button>
            )}
          </span>
        ))}
        <input
          id={inputId}
          type="text"
          list={listId}
          value={draft}
          disabled={disabled || atLimit}
          maxLength={MAX_TAG_LENGTH}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={onKeyDown}
          onBlur={() => addTag(draft)}
          placeholder={atLimit ? `Limit of ${MAX_TAGS_PER_RECORD} tags reached` : 'Add a tag…'}
          aria-describedby={`${inputId}-hint`}
          aria-invalid={error ? true : undefined}
          className="flex-1 min-w-[8rem] px-1 py-0.5 text-sm border-0 focus:outline-none focus:ring-0 disabled:bg-transparent"
        />
        <datalist id={listId}>
          {unusedSuggestions.map((s) => (
            <option key={s} value={s} />
          ))}
        </datalist>
      </div>
      <p id={`${inputId}-hint`} className="mt-1 text-xs text-gray-500">
        Press Enter or comma to add. Stored lower-case, {MAX_TAG_LENGTH} characters each, up to{' '}
        {MAX_TAGS_PER_RECORD} per record ({value.length} used).
      </p>
      {error && <p className="mt-1 text-xs text-red-600">{error}</p>}
    </div>
  );
}

export function TagChips({ tags }: { tags: string[] }) {
  if (tags.length === 0) return <span className="text-sm text-gray-500">—</span>;
  return (
    <span className="flex flex-wrap gap-1">
      {tags.map((tag) => (
        <span
          key={tag}
          className="inline-flex px-2 py-0.5 bg-gray-100 text-gray-700 text-xs font-medium rounded-full"
        >
          {tag}
        </span>
      ))}
    </span>
  );
}
