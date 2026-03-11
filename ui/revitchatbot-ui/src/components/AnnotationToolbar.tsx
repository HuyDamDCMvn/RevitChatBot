import { useState } from 'react';

interface Props {
  onSend: (message: string) => void;
  disabled: boolean;
}

const quickActions = [
  {
    id: 'smart-annotate',
    label: 'Smart Annotate',
    icon: '✦',
    command: 'Smart annotate the active view: tag all MEP elements, arrange tags to avoid overlaps, and check quality',
    description: 'One-click: tag + arrange + quality check',
  },
  {
    id: 'tag-all',
    label: 'Tag All',
    icon: '🏷',
    command: 'Place tags on all untagged MEP elements in the active view with smart positioning',
    description: 'Tag untagged elements with smart placement',
  },
  {
    id: 'arrange',
    label: 'Arrange Tags',
    icon: '⇲',
    command: 'Arrange all tags in the active view using force-directed layout to avoid overlapping with elements',
    description: 'Smart-arrange tags avoiding all obstacles',
  },
  {
    id: 'dimension',
    label: 'Auto Dimension',
    icon: '↔',
    command: 'Add dimensions to MEP elements in the active view along horizontal and vertical centerlines',
    description: 'Create dimension chains automatically',
  },
  {
    id: 'quality',
    label: 'Check Quality',
    icon: '✓',
    command: 'Check annotation quality in the active view: detect overlaps, missing tags, and alignment issues',
    description: 'Score annotation quality (0-100)',
  },
  {
    id: 'batch',
    label: 'Batch All Views',
    icon: '📋',
    command: 'Batch annotate all plan views: tag MEP elements, arrange tags, for all floor plans',
    description: 'Annotate multiple views at once',
  },
];

const categoryOptions = [
  { value: 'all', label: 'All MEP' },
  { value: 'Ducts', label: 'Ducts' },
  { value: 'Pipes', label: 'Pipes' },
  { value: 'Equipment', label: 'Equipment' },
  { value: 'Cable Trays', label: 'Cable Trays' },
  { value: 'Conduits', label: 'Conduits' },
];

export function AnnotationToolbar({ onSend, disabled }: Props) {
  const [expanded, setExpanded] = useState(false);
  const [category, setCategory] = useState('all');

  const handleAction = (command: string) => {
    const catSuffix = category !== 'all' ? ` for ${category} only` : '';
    onSend(command + catSuffix);
  };

  return (
    <div className="border-t border-gray-100 bg-gray-50/80">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center justify-between px-3 py-1.5 text-[11px] font-medium text-gray-500 hover:text-revit-600 transition-colors"
      >
        <span className="flex items-center gap-1.5">
          <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 16 16" fill="currentColor" className="h-3.5 w-3.5">
            <path d="M3 4.75a.75.75 0 0 1 .75-.75h8.5a.75.75 0 0 1 0 1.5h-8.5A.75.75 0 0 1 3 4.75ZM3 8a.75.75 0 0 1 .75-.75h8.5a.75.75 0 0 1 0 1.5h-8.5A.75.75 0 0 1 3 8Zm0 3.25a.75.75 0 0 1 .75-.75h8.5a.75.75 0 0 1 0 1.5h-8.5a.75.75 0 0 1-.75-.75Z" />
          </svg>
          Annotation Tools
        </span>
        <svg
          xmlns="http://www.w3.org/2000/svg"
          viewBox="0 0 16 16"
          fill="currentColor"
          className={`h-3 w-3 transition-transform ${expanded ? 'rotate-180' : ''}`}
        >
          <path fillRule="evenodd" d="M4.22 6.22a.75.75 0 0 1 1.06 0L8 8.94l2.72-2.72a.75.75 0 1 1 1.06 1.06l-3.25 3.25a.75.75 0 0 1-1.06 0L4.22 7.28a.75.75 0 0 1 0-1.06Z" clipRule="evenodd" />
        </svg>
      </button>

      {expanded && (
        <div className="px-3 pb-2 space-y-2">
          <div className="flex items-center gap-2">
            <label className="text-[10px] text-gray-500 shrink-0">Category:</label>
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="flex-1 rounded border border-gray-200 bg-white px-2 py-0.5 text-[11px] text-gray-700 outline-none focus:border-revit-400"
            >
              {categoryOptions.map((opt) => (
                <option key={opt.value} value={opt.value}>{opt.label}</option>
              ))}
            </select>
          </div>

          <div className="grid grid-cols-3 gap-1.5">
            {quickActions.map((action) => (
              <button
                key={action.id}
                onClick={() => handleAction(action.command)}
                disabled={disabled}
                title={action.description}
                className="flex flex-col items-center gap-0.5 rounded-lg border border-gray-200 bg-white px-2 py-1.5
                  text-center transition-all hover:border-revit-300 hover:bg-revit-50 hover:shadow-sm
                  disabled:opacity-40 disabled:hover:border-gray-200 disabled:hover:bg-white"
              >
                <span className="text-sm leading-none">{action.icon}</span>
                <span className="text-[10px] font-medium text-gray-700 leading-tight">{action.label}</span>
              </button>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
