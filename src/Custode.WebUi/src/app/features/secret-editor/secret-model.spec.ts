// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import type { SecretValue } from '../../core/vault/vault-api.service';
import {
  diffVersions,
  emptyDraft,
  fileExtensionAllowed,
  fileSizeFromBase64,
  hasErrors,
  buildWriteBody,
  validateDraft,
} from './secret-model';

describe('secret model validation', () => {
  it('requires both username and password for a username/password secret', () => {
    const draft = { ...emptyDraft('username-password'), name: 'db', username: 'deploy' };

    const errors = validateDraft(draft, 'create', false);

    expect(errors).toEqual({ password: 'Enter a password.' });
    expect(hasErrors(errors)).toBe(true);
  });

  it('requires one value for a PAT or an API key', () => {
    expect(validateDraft({ ...emptyDraft('pat'), name: 'ci' }, 'create', false)).toEqual({ value: 'Enter a value.' });
    expect(validateDraft({ ...emptyDraft('api-key'), name: 'ci', value: 'k' }, 'create', false)).toEqual({});
  });

  it('rejects a structured field whose name already exists and keeps both rows marked', () => {
    const draft = {
      ...emptyDraft('structured'),
      name: 'smtp',
      fields: [
        { name: 'host', value: 'mail' },
        { name: 'host', value: 'other' },
      ],
    };

    const errors = validateDraft(draft, 'create', false);

    expect(errors['field-1-name']).toBe('This field name already exists.');
    expect(errors['field-0-name']).toBe('This field name already exists.');
  });

  it('rejects an empty key in a key-value secret', () => {
    const draft = { ...emptyDraft('key-value'), name: 'env', pairs: [{ name: '', value: 'x' }] };

    expect(validateDraft(draft, 'create', false)['pair-0-name']).toBe('Enter a key.');
  });

  it('requires a file only when none is selected or stored', () => {
    expect(validateDraft({ ...emptyDraft('file'), name: 'tls' }, 'create', false)).toEqual({ file: 'Select a certificate file.' });
    expect(validateDraft({ ...emptyDraft('file'), name: 'tls' }, 'edit', true)).toEqual({});
  });
});

describe('certificate file checks', () => {
  it.each(['server.pfx', 'ca.CRT', 'chain.pem', 'key.key', 'bundle.p12', 'root.cer'])('accepts %s', (name) => {
    expect(fileExtensionAllowed(name)).toBe(true);
  });

  it.each(['notes.txt', 'archive.zip', 'no-extension'])('rejects %s', (name) => {
    expect(fileExtensionAllowed(name)).toBe(false);
  });

  it('derives the stored file size from its base64 content', () => {
    expect(fileSizeFromBase64('AAAA')).toBe(3);
    expect(fileSizeFromBase64('AAA=')).toBe(2);
    expect(fileSizeFromBase64(null)).toBeNull();
  });
});

describe('write bodies', () => {
  it('keeps the order of key-value pairs and sends structured fields as an object', () => {
    const keyValue = buildWriteBody(
      { ...emptyDraft('key-value'), name: 'env', pairs: [{ name: 'B', value: '2' }, { name: 'A', value: '1' }] },
      null,
    );
    const structured = buildWriteBody(
      { ...emptyDraft('structured'), name: 'smtp', fields: [{ name: ' host ', value: 'mail' }] },
      null,
    );

    expect(keyValue.pairs).toEqual([
      { key: 'B', value: '2' },
      { key: 'A', value: '1' },
    ]);
    expect(structured.fields).toEqual({ host: 'mail' });
  });
});

describe('version diff', () => {
  function version(overrides: Partial<SecretValue>): SecretValue {
    return {
      id: 'id',
      name: 'db',
      type: 'username-password',
      folderPath: '/',
      fileName: null,
      username: 'deploy',
      password: 'one',
      value: null,
      contentBase64: null,
      fields: null,
      pairs: null,
      version: 1,
      ...overrides,
    };
  }

  it('marks a changed password and keeps the values for the view to hide', () => {
    const diff = diffVersions(version({ password: 'one' }), version({ password: 'two' }));

    expect(diff.find((entry) => entry.field === 'password')).toMatchObject({
      status: 'changed',
      before: 'one',
      after: 'two',
      revealable: true,
    });
    expect(diff.find((entry) => entry.field === 'username')?.status).toBe('unchanged');
  });

  it('reports fields added and removed between structured versions', () => {
    const before = version({ type: 'structured', fields: [{ key: 'host', value: 'a' }, { key: 'old', value: 'x' }] });
    const after = version({ type: 'structured', fields: [{ key: 'host', value: 'a' }, { key: 'new', value: 'y' }] });

    const statuses = Object.fromEntries(diffVersions(before, after).map((entry) => [entry.field, entry.status]));

    expect(statuses).toEqual({ host: 'unchanged', old: 'removed', new: 'added' });
  });

  it('never reveals certificate content', () => {
    const before = version({ type: 'file', username: null, password: null, fileName: 'a.pem', contentBase64: 'AAAA' });
    const after = version({ type: 'file', username: null, password: null, fileName: 'a.pem', contentBase64: 'BBBB' });

    const content = diffVersions(before, after).find((entry) => entry.field === 'file content');

    expect(content).toMatchObject({ status: 'changed', revealable: false });
  });
});
