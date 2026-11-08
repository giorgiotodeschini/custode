// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { CdkTree, CdkTreeNode, CdkTreeNodeDef, CdkTreeNodePadding, CdkTreeNodeToggle, FlatTreeControl } from '@angular/cdk/tree';
import { Component, inject, input, output, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { VaultApi } from '../../core/vault/vault-api.service';

export interface FolderNode {
  path: string;
  name: string;
  level: number;
}

const ROOT: FolderNode = { path: '/', name: '/', level: 0 };

/** Folder tree rooted at `/`. It shows only the folders the server returns for the caller. */
@Component({
  selector: 'app-folder-tree',
  imports: [CdkTree, CdkTreeNode, CdkTreeNodeDef, CdkTreeNodePadding, CdkTreeNodeToggle],
  templateUrl: './folder-tree.html',
})
export class FolderTree {
  private readonly api = inject(VaultApi);

  readonly selectedPath = input.required<string>();
  readonly folderSelected = output<string>();
  /** Inheritance per folder path; a folder mapped to false is shown as having broken inheritance. */
  readonly inheritance = input<ReadonlyMap<string, boolean>>(new Map());

  protected readonly nodes = signal<FolderNode[]>([ROOT]);
  protected readonly open = signal<ReadonlySet<string>>(new Set());

  protected readonly treeControl = new FlatTreeControl<FolderNode, string>(
    (node) => node.level,
    () => true,
    { trackBy: (node) => node.path },
  );

  private readonly loads = new Set<Promise<void>>();

  constructor() {
    this.treeControl.expansionModel.changed.subscribe((change) => {
      for (const path of change.added) {
        this.setOpen(path, true);
        const node = this.nodes().find((candidate) => candidate.path === path);
        if (node) {
          const load = this.loadChildren(node);
          this.loads.add(load);
          void load.finally(() => this.loads.delete(load));
        }
      }
      for (const path of change.removed) {
        this.setOpen(path, false);
        this.removeDescendants(path);
      }
    });
    this.treeControl.expand(ROOT);
  }

  /** Reloads the tree from the server, keeping the folders that were open. */
  async reload(): Promise<void> {
    const openPaths = [...this.open()].sort((a, b) => depth(a) - depth(b));
    this.treeControl.collapseAll();
    this.nodes.set([ROOT]);
    this.open.set(new Set());
    this.treeControl.expand(ROOT);
    await this.settle();
    for (const path of openPaths) {
      const node = this.nodes().find((candidate) => candidate.path === path);
      if (node) {
        this.treeControl.expand(node);
        await this.settle();
      }
    }
  }

  protected select(node: FolderNode): void {
    this.folderSelected.emit(node.path);
  }

  private async settle(): Promise<void> {
    while (this.loads.size > 0) {
      await Promise.all([...this.loads]);
    }
  }

  private async loadChildren(parent: FolderNode): Promise<void> {
    let children: FolderNode[] = [];
    try {
      const entries = await firstValueFrom(this.api.listFolders(parent.path));
      children = entries.map((entry) => ({
        path: entry.path,
        name: lastSegment(entry.path),
        level: parent.level + 1,
      }));
    } catch {
      children = [];
    }
    this.nodes.update((current) => {
      const index = current.findIndex((node) => node.path === parent.path);
      if (index < 0) {
        return current;
      }
      const kept = current.filter((node) => !isDescendantPath(node.path, parent.path));
      const at = kept.findIndex((node) => node.path === parent.path);
      return [...kept.slice(0, at + 1), ...children, ...kept.slice(at + 1)];
    });
  }

  private removeDescendants(parentPath: string): void {
    this.nodes.update((current) => current.filter((node) => !isDescendantPath(node.path, parentPath)));
    for (const path of [...this.open()]) {
      if (isDescendantPath(path, parentPath)) {
        this.setOpen(path, false);
      }
    }
  }

  private setOpen(path: string, value: boolean): void {
    this.open.update((current) => {
      const next = new Set(current);
      if (value) {
        next.add(path);
      } else {
        next.delete(path);
      }
      return next;
    });
  }
}

function depth(path: string): number {
  return path.split('/').filter((segment) => segment.length > 0).length;
}

function lastSegment(path: string): string {
  return path.split('/').filter((segment) => segment.length > 0).pop() ?? '/';
}

function isDescendantPath(path: string, ancestorPath: string): boolean {
  const prefix = ancestorPath === '/' ? '/' : `${ancestorPath}/`;
  return path !== ancestorPath && path.startsWith(prefix);
}
