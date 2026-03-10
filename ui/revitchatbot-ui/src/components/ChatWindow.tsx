import { useEffect, useRef } from 'react';
import { useRevitBridge } from '../hooks/useRevitBridge';
import { InputBar } from './InputBar';
import { MessageBubble } from './MessageBubble';
import { SkillPanel } from './SkillPanel';

export function ChatWindow() {
  const { messages, isLoading, isConnected, activeSkill, sendMessage, clearMessages } =
    useRevitBridge();
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    scrollRef.current?.scrollTo({
      top: scrollRef.current.scrollHeight,
      behavior: 'smooth',
    });
  }, [messages, activeSkill]);

  return (
    <div className="flex h-screen flex-col bg-white">
      {/* Header */}
      <div className="flex items-center justify-between border-b border-gray-200 bg-revit-600 px-4 py-3">
        <div className="flex items-center gap-2">
          <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-white/20 text-white text-sm font-bold">
            M
          </div>
          <div>
            <h1 className="text-sm font-semibold text-white">MEP ChatBot</h1>
            <p className="text-[10px] text-revit-200">
              {isConnected ? 'Connected to Revit' : 'Standalone mode'}
            </p>
          </div>
        </div>
        <button
          onClick={clearMessages}
          className="rounded-lg px-2 py-1 text-xs text-revit-200 transition-colors hover:bg-white/10 hover:text-white"
        >
          Clear
        </button>
      </div>

      {/* Messages */}
      <div ref={scrollRef} className="flex-1 overflow-y-auto px-3 py-4">
        {messages.length === 0 && (
          <div className="flex h-full flex-col items-center justify-center text-center text-gray-400">
            <div className="mb-3 flex h-16 w-16 items-center justify-center rounded-2xl bg-revit-50 text-2xl">
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="h-8 w-8 text-revit-400">
                <path d="M4.913 2.658c2.075-.27 4.19-.408 6.337-.408 2.147 0 4.262.139 6.337.408 1.922.25 3.291 1.861 3.405 3.727a4.403 4.403 0 0 0-1.032-.211 50.89 50.89 0 0 0-8.42 0c-2.358.196-4.04 2.19-4.04 4.434v4.286a4.47 4.47 0 0 0 2.433 3.984L7.28 21.53A.75.75 0 0 1 6 21v-2.996a3.747 3.747 0 0 1-1.087-3.398V2.658Z" />
                <path d="M16.5 14.25a.75.75 0 0 0 0-1.5h-5.25a.75.75 0 0 0 0 1.5h5.25Z" />
              </svg>
            </div>
            <p className="text-sm font-medium">MEP ChatBot</p>
            <p className="mt-1 text-xs">Ask me anything about your MEP model</p>
          </div>
        )}

        {messages.map((msg) => (
          <MessageBubble key={msg.id} message={msg} />
        ))}

        {activeSkill && <SkillPanel skill={activeSkill} />}

        {isLoading && !activeSkill && (
          <div className="flex justify-start mb-3">
            <div className="rounded-2xl rounded-bl-md bg-gray-100 px-4 py-3">
              <div className="flex gap-1">
                <span className="h-2 w-2 animate-bounce rounded-full bg-gray-400" style={{ animationDelay: '0ms' }} />
                <span className="h-2 w-2 animate-bounce rounded-full bg-gray-400" style={{ animationDelay: '150ms' }} />
                <span className="h-2 w-2 animate-bounce rounded-full bg-gray-400" style={{ animationDelay: '300ms' }} />
              </div>
            </div>
          </div>
        )}
      </div>

      {/* Input */}
      <InputBar onSend={sendMessage} disabled={isLoading} />
    </div>
  );
}
