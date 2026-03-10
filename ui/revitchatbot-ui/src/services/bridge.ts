import { BridgeMessage, MessageTypes } from '../types/messages';

declare global {
  interface Window {
    chrome?: {
      webview?: {
        postMessage: (message: string) => void;
        addEventListener: (type: string, listener: (event: { data: string }) => void) => void;
        removeEventListener: (type: string, listener: (event: { data: string }) => void) => void;
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
          const message: BridgeMessage = JSON.parse(event.data);
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
      window.chrome!.webview!.postMessage(JSON.stringify(message));
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
