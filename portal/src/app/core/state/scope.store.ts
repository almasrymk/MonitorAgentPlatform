import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';

export interface Workspace {
  id: string;
  name: string;
}

const WORKSPACE_KEY = 'mc.workspace';

/** Where the user is: the customer workspace opened by platform staff (sent as `X-Tenant-Id`). */
@Injectable({ providedIn: 'root' })
export class ScopeStore {
  private readonly storage = inject(DOCUMENT).defaultView?.sessionStorage;
  readonly workspace = signal<Workspace | null>(this.read());

  enterWorkspace(workspace: Workspace): void {
    this.workspace.set(workspace);
    this.write(workspace);
  }

  leaveWorkspace(): void {
    this.workspace.set(null);
    this.write(null);
  }

  private read(): Workspace | null {
    try {
      const raw = this.storage?.getItem(WORKSPACE_KEY);
      return raw ? (JSON.parse(raw) as Workspace) : null;
    } catch {
      return null;
    }
  }

  private write(workspace: Workspace | null): void {
    try {
      if (workspace) {
        this.storage?.setItem(WORKSPACE_KEY, JSON.stringify(workspace));
      } else {
        this.storage?.removeItem(WORKSPACE_KEY);
      }
    } catch {
      // ignore
    }
  }
}
