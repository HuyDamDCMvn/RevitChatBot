import { useState } from 'react';
import { bridge } from '../services/bridge';
import { CodeGenModelSuggestData, ModelPullProgressData } from '../types/messages';

interface Props {
  suggest: CodeGenModelSuggestData;
  pullProgress: ModelPullProgressData | null;
  isPulling: boolean;
  onDismiss: () => void;
}

export function CodeGenBanner({ suggest, pullProgress, isPulling, onDismiss }: Props) {
  const [selectedModel, setSelectedModel] = useState(suggest.modelName);
  const [showOptions, setShowOptions] = useState(false);

  const handlePull = () => {
    bridge.pullModel(selectedModel, true);
  };

  const handleCancel = () => {
    bridge.cancelPull();
  };

  const handleUseExisting = (name: string) => {
    bridge.setCodeGenModel(name);
    onDismiss();
  };

  const formatPercent = (p: number) => `${Math.round(p)}%`;
  const formatBytes = (b: number) => {
    if (b <= 0) return '';
    const gb = b / (1024 * 1024 * 1024);
    if (gb >= 1) return `${gb.toFixed(1)} GB`;
    const mb = b / (1024 * 1024);
    return `${Math.round(mb)} MB`;
  };

  if (isPulling && pullProgress) {
    return (
      <div className="mx-3 mb-3 rounded-xl border border-blue-200 bg-blue-50 p-3">
        <div className="flex items-center justify-between mb-2">
          <div className="flex items-center gap-2">
            <span className="h-2 w-2 animate-pulse rounded-full bg-blue-500" />
            <span className="text-xs font-medium text-blue-700">
              Downloading {pullProgress.modelName}
            </span>
          </div>
          <button
            onClick={handleCancel}
            className="text-[10px] text-blue-500 hover:text-blue-700"
          >
            Cancel
          </button>
        </div>
        <div className="relative h-2 w-full overflow-hidden rounded-full bg-blue-200">
          <div
            className="absolute left-0 top-0 h-full rounded-full bg-blue-500 transition-all duration-300"
            style={{ width: `${Math.min(pullProgress.percent, 100)}%` }}
          />
        </div>
        <div className="mt-1 flex justify-between text-[10px] text-blue-600">
          <span>{pullProgress.status}</span>
          <span>
            {pullProgress.total > 0
              ? `${formatBytes(pullProgress.completed)} / ${formatBytes(pullProgress.total)} (${formatPercent(pullProgress.percent)})`
              : formatPercent(pullProgress.percent)}
          </span>
        </div>
      </div>
    );
  }

  return (
    <div className="mx-3 mb-3 rounded-xl border border-amber-200 bg-amber-50 p-3">
      <div className="flex items-start justify-between">
        <div className="flex items-start gap-2">
          <span className="mt-0.5 text-base">⚡</span>
          <div>
            <p className="text-xs font-semibold text-amber-800">
              Upgrade CodeGen
            </p>
            <p className="mt-0.5 text-[11px] text-amber-700 leading-relaxed">
              A code-specialized model generates far more accurate Revit API code.
              Completely free, runs locally.
            </p>
          </div>
        </div>
        <button
          onClick={onDismiss}
          className="ml-2 flex-shrink-0 text-amber-400 hover:text-amber-600"
          title="Dismiss"
        >
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
            <path d="M6.28 5.22a.75.75 0 0 0-1.06 1.06L8.94 10l-3.72 3.72a.75.75 0 1 0 1.06 1.06L10 11.06l3.72 3.72a.75.75 0 1 0 1.06-1.06L11.06 10l3.72-3.72a.75.75 0 0 0-1.06-1.06L10 8.94 6.28 5.22Z" />
          </svg>
        </button>
      </div>

      {showOptions ? (
        <div className="mt-2 space-y-1.5">
          {suggest.options.map((opt) => (
            <label
              key={opt.name}
              className={`flex items-center gap-2 rounded-lg border px-2.5 py-1.5 text-[11px] cursor-pointer transition-colors ${
                opt.name === selectedModel
                  ? 'border-amber-400 bg-amber-100'
                  : 'border-amber-200 bg-white hover:bg-amber-50'
              }`}
            >
              <input
                type="radio"
                name="codegen-model"
                value={opt.name}
                checked={opt.name === selectedModel}
                onChange={() => setSelectedModel(opt.name)}
                className="accent-amber-600"
              />
              <div className="flex-1 min-w-0">
                <span className="font-medium text-amber-900">{opt.name}</span>
                <span className="ml-1 text-amber-600">({opt.minVram} VRAM)</span>
              </div>
              {opt.installed && (
                <button
                  onClick={(e) => {
                    e.preventDefault();
                    handleUseExisting(opt.name);
                  }}
                  className="flex-shrink-0 rounded bg-green-100 px-1.5 py-0.5 text-[10px] font-medium text-green-700 hover:bg-green-200"
                >
                  Use
                </button>
              )}
            </label>
          ))}
        </div>
      ) : (
        <div className="mt-2 flex items-center gap-2">
          <span className="rounded bg-amber-100 px-2 py-0.5 text-[11px] font-medium text-amber-800">
            {suggest.modelName}
          </span>
          <span className="text-[10px] text-amber-600">
            {suggest.description}
          </span>
        </div>
      )}

      <div className="mt-2.5 flex items-center gap-2">
        <button
          onClick={handlePull}
          disabled={isPulling}
          className="rounded-lg bg-amber-600 px-3 py-1 text-[11px] font-medium text-white hover:bg-amber-700 disabled:opacity-50"
        >
          Pull & Use
        </button>
        {!showOptions && (
          <button
            onClick={() => setShowOptions(true)}
            className="rounded-lg px-2 py-1 text-[11px] text-amber-700 hover:bg-amber-100"
          >
            More options
          </button>
        )}
        <button
          onClick={onDismiss}
          className="rounded-lg px-2 py-1 text-[11px] text-amber-600 hover:bg-amber-100"
        >
          Skip
        </button>
      </div>
    </div>
  );
}
