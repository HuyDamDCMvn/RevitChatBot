import { useEffect, useRef, useState } from 'react';
import { useRevitBridge } from '../hooks/useRevitBridge';
import { AnnotationToolbar } from './AnnotationToolbar';
import { InputBar } from './InputBar';
import { MessageBubble } from './MessageBubble';
import { SettingsPanel } from './SettingsPanel';
import { SkillPanel } from './SkillPanel';

export function ChatWindow() {
  const {
    messages, isLoading, isConnected, activeSkill, thinkingText,
    activeModel, installedModels, sendMessage, clearMessages,
  } = useRevitBridge();
  const scrollRef = useRef<HTMLDivElement>(null);
  const [settingsOpen, setSettingsOpen] = useState(false);

  useEffect(() => {
    scrollRef.current?.scrollTo({
      top: scrollRef.current.scrollHeight,
      behavior: 'smooth',
    });
  }, [messages, activeSkill, thinkingText]);

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
              {isConnected
                ? activeModel
                  ? `Connected · ${activeModel}`
                  : 'Connected to Revit'
                : 'Standalone mode'}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-1">
          <button
            onClick={() => setSettingsOpen(true)}
            className="rounded-lg p-1.5 text-revit-200 transition-colors hover:bg-white/10 hover:text-white"
            title="Settings"
          >
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-4 w-4">
              <path fillRule="evenodd" d="M7.84 1.804A1 1 0 0 1 8.82 1h2.36a1 1 0 0 1 .98.804l.331 1.652a6.993 6.993 0 0 1 1.929 1.115l1.598-.54a1 1 0 0 1 1.186.447l1.18 2.044a1 1 0 0 1-.205 1.251l-1.267 1.113a7.047 7.047 0 0 1 0 2.228l1.267 1.113a1 1 0 0 1 .206 1.25l-1.18 2.045a1 1 0 0 1-1.187.447l-1.598-.54a6.993 6.993 0 0 1-1.929 1.115l-.33 1.652a1 1 0 0 1-.98.804H8.82a1 1 0 0 1-.98-.804l-.331-1.652a6.993 6.993 0 0 1-1.929-1.115l-1.598.54a1 1 0 0 1-1.186-.447l-1.18-2.044a1 1 0 0 1 .205-1.251l1.267-1.114a7.05 7.05 0 0 1 0-2.227L1.821 7.773a1 1 0 0 1-.206-1.25l1.18-2.045a1 1 0 0 1 1.187-.447l1.598.54A6.992 6.992 0 0 1 7.51 3.456l.33-1.652ZM10 13a3 3 0 1 0 0-6 3 3 0 0 0 0 6Z" clipRule="evenodd" />
            </svg>
          </button>
          <button
            onClick={clearMessages}
            className="rounded-lg px-2 py-1 text-xs text-revit-200 transition-colors hover:bg-white/10 hover:text-white"
          >
            Clear
          </button>
        </div>
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

        {thinkingText && (
          <div className="flex justify-start mb-3">
            <div className="max-w-[85%] rounded-2xl rounded-bl-md bg-purple-50 border border-purple-100 px-4 py-2.5 text-sm text-purple-700">
              <div className="flex items-center gap-2">
                <span className="inline-block h-2 w-2 animate-pulse rounded-full bg-purple-400" />
                <span className="font-medium text-xs text-purple-500">Thinking</span>
              </div>
              <p className="mt-1 text-xs text-purple-600 line-clamp-3 whitespace-pre-wrap">{thinkingText}</p>
            </div>
          </div>
        )}

        {isLoading && !activeSkill && !thinkingText && !messages.some((m) => m.streaming) && (
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

      {/* Annotation quick actions */}
      <AnnotationToolbar onSend={sendMessage} disabled={isLoading} />

      {/* Input */}
      <InputBar onSend={sendMessage} disabled={isLoading} />

      <SettingsPanel
        isOpen={settingsOpen}
        onClose={() => setSettingsOpen(false)}
        activeModel={activeModel}
        syncedModels={installedModels}
      />
    </div>
  );
}
