// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

export interface BrowserStorageWrite {
  target: 'localStorage' | 'sessionStorage' | 'cookie';
  key: string;
  value: string;
}

/** Only UI preferences may be written to browser storage (webui-platform). */
export function isAllowedStorageKey(key: string): boolean {
  return key === 'custode.theme' || /^custode\.secrets\.presentation\.[A-Za-z0-9-]+$/.test(key);
}

/**
 * Records every write to local storage, session storage, and document cookies
 * until the returned function is called. Use it in tests of any service that
 * touches the browser storage.
 */
export function recordBrowserStorageWrites(): { writes: BrowserStorageWrite[]; restore: () => void } {
  const writes: BrowserStorageWrite[] = [];
  const storageProto = Object.getPrototypeOf(localStorage) as Storage;
  const originalSetItem = storageProto.setItem;
  const cookieDescriptor = Object.getOwnPropertyDescriptor(Document.prototype, 'cookie');
  if (!cookieDescriptor?.get || !cookieDescriptor.set) {
    throw new Error('document.cookie accessors are not available in this environment');
  }

  storageProto.setItem = function (this: Storage, key: string, value: string): void {
    writes.push({
      target: this === localStorage ? 'localStorage' : 'sessionStorage',
      key,
      value: String(value),
    });
    originalSetItem.call(this, key, value);
  };

  Object.defineProperty(document, 'cookie', {
    configurable: true,
    get(): string {
      return cookieDescriptor.get!.call(document);
    },
    set(value: string) {
      writes.push({ target: 'cookie', key: String(value).split('=')[0], value: String(value) });
      cookieDescriptor.set!.call(document, value);
    },
  });

  return {
    writes,
    restore: () => {
      storageProto.setItem = originalSetItem;
      delete (document as unknown as Record<string, unknown>)['cookie'];
    },
  };
}
