export interface BridgeMessage {
  type: string;
  content?: string;
  data?: Record<string, unknown>;
}

export interface ChatMessage {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: number;
  skillInfo?: SkillInfo;
}

export interface SkillInfo {
  name: string;
  status: 'executing' | 'completed' | 'failed';
  result?: {
    success: boolean;
    message: string;
  };
}

export const MessageTypes = {
  USER_MESSAGE: 'user_message',
  ASSISTANT_MESSAGE: 'assistant_message',
  SKILL_EXECUTING: 'skill_executing',
  SKILL_COMPLETED: 'skill_completed',
  ERROR: 'error',
  STREAM_CHUNK: 'stream_chunk',
  STREAM_END: 'stream_end',
  SETTINGS_UPDATE: 'settings_update',
} as const;
