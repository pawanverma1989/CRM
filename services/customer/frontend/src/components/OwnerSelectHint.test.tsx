import { describe, expect, it, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { OwnerSelectHint } from './OwnerSelectHint';

const baseProps = { ownerCount: 2, isLoading: false, isFetching: false, onRefresh: () => {} };

describe('OwnerSelectHint', () => {
  it('shows the eventual-consistency hint and refreshes on click', async () => {
    const onRefresh = vi.fn();
    render(<OwnerSelectHint {...baseProps} onRefresh={onRefresh} />);
    expect(screen.getByText(/New users appear here within a few seconds/)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: 'Refresh' }));
    expect(onRefresh).toHaveBeenCalledTimes(1);
  });

  it('shows the empty message once loaded with no owners', () => {
    render(<OwnerSelectHint {...baseProps} ownerCount={0} />);
    expect(screen.getByText(/No users available yet/)).toBeInTheDocument();
  });

  it('does not show the empty message while loading', () => {
    render(<OwnerSelectHint {...baseProps} ownerCount={0} isLoading isFetching />);
    expect(screen.queryByText(/No users available yet/)).not.toBeInTheDocument();
  });

  it('disables the button and announces while fetching', () => {
    render(<OwnerSelectHint {...baseProps} isFetching />);
    expect(screen.getByRole('button', { name: 'Refreshing…' })).toBeDisabled();
    expect(screen.getByRole('status')).toHaveTextContent('Refreshing users');
  });
});
