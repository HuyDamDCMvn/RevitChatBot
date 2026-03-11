import { useMemo } from 'react';
import {
  BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer,
  PieChart, Pie, Cell, Legend,
} from 'recharts';

const COLORS = [
  '#3b82f6', '#ef4444', '#22c55e', '#f59e0b', '#8b5cf6',
  '#ec4899', '#06b6d4', '#f97316', '#6366f1', '#14b8a6',
];

interface ChartItem {
  name: string;
  value: number;
  [key: string]: unknown;
}

interface Props {
  code: string;
}

function parseChartData(raw: string): { type: 'bar' | 'pie'; data: ChartItem[] } | null {
  try {
    const parsed = JSON.parse(raw);
    if (Array.isArray(parsed)) {
      const data = parsed
        .filter((d) => d.name !== undefined && d.value !== undefined)
        .map((d) => ({ ...d, name: String(d.name), value: Number(d.value) }));
      if (data.length === 0) return null;
      return { type: data.length <= 8 ? 'pie' : 'bar', data };
    }
    if (parsed.type && Array.isArray(parsed.data)) {
      return {
        type: parsed.type === 'pie' ? 'pie' : 'bar',
        data: parsed.data.map((d: ChartItem) => ({
          ...d,
          name: String(d.name),
          value: Number(d.value),
        })),
      };
    }
  } catch {
    // not valid JSON
  }
  return null;
}

export function ChartBlock({ code }: Props) {
  const chart = useMemo(() => parseChartData(code), [code]);

  if (!chart || chart.data.length === 0) {
    return (
      <pre className="rounded bg-gray-200 p-2 text-xs text-gray-800 overflow-auto">
        <code>{code}</code>
      </pre>
    );
  }

  if (chart.type === 'pie') {
    return (
      <div className="my-2 rounded bg-white p-2" style={{ height: 280 }}>
        <ResponsiveContainer width="100%" height="100%">
          <PieChart>
            <Pie
              data={chart.data}
              dataKey="value"
              nameKey="name"
              cx="50%"
              cy="50%"
              outerRadius={90}
              label={({ name, percent }) => `${name} ${((percent ?? 0) * 100).toFixed(0)}%`}
              labelLine={false}
              fontSize={11}
            >
              {chart.data.map((_, i) => (
                <Cell key={i} fill={COLORS[i % COLORS.length]} />
              ))}
            </Pie>
            <Tooltip />
            <Legend wrapperStyle={{ fontSize: 11 }} />
          </PieChart>
        </ResponsiveContainer>
      </div>
    );
  }

  return (
    <div className="my-2 rounded bg-white p-2" style={{ height: 280 }}>
      <ResponsiveContainer width="100%" height="100%">
        <BarChart data={chart.data} margin={{ top: 5, right: 10, left: 0, bottom: 40 }}>
          <CartesianGrid strokeDasharray="3 3" />
          <XAxis dataKey="name" angle={-35} textAnchor="end" fontSize={10} interval={0} height={60} />
          <YAxis fontSize={10} />
          <Tooltip />
          <Bar dataKey="value" fill="#3b82f6" radius={[4, 4, 0, 0]} />
        </BarChart>
      </ResponsiveContainer>
    </div>
  );
}
