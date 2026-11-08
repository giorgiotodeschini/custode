// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

import { checkRows, duplicateInFile, neutralizeCell, parseCsv, parseCsvItems, toCsv } from './transfer-format';

describe('formula neutralization', () => {
  it.each([
    ['=SUM(A1)', "'=SUM(A1)"],
    ['+cmd', "'+cmd"],
    ['-1', "'-1"],
    ['@host', "'@host"],
    ['\tTAB', "'\tTAB"],
    ['\rCR', "'\rCR"],
    ['plain', 'plain'],
  ])('prefixes %j', (input, expected) => {
    expect(neutralizeCell(input)).toBe(expected);
  });

  it('is applied to every cell of an exported row', () => {
    const csv = toCsv([{ name: '=HYPERLINK("x")', type: 'pat' }], ['name', 'type']);

    expect(csv.split('\r\n')[1]).toBe(`"'=HYPERLINK(""x"")",pat`);
  });
});

describe('CSV parsing', () => {
  it('keeps commas, quotes and line breaks inside quoted cells', () => {
    const rows = parseCsv('name,value\r\n"a, b","say ""hi""\nthere"\r\n');

    expect(rows).toEqual([
      ['name', 'value'],
      ['a, b', 'say "hi"\nthere'],
    ]);
  });

  it('restores a neutralized name written by this application', () => {
    const items = parseCsvItems("folderPath,name,type\r\n/a,'=cell,pat\r\n");

    expect(items[0]?.name).toBe('=cell');
  });
});

describe('row checks', () => {
  it('reports an unsupported type with the row reason', () => {
    const rows = checkRows([{ name: 'x', type: 'pgp-key', folderPath: '/a', value: 'v' }]);

    expect(rows[0]?.errors[0]).toBe('Unsupported secret type "pgp-key".');
  });

  it('applies the form rules to a username/password row without a password', () => {
    const rows = checkRows([{ name: 'db', type: 'username-password', folderPath: '/a', username: 'u' }]);

    expect(rows[0]?.errors).toEqual(['Enter a password.']);
  });

  it('explains that a certificate cannot come from CSV', () => {
    const rows = checkRows([{ name: 'tls', type: 'file', folderPath: '/a', fileName: 'a.pem' }]);

    expect(rows[0]?.errors[0]).toContain('needs the JSON format');
  });

  it('finds a name repeated within the same folder', () => {
    const duplicates = duplicateInFile([
      { name: 'db', type: 'pat', folderPath: '/a', value: '1' },
      { name: 'db', type: 'pat', folderPath: '/b', value: '2' },
      { name: 'db', type: 'pat', folderPath: '/a', value: '3' },
    ]);

    expect([...duplicates]).toEqual([2]);
  });
});
