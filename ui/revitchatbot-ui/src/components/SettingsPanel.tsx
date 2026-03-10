import { useState } from 'react';
import { bridge } from '../services/bridge';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

export function SettingsPanel({ isOpen, onClose }: Props) {
  const [model, setModel] = useState('qwen2.5:7b');
  const [temperature, setTemperature] = useState(0.7);
  const [ollamaUrl, setOllamaUrl] = useState('http://localhost:11434');

  if (!isOpen) return null;

  const handleSave = () => {
    bridge.updateSettings({
      model,
      temperature,
      ollamaUrl,
    });
    onClose();
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
              Model
            </label>
            <input
              type="text"
              value={model}
              onChange={(e) => setModel(e.target.value)}
              className="w-full rounded-lg border border-gray-300 px-3 py-1.5 text-sm outline-none focus:border-revit-400"
            />
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
