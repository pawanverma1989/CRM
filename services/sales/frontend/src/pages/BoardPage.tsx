import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { listPipelines } from '../api/pipelines';
import { getBoard } from '../api/board';
import { LoadingBlock, ErrorBlock, EmptyBlock } from '../components/StateBlocks';
import { formatMoney } from '../lib/utils';
import type { BoardDealDto } from '../types';

function DealCard({ deal }: { deal: BoardDealDto }) {
  const navigate = useNavigate();
  const isOverdue = deal.isOverdue;
  const closeDate = deal.expectedCloseDate;

  return (
    <button
      type="button"
      onClick={() => navigate(`/deals/${deal.id}`)}
      className="w-full text-left bg-white rounded-lg border border-gray-200 shadow-sm p-3 hover:border-primary-300 hover:shadow-md transition-all focus:outline-none focus-visible:ring-2 focus-visible:ring-primary-500"
    >
      <p className="text-sm font-medium text-gray-900 mb-1 line-clamp-2">{deal.name}</p>
      {deal.companyName && (
        <p className="text-xs text-gray-500 mb-1">{deal.companyName}</p>
      )}
      <p className="text-sm font-semibold text-gray-800 mb-2">
        {formatMoney(deal.amount, deal.currency)}
        {deal.probability > 0 && (
          <span className="text-xs text-gray-400 font-normal ml-1">({deal.probability}%)</span>
        )}
      </p>
      <div className="flex flex-wrap items-center gap-1 mb-1">
        {deal.isStale && (
          <span className="inline-flex px-1.5 py-0.5 text-xs font-medium bg-yellow-100 text-yellow-800 rounded">Stale</span>
        )}
        {isOverdue && (
          <span className="inline-flex px-1.5 py-0.5 text-xs font-medium bg-red-100 text-red-800 rounded">Overdue</span>
        )}
      </div>
      {closeDate && (
        <p className={`text-xs ${isOverdue ? 'text-red-600 font-medium' : 'text-gray-400'}`}>
          Close: {closeDate}
        </p>
      )}
      {deal.ownerName && (
        <p className="text-xs text-gray-400 mt-1">{deal.ownerName}</p>
      )}
      {deal.tags.length > 0 && (
        <div className="flex flex-wrap gap-1 mt-1">
          {deal.tags.slice(0, 3).map((tag) => (
            <span key={tag} className="inline-flex px-1.5 py-0.5 bg-gray-100 text-gray-600 text-xs rounded-full">
              {tag}
            </span>
          ))}
          {deal.tags.length > 3 && (
            <span className="text-xs text-gray-400">+{deal.tags.length - 3}</span>
          )}
        </div>
      )}
    </button>
  );
}

export function BoardPage() {
  const navigate = useNavigate();
  const [selectedPipelineId, setSelectedPipelineId] = useState<string>('');

  const { data: pipelines = [], isLoading: pipelinesLoading, error: pipelinesError } = useQuery({
    queryKey: ['pipelines'],
    queryFn: listPipelines,
    staleTime: 60_000,
    select: (data) => {
      if (!selectedPipelineId && data.length > 0) {
        const defaultPipeline = data.find((p) => p.isDefault) ?? data[0];
        setSelectedPipelineId(defaultPipeline.id);
      }
      return data;
    },
  });

  const effectivePipelineId = selectedPipelineId || (pipelines.find((p) => p.isDefault) ?? pipelines[0])?.id || '';

  const {
    data: board,
    isLoading: boardLoading,
    error: boardError,
    refetch,
  } = useQuery({
    queryKey: ['board', effectivePipelineId],
    queryFn: () => getBoard(effectivePipelineId),
    enabled: !!effectivePipelineId,
    staleTime: 30_000,
  });

  if (pipelinesLoading) return <LoadingBlock label="Loading pipelines…" />;
  if (pipelinesError) return <ErrorBlock error={pipelinesError} title="Could not load pipelines" onRetry={() => refetch()} />;

  if (pipelines.length === 0) {
    return (
      <EmptyBlock
        title="No pipelines yet"
        message="Create a pipeline to start tracking deals."
        action={
          <button
            type="button"
            onClick={() => navigate('/pipelines')}
            className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700"
          >
            Manage pipelines
          </button>
        }
      />
    );
  }

  return (
    <div>
      <div className="flex flex-wrap items-center justify-between gap-4 mb-6">
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-bold text-gray-900">Pipeline Board</h1>
          <select
            value={effectivePipelineId}
            onChange={(e) => setSelectedPipelineId(e.target.value)}
            className="px-3 py-1.5 border border-gray-300 rounded-md text-sm bg-white focus:outline-none focus:ring-2 focus:ring-primary-500"
            aria-label="Select pipeline"
          >
            {pipelines.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}{p.isDefault ? ' (default)' : ''}
              </option>
            ))}
          </select>
        </div>
        <button
          type="button"
          onClick={() => navigate('/deals/new')}
          className="px-4 py-2 text-sm font-medium text-white bg-primary-600 rounded-md hover:bg-primary-700"
        >
          New deal
        </button>
      </div>

      {boardLoading && <LoadingBlock label="Loading board…" />}
      {boardError && <ErrorBlock error={boardError} title="Could not load board" onRetry={() => refetch()} />}

      {board && (
        <div className="overflow-x-auto pb-4">
          <div className="flex gap-4 min-w-max">
            {board.columns.map((col) => (
              <div key={col.stageId} className="w-72 flex-shrink-0">
                <div className="bg-gray-100 rounded-t-lg px-3 py-2 border border-gray-200 border-b-0">
                  <div className="flex items-center justify-between mb-1">
                    <h3 className="text-sm font-semibold text-gray-800">{col.stageName}</h3>
                    <span className="text-xs text-gray-500 bg-gray-200 rounded-full px-2 py-0.5">{col.dealCount}</span>
                  </div>
                  <div className="flex items-center justify-between text-xs text-gray-500">
                    <span>{formatMoney(col.totalAmount)}</span>
                    <span className="text-gray-400">Wtd: {formatMoney(col.weightedForecast)}</span>
                  </div>
                </div>
                <div className="bg-gray-50 border border-gray-200 border-t-0 rounded-b-lg min-h-[200px] p-2 flex flex-col gap-2">
                  {col.deals.length === 0 ? (
                    <p className="text-xs text-gray-400 text-center py-8">No deals</p>
                  ) : (
                    col.deals.map((deal) => (
                      <DealCard key={deal.id} deal={deal} />
                    ))
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {board && board.columns.length === 0 && (
        <EmptyBlock title="No open stages" message="Add open stages to your pipeline to use the board view." />
      )}
    </div>
  );
}
