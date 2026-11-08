// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

export const TENANT_ADMINISTRATOR_ROLE = 'TenantAdministrator';

export interface SessionClaims {
  subject: string | null;
  tenantId: string | null;
  sessionId: string | null;
  roles: readonly string[];
}

export function isTenantAdministrator(claims: SessionClaims | null): boolean {
  return claims?.roles.includes(TENANT_ADMINISTRATOR_ROLE) ?? false;
}

/**
 * Reads the claims of an access token for shaping the UI only. The payload is not verified
 * here; every decision that matters is taken by the server.
 */
export function decodeAccessTokenClaims(token: string): SessionClaims {
  const payload = decodePayload(token);
  return {
    subject: stringClaim(payload['sub']),
    tenantId: stringClaim(payload['tenant_id']),
    sessionId: stringClaim(payload['sid']),
    roles: roleClaims(payload['role']),
  };
}

function decodePayload(token: string): Record<string, unknown> {
  const segment = token.split('.')[1];
  if (!segment) {
    return {};
  }
  try {
    const base64 = segment.replace(/-/g, '+').replace(/_/g, '/');
    const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), '=');
    const parsed: unknown = JSON.parse(atob(padded));
    return typeof parsed === 'object' && parsed !== null ? (parsed as Record<string, unknown>) : {};
  } catch {
    return {};
  }
}

function stringClaim(value: unknown): string | null {
  return typeof value === 'string' ? value : null;
}

function roleClaims(value: unknown): readonly string[] {
  if (typeof value === 'string') {
    return [value];
  }
  if (Array.isArray(value)) {
    return value.filter((role): role is string => typeof role === 'string');
  }
  return [];
}
