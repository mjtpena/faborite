/**
 * Faborite JavaScript/TypeScript SDK
 * Issue #127
 * 
 * Installation:
 *   npm install faborite-sdk
 * 
 * Usage:
 *   import { FaboriteClient } from 'faborite-sdk';
 *   const client = new FaboriteClient({ apiKey: 'your-api-key' });
 *   await client.triggerSync({ workspaceId: '...', lakehouseId: '...' });
 */

export interface FaboriteConfig {
  apiKey: string;
  baseUrl?: string;
}

export interface SyncRequest {
  workspaceId: string;
  lakehouseId: string;
  tables?: string[];
  sampleRows?: number;
}

export interface SyncResponse {
  syncId: string;
  status: string;
  message: string;
}

export class FaboriteClient {
  private apiKey: string;
  private baseUrl: string;

  constructor(config: FaboriteConfig) {
    this.apiKey = config.apiKey;
    this.baseUrl = config.baseUrl || 'https://api.faborite.com';
  }

  async triggerSync(request: SyncRequest): Promise<SyncResponse> {
    const response = await fetch(`${this.baseUrl}/api/sync`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${this.apiKey}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(request),
    });

    return await response.json();
  }

  async listTables(workspaceId: string, lakehouseId: string): Promise<string[]> {
    const response = await fetch(
      `${this.baseUrl}/api/tables?workspaceId=${workspaceId}&lakehouseId=${lakehouseId}`,
      {
        headers: {
          'Authorization': `Bearer ${this.apiKey}`,
        },
      }
    );

    return await response.json();
  }

  async getSyncStatus(syncId: string): Promise<any> {
    const response = await fetch(`${this.baseUrl}/api/sync/${syncId}`, {
      headers: {
        'Authorization': `Bearer ${this.apiKey}`,
      },
    });

    return await response.json();
  }

  async profileData(tableName: string): Promise<any> {
    const response = await fetch(`${this.baseUrl}/api/profile`, {
      method: 'POST',
      headers: {
        'Authorization': `Bearer ${this.apiKey}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ tableName }),
    });

    return await response.json();
  }

  // Streaming support with async iterators
  async *streamSyncProgress(syncId: string): AsyncGenerator<any> {
    const eventSource = new EventSource(
      `${this.baseUrl}/api/sync/${syncId}/stream`,
      { headers: { 'Authorization': `Bearer ${this.apiKey}` } }
    );

    yield* this.eventSourceToAsyncIterator(eventSource);
  }

  private async *eventSourceToAsyncIterator(eventSource: EventSource): AsyncGenerator<any> {
    const queue: any[] = [];
    let resolve: ((value: any) => void) | null = null;

    eventSource.onmessage = (event) => {
      const data = JSON.parse(event.data);
      if (resolve) {
        resolve(data);
        resolve = null;
      } else {
        queue.push(data);
      }
    };

    while (true) {
      if (queue.length > 0) {
        yield queue.shift();
      } else {
        yield await new Promise((r) => (resolve = r));
      }
    }
  }
}

// Example usage
async function example() {
  const client = new FaboriteClient({ apiKey: 'test-key' });

  // Trigger sync
  const result = await client.triggerSync({
    workspaceId: 'workspace-123',
    lakehouseId: 'lakehouse-456',
  });
  console.log('Sync started:', result);

  // List tables
  const tables = await client.listTables('workspace-123', 'lakehouse-456');
  console.log('Tables:', tables);

  // Stream progress
  for await (const progress of client.streamSyncProgress(result.syncId)) {
    console.log('Progress:', progress);
  }
}

export default FaboriteClient;
