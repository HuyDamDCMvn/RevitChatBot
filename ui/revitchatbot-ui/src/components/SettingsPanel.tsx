import { useEffect, useState } from 'react';
import { bridge } from '../services/bridge';
import { InstalledModelInfo, MessageTypes } from '../types/messages';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

const DEFAULT_MODEL = 'qwen2.5:7b';

export function SettingsPanel({ isOpen, onClose }: Props) {
  const [model, setModel] = useState(DEFAULT_MODEL);
  const [temperature, setTemperature] = useState(0.3);
  const [ollamaUrl, setOllamaUrl] = useState('http://localhost:11434');
  const [models, setModels] = useState<InstalledModelInfo[]>([]);
  const [loadingModels, setLoadingModels] = useState(false);

  useEffect(() => {
    if (!isOpen) return;

    setLoadingModels(true);
    const unsub = bridge.onMessage((msg) => {
      if (msg.type !== MessageTypes.HEALTH_STATUS) return;
      const data = msg.data as { installedModels?: InstalledModelInfo[] } | undefined;
      setModels(data?.installedModels ?? []);
      setLoadingModels(false);
    });

    bridge.requestHealthCheck();

    const timeout = setTimeout(() => setLoadingModels(false), 8000);
    return () => {
      unsub();
      clearTimeout(timeout);
    };
  }, [isOpen]);

  if (!isOpen) return null;

  const handleSave = () => {
    bridge.updateSettings({ model, temperature, ollamaUrl });
    onClose();
  };

  const formatSize = (mb: number) => {
    if (mb >= 1024) return `${(mb / 1024).toFixed(1)} GB`;
    return `${mb} MB`;
  };

  return (
    <div className="absolute inset-0 z-50 flex items-center justify-center bg-black/30">
      <div className="mx-4 w-full max-w-sm rounded-xl bg-white p-5 shadow-xl">
        <h2 className="mb-4 text-base font-semibold text-gray-800">Settings</h2>

        <div className="space-y-3">
          <div>
            <label className="mb-1 block text-xs font-medium text-gray-600">
              Ollama URL
            </label>
            <input
              type="text"
              value={ollamaUrl}
              onChange={(e) => setOllamaUrl(e.target.value)}
              className="w-full rounded-lg border border-gray-300 px-3 py-1.5 text-sm outline-none focus:border-revit-400"
            />
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-600">
              LLM Model
            </label>
            {loadingModels ? (
              <div className="flex items-center gap-2 rounded-lg border border-gray-300 px-3 py-1.5 text-sm text-gray-400">
                <span className="h-3 w-3 animate-spin rounded-full border-2 border-gray-300 border-t-revit-500" />
                Loading models...
              </div>
            ) : models.length > 0 ? (
              <select
                value={model}
                onChange={(e) => setModel(e.target.value)}
                className="w-full rounded-lg border border-gray-300 bg-white px-3 py-1.5 text-sm outline-none focus:border-revit-400"
              >
                {models.map((m) => (
                  <option key={m.name} value={m.name}>
                    {m.name} ({formatSize(m.sizeMB)})
                  </option>
                ))}
              </select>
            ) : (
              <input
                type="text"
                value={model}
                onChange={(e) => setModel(e.target.value)}
                placeholder="e.g. qwen2.5:7b"
                className="w-full rounded-lg border border-gray-300 px-3 py-1.5 text-sm outline-none focus:border-revit-400"
              />
            )}
            <p className="mt-1 text-[10px] text-gray-400">
              {models.length > 0
                ? `${models.length} model(s) installed`
                : 'Could not fetch models — type name manually'}
            </p>
          </div>

          <div>
            <label className="mb-1 block text-xs font-medium text-gray-600">
              Temperature: {temperature}
            </label>
            <input
              type="range"
              min="0"
              max="1"
              step="0.1"
              value={temperature}
              onChange={(e) => setTemperature(parseFloat(e.target.value))}
              className="w-full accent-revit-600"
            />
          </div>
        </div>

        <div className="mt-5 flex justify-end gap-2">
          <button
            onClick={onClose}
            className="rounded-lg px-3 py-1.5 text-sm text-gray-600 hover:bg-gray-100"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            className="rounded-lg bg-revit-600 px-3 py-1.5 text-sm text-white hover:bg-revit-700"
          >
            Save
          </button>
        </div>
      </div>
    </div>
  );
}
