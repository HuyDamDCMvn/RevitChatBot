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
  thinking?: string;
  skillInfo?: SkillInfo;
  streaming?: boolean;
  variant?: 'thinking' | 'clarification' | 'action_plan' | 'confirmation';
  clarificationOptions?: string[];
  actionPlan?: ActionPlanData;
  images?: ImageAttachment[];
}

export interface ImageAttachment {
  base64: string;
  mimeType?: string;
  caption?: string;
}

export interface ChartDataItem {
  name: string;
  value: number;
  [key: string]: unknown;
}

export interface SkillInfo {
  name: string;
  status: 'executing' | 'completed' | 'failed';
  result?: {
    success: boolean;
    message: string;
  };
}

export interface HealthStatus {
  available: boolean;
  runningModels?: RunningModelInfo[];
  installedModels?: InstalledModelInfo[];
  error?: string;
}

export interface RunningModelInfo {
  name: string;
  parameterSize: string;
  quantization: string;
  sizeMB: number;
  vramMB: number;
  expiresAt: string;
}

export interface InstalledModelInfo {
  name: string;
  parameterSize: string;
  quantization: string;
  sizeMB: number;
}

export interface ModelInfoResponse {
  modelName?: string;
  contextLength?: number;
  family?: string;
  parameterSize?: string;
  quantization?: string;
  error?: string;
}

export type AutomationMode = 'SuggestOnly' | 'PlanAndApprove' | 'AutoExecute';

export interface PlannedAction {
  toolName: string;
  description: string;
  isDestructive: boolean;
  arguments: Record<string, unknown>;
}

export interface ActionPlanData {
  actions: PlannedAction[];
  riskLevel: string;
  estimatedElementsAffected: number;
}

export interface ContextSnapshot {
  view?: string;
  viewType?: string;
  selectionCount?: number;
  categories?: string[];
  automationMode: AutomationMode;
}

export interface MemoryStatsData {
  shortTermEntries: number;
  longTermEntries: number;
  consentGranted: boolean;
  oldestLongTermUtc?: string;
  nextExpiryUtc?: string;
}

export interface WarningsDeltaData {
  warningsBefore: number;
  warningsAfter: number;
  delta: number;
  newWarnings: string[];
  resolvedWarnings: string[];
  isRegression: boolean;
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
  AGENT_THINKING: 'agent_thinking',
  AGENT_STEP: 'agent_step',
  CONFIRMATION_REQUIRED: 'confirmation_required',
  CONFIRMATION_RESPONSE: 'confirmation_response',
  CLARIFICATION_REQUEST: 'clarification_request',
  CLARIFICATION_RESPONSE: 'clarification_response',
  PARTIAL_INTENT: 'partial_intent',
  HEALTH_CHECK: 'health_check',
  HEALTH_STATUS: 'health_status',
  MODEL_INFO: 'model_info',
  MODEL_INFO_RESPONSE: 'model_info_response',
  AUTOMATION_MODE_CHANGED: 'automation_mode_changed',
  ACTION_PLAN_REVIEW: 'action_plan_review',
  ACTION_PLAN_APPROVAL: 'action_plan_approval',
  WARNINGS_DELTA: 'warnings_delta',
  MEMORY_CONSENT: 'memory_consent',
  MEMORY_STATS: 'memory_stats',
  VISION_ANALYSIS: 'vision_analysis',
  CONTEXT_SNAPSHOT: 'context_snapshot',
  VIEW_SNAPSHOT: 'view_snapshot',
  MODEL_SYNC: 'model_sync',
  REQUEST_SETTINGS: 'request_settings',
} as const;
