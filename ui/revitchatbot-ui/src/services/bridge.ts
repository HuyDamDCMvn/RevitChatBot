import { BridgeMessage, MessageTypes, AutomationMode } from '../types/messages';

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: unknown) => void;
        addEventListener: (type: string, listener: (event: { data: unknown }) => void) => void;
        removeEventListener: (type: string, listener: (event: { data: unknown }) => void) => void;
      };
    };
  }
}

type MessageHandler = (message: BridgeMessage) => void;

class RevitBridge {
  private handlers: MessageHandler[] = [];
  private isWebView2 = false;

  constructor() {
    this.isWebView2 = !!window.chrome?.webview;

    if (this.isWebView2) {
      window.chrome!.webview!.addEventListener('message', (event) => {
        try {
          const message: BridgeMessage =
            typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
          this.handlers.forEach((h) => h(message));
        } catch {
          // ignore parse errors
        }
      });
    }
  }

  get isConnected(): boolean {
    return this.isWebView2;
  }

  send(message: BridgeMessage): void {
    if (this.isWebView2) {
      window.chrome!.webview!.postMessage(message);
    } else {
      this.simulateResponse(message);
    }
  }

  sendUserMessage(content: string): void {
    this.send({
      type: MessageTypes.USER_MESSAGE,
      content,
    });
  }

  updateSettings(data: Record<string, unknown>): void {
    this.send({
      type: MessageTypes.SETTINGS_UPDATE,
      data,
    });
  }

  requestHealthCheck(): void {
    this.send({ type: MessageTypes.HEALTH_CHECK });
  }

  requestModelInfo(): void {
    this.send({ type: MessageTypes.MODEL_INFO });
  }

  sendPartialInput(content: string): void {
    this.send({ type: 'partial_input', content });
  }

  requestCurrentSettings(): void {
    this.send({ type: MessageTypes.REQUEST_SETTINGS });
  }

  setAutomationMode(mode: AutomationMode): void {
    this.send({
      type: MessageTypes.AUTOMATION_MODE_CHANGED,
      content: mode,
    });
  }

  respondToActionPlan(approved: boolean): void {
    this.send({
      type: MessageTypes.ACTION_PLAN_APPROVAL,
      data: { approved },
    });
  }

  setMemoryConsent(granted: boolean): void {
    this.send({
      type: MessageTypes.MEMORY_CONSENT,
      data: { granted },
    });
  }

  requestMemoryStats(): void {
    this.send({ type: MessageTypes.MEMORY_STATS });
  }

  pullModel(modelName: string, setAsCodeGen = true): void {
    this.send({
      type: MessageTypes.MODEL_PULL_REQUEST,
      data: { modelName, setAsCodeGen },
    });
  }

  cancelPull(): void {
    this.send({ type: MessageTypes.MODEL_PULL_CANCEL });
  }

  setCodeGenModel(modelName: string): void {
    this.send({
      type: MessageTypes.CODEGEN_MODEL_SET,
      data: { modelName },
    });
  }

  onMessage(handler: MessageHandler): () => void {
    this.handlers.push(handler);
    return () => {
      this.handlers = this.handlers.filter((h) => h !== handler);
    };
  }

  private simulateResponse(message: BridgeMessage): void {
    if (message.type !== MessageTypes.USER_MESSAGE) return;

    setTimeout(() => {
      this.handlers.forEach((h) =>
        h({
          type: MessageTypes.ASSISTANT_MESSAGE,
          content:
            `[Dev Mode] Received: "${message.content}"\n\n` +
            'The chatbot is running in standalone mode. ' +
            'Connect to Revit to use MEP skills.',
        })
      );
    }, 500);
  }
}

export const bridge = new RevitBridge();
