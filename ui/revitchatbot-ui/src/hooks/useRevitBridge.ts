import { useCallback, useEffect, useRef, useState } from 'react';
import { bridge } from '../services/bridge';
import {
  ActionPlanData,
  ChatMessage,
  CodeGenModelSuggestData,
  ImageAttachment,
  InstalledModelInfo,
  MessageTypes,
  ModelPullCompleteData,
  ModelPullProgressData,
  SkillInfo,
} from '../types/messages';

let nextId = 1;
const genId = () => `msg-${nextId++}`;

export function useRevitBridge() {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [activeSkill, setActiveSkill] = useState<SkillInfo | null>(null);
  const [thinkingText, setThinkingText] = useState<string | null>(null);
  const [activeModel, setActiveModel] = useState<string>('');
  const [installedModels, setInstalledModels] = useState<InstalledModelInfo[]>([]);
  const [codeGenSuggest, setCodeGenSuggest] = useState<CodeGenModelSuggestData | null>(null);
  const [pullProgress, setPullProgress] = useState<ModelPullProgressData | null>(null);
  const [isPulling, setIsPulling] = useState(false);
  const isConnected = bridge.isConnected;
  const pendingSkillRef = useRef<string | null>(null);
  const streamIdRef = useRef<string | null>(null);

  useEffect(() => {
    const unsubscribe = bridge.onMessage((msg) => {
      switch (msg.type) {
        case MessageTypes.MODEL_SYNC: {
          const modelName = msg.content ?? '';
          if (modelName) setActiveModel(modelName);
          const data = msg.data as { models?: InstalledModelInfo[] } | undefined;
          if (data?.models) setInstalledModels(data.models);
          break;
        }

        case MessageTypes.STREAM_CHUNK: {
          const chunk = msg.content ?? '';
          setThinkingText(null);
          if (!streamIdRef.current) {
            const id = genId();
            streamIdRef.current = id;
            setMessages((prev) => [
              ...prev,
              { id, role: 'assistant', content: chunk, timestamp: Date.now(), streaming: true },
            ]);
          } else {
            const sid = streamIdRef.current;
            setMessages((prev) =>
              prev.map((m) => (m.id === sid ? { ...m, content: m.content + chunk } : m))
            );
          }
          break;
        }

        case MessageTypes.STREAM_END: {
          setThinkingText(null);
          if (streamIdRef.current) {
            const sid = streamIdRef.current;
            const finalContent = msg.content ?? '';
            setMessages((prev) =>
              prev.map((m) => (m.id === sid ? { ...m, content: finalContent, streaming: false } : m))
            );
            streamIdRef.current = null;
          } else {
            setMessages((prev) => [
              ...prev,
              { id: genId(), role: 'assistant', content: msg.content ?? '', timestamp: Date.now() },
            ]);
          }
          setIsLoading(false);
          setActiveSkill(null);
          break;
        }

        case MessageTypes.ASSISTANT_MESSAGE:
          setThinkingText(null);
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

        case MessageTypes.AGENT_THINKING:
          setThinkingText(msg.content ?? 'Thinking...');
          break;

        case MessageTypes.AGENT_STEP: {
          const stepData = msg.data as { stepType?: string; skillName?: string } | undefined;
          if (stepData?.stepType === 'Answer') {
            setThinkingText(null);
          }
          break;
        }

        case MessageTypes.CLARIFICATION_REQUEST: {
          const clarData = msg.data as { options?: string[]; reason?: string } | undefined;
          setThinkingText(null);
          setMessages((prev) => [
            ...prev,
            {
              id: genId(),
              role: 'system',
              content: msg.content ?? '',
              timestamp: Date.now(),
              variant: 'clarification',
              clarificationOptions: clarData?.options,
            },
          ]);
          break;
        }

        case MessageTypes.ACTION_PLAN_REVIEW: {
          const planData = msg.data as ActionPlanData | undefined;
          setThinkingText(null);
          setMessages((prev) => [
            ...prev,
            {
              id: genId(),
              role: 'system',
              content: msg.content ?? 'Action plan pending review',
              timestamp: Date.now(),
              variant: 'action_plan',
              actionPlan: planData,
            },
          ]);
          break;
        }

        case MessageTypes.CONFIRMATION_REQUIRED:
          setThinkingText(null);
          setMessages((prev) => [
            ...prev,
            {
              id: genId(),
              role: 'system',
              content: msg.content ?? 'Confirmation required',
              timestamp: Date.now(),
              variant: 'confirmation',
            },
          ]);
          break;

        case MessageTypes.SKILL_EXECUTING:
          setThinkingText(null);
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

        case MessageTypes.VIEW_SNAPSHOT: {
          const snapData = msg.data as {
            base64?: string;
            mimeType?: string;
            caption?: string;
            analysis?: string;
          } | undefined;
          if (snapData?.base64) {
            const img: ImageAttachment = {
              base64: snapData.base64,
              mimeType: snapData.mimeType ?? 'image/png',
              caption: snapData.caption,
            };
            setThinkingText(null);
            setMessages((prev) => [
              ...prev,
              {
                id: genId(),
                role: 'assistant',
                content: snapData.analysis ?? snapData.caption ?? 'View snapshot',
                timestamp: Date.now(),
                images: [img],
              },
            ]);
          }
          break;
        }

        case MessageTypes.CODEGEN_MODEL_SUGGEST: {
          const suggestData = msg.data as CodeGenModelSuggestData | undefined;
          if (suggestData) setCodeGenSuggest(suggestData);
          break;
        }

        case MessageTypes.MODEL_PULL_PROGRESS: {
          const progressData = msg.data as ModelPullProgressData | undefined;
          if (progressData) {
            setPullProgress(progressData);
            setIsPulling(true);
          }
          break;
        }

        case MessageTypes.MODEL_PULL_COMPLETE: {
          const completeData = msg.data as ModelPullCompleteData | undefined;
          setPullProgress(null);
          setIsPulling(false);
          if (!completeData?.cancelled) {
            setCodeGenSuggest(null);
          }
          break;
        }

        case MessageTypes.ERROR:
          setThinkingText(null);
          if (streamIdRef.current) streamIdRef.current = null;
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
    setThinkingText(null);
    setIsLoading(false);
  }, []);

  const dismissCodeGenSuggest = useCallback(() => {
    setCodeGenSuggest(null);
  }, []);

  return {
    messages,
    isLoading,
    isConnected,
    activeSkill,
    thinkingText,
    activeModel,
    installedModels,
    codeGenSuggest,
    pullProgress,
    isPulling,
    sendMessage,
    clearMessages,
    dismissCodeGenSuggest,
  };
}
