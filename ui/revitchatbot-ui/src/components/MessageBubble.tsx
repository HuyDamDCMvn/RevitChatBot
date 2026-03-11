import ReactMarkdown, { Components } from 'react-markdown';
import remarkGfm from 'remark-gfm';
import { ChatMessage } from '../types/messages';
import { MermaidBlock } from './MermaidBlock';
import { ChartBlock } from './ChartBlock';

interface Props {
  message: ChatMessage;
}

const markdownComponents: Components = {
  code({ className, children, ...props }) {
    const lang = className?.replace('language-', '') ?? '';
    const text = String(children).replace(/\n$/, '');

    if (lang === 'mermaid') return <MermaidBlock code={text} />;
    if (lang === 'chart' || lang === 'json-chart') return <ChartBlock code={text} />;

    if (className) {
      return (
        <pre className="rounded bg-gray-200 p-2 text-xs overflow-auto my-1">
          <code className={className} {...props}>{children}</code>
        </pre>
      );
    }

    return (
      <code className="rounded bg-gray-200 px-1.5 py-0.5 text-xs font-mono" {...props}>
        {children}
      </code>
    );
  },
  table({ children }) {
    return (
      <div className="overflow-x-auto my-2 rounded-lg border border-gray-200">
        <table className="min-w-full text-xs border-collapse">{children}</table>
      </div>
    );
  },
  thead({ children }) {
    return <thead className="bg-gray-200/70">{children}</thead>;
  },
  th({ children }) {
    return (
      <th className="border-b border-gray-300 px-3 py-1.5 text-left font-semibold text-gray-700 whitespace-nowrap">
        {children}
      </th>
    );
  },
  td({ children }) {
    return (
      <td className="border-b border-gray-100 px-3 py-1.5 text-gray-600">{children}</td>
    );
  },
  tr({ children }) {
    return <tr className="even:bg-gray-50 hover:bg-revit-50/50 transition-colors">{children}</tr>;
  },
};

function ImageGallery({ images }: { images: ChatMessage['images'] }) {
  if (!images || images.length === 0) return null;
  return (
    <div className="mt-2 flex flex-wrap gap-2">
      {images.map((img, i) => (
        <figure key={i} className="max-w-full">
          <img
            src={`data:${img.mimeType ?? 'image/png'};base64,${img.base64}`}
            alt={img.caption ?? `Image ${i + 1}`}
            className="rounded-lg max-h-64 object-contain cursor-pointer hover:opacity-90 transition-opacity"
            onClick={() => {
              const w = window.open();
              if (w) {
                w.document.write(`<img src="data:${img.mimeType ?? 'image/png'};base64,${img.base64}" style="max-width:100%;height:auto" />`);
              }
            }}
          />
          {img.caption && <figcaption className="text-[10px] text-gray-500 mt-0.5">{img.caption}</figcaption>}
        </figure>
      ))}
    </div>
  );
}

function SystemBubble({ message }: Props) {
  const variantStyles: Record<string, string> = {
    clarification: 'bg-amber-50 border-amber-200 text-amber-900',
    action_plan: 'bg-blue-50 border-blue-200 text-blue-900',
    confirmation: 'bg-orange-50 border-orange-200 text-orange-900',
  };

  const variantIcons: Record<string, string> = {
    clarification: '?',
    action_plan: '\u{1F4CB}',
    confirmation: '\u26A0',
  };

  const variant = message.variant ?? 'clarification';
  const style = variantStyles[variant] ?? variantStyles.clarification;
  const icon = variantIcons[variant] ?? '?';

  return (
    <div className="flex justify-center mb-3">
      <div className={`max-w-[90%] rounded-xl border px-4 py-3 text-sm ${style}`}>
        <div className="flex items-start gap-2">
          <span className="text-base leading-none mt-0.5">{icon}</span>
          <div className="flex-1">
            <p className="whitespace-pre-wrap">{message.content}</p>

            {message.variant === 'clarification' && message.clarificationOptions && message.clarificationOptions.length > 0 && (
              <div className="mt-2 flex flex-wrap gap-1.5">
                {message.clarificationOptions.map((opt, i) => (
                  <span
                    key={i}
                    className="inline-block rounded-full bg-amber-100 px-3 py-1 text-xs font-medium text-amber-800"
                  >
                    {opt}
                  </span>
                ))}
              </div>
            )}

            {message.variant === 'action_plan' && message.actionPlan && (
              <div className="mt-2 space-y-1 text-xs">
                {message.actionPlan.actions?.map((action, i) => (
                  <div
                    key={i}
                    className={`rounded px-2 py-1 ${action.isDestructive ? 'bg-red-100 text-red-800' : 'bg-blue-100 text-blue-800'}`}
                  >
                    <span className="font-mono font-medium">{action.toolName}</span>
                    {action.description && <span className="ml-1 text-blue-600">— {action.description}</span>}
                  </div>
                ))}
                {message.actionPlan.riskLevel && (
                  <p className="text-blue-600 mt-1">
                    Risk: <span className="font-medium">{message.actionPlan.riskLevel}</span>
                  </p>
                )}
              </div>
            )}
          </div>
        </div>
        <div className="text-[10px] mt-1 opacity-60 text-right">
          {new Date(message.timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
        </div>
      </div>
    </div>
  );
}

export function MessageBubble({ message }: Props) {
  if (message.role === 'system') {
    return <SystemBubble message={message} />;
  }

  const isUser = message.role === 'user';

  return (
    <div className={`flex ${isUser ? 'justify-end' : 'justify-start'} mb-3`}>
      <div
        className={`max-w-[85%] rounded-2xl px-4 py-2.5 text-sm leading-relaxed ${
          isUser
            ? 'bg-revit-600 text-white rounded-br-md'
            : 'bg-gray-100 text-gray-800 rounded-bl-md'
        }`}
      >
        {isUser ? (
          <p className="whitespace-pre-wrap">{message.content}</p>
        ) : (
          <div className="prose prose-sm max-w-none prose-p:my-1 prose-pre:bg-gray-200 prose-pre:text-gray-800 prose-code:text-revit-700">
            <ReactMarkdown remarkPlugins={[remarkGfm]} components={markdownComponents}>{message.content}</ReactMarkdown>
            {message.streaming && (
              <span className="inline-block w-2 h-4 ml-0.5 -mb-0.5 bg-revit-500 animate-pulse rounded-sm" />
            )}
            <ImageGallery images={message.images} />
          </div>
        )}
        <div
          className={`text-[10px] mt-1 ${
            isUser ? 'text-revit-200' : 'text-gray-400'
          }`}
        >
          {new Date(message.timestamp).toLocaleTimeString([], {
            hour: '2-digit',
            minute: '2-digit',
          })}
        </div>
      </div>
    </div>
  );
}
