import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from '../services/bridge';
import { ChatMessage, MessageTypes, SkillInfo } from '../types/messages';

let nextId = 1;
const genId = () => `msg-${nextId++}`;

export function useRevitBridge() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [activeSkill, setActiveSkill] = useState<SkillInfo | null>(null);
  const isConnected = bridge.isConnected;
  const pendingSkillRef = useRef<string | null>(null);

  useEffect(() => {
    const unsubscribe = bridge.onMessage((msg) => {
      switch (msg.type) {
        case MessageTypes.ASSISTANT_MESSAGE:
          setMessages((prev) => [
            ...prev,
            {
              id: genId(),
              role: 'assistant',
              content: msg.content ?? '',
              timestamp: Date.now(),
            },
          ]);
          setIsLoading(false);
          setActiveSkill(null);
          break;

        case MessageTypes.SKILL_EXECUTING:
          pendingSkillRef.current = msg.content ?? '';
          setActiveSkill({
            name: msg.content ?? '',
            status: 'executing',
          });
          break;

        case MessageTypes.SKILL_COMPLETED: {
          const data = msg.data as { success?: boolean; message?: string } | undefined;
          setActiveSkill({
            name: pendingSkillRef.current ?? msg.content ?? '',
            status: 'completed',
            result: {
              success: data?.success ?? true,
              message: data?.message ?? 'Done',
            },
          });
          pendingSkillRef.current = null;
          break;
        }

        case MessageTypes.ERROR:
          setMessages((prev) => [
            ...prev,
            {
              id: genId(),
              role: 'assistant',
              content: `Error: ${msg.content}`,
              timestamp: Date.now(),
            },
          ]);
          setIsLoading(false);
          setActiveSkill(null);
          break;
      }
    });

    return unsubscribe;
  }, []);

  const sendMessage = useCallback(
    (content: string) => {
      if (!content.trim() || isLoading) return;

      setMessages((prev) => [
        ...prev,
        {
          id: genId(),
          role: 'user',
          content: content.trim(),
          timestamp: Date.now(),
        },
      ]);

      setIsLoading(true);
      bridge.sendUserMessage(content.trim());
    },
    [isLoading]
  );

  const clearMessages = useCallback(() => {
    setMessages([]);
    setActiveSkill(null);
    setIsLoading(false);
  }, []);

  return {
    messages,
    isLoading,
    isConnected,
    activeSkill,
    sendMessage,
    clearMessages,
  };
}
