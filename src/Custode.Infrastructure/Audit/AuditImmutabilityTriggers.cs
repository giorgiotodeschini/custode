// Copyright (c) 2026 [Giorgio Todeschini]. All rights reserved.
// Licensed under the AGPL-3.0 License. See LICENSE file in the project root for full license information.

namespace Custode.Infrastructure.Audit;

public static class AuditImmutabilityTriggers
{
    public static readonly string[] PostgreSqlUp =
    [
        """
        CREATE OR REPLACE FUNCTION custode_reject_audit_change() RETURNS trigger AS $$
        BEGIN
            RAISE EXCEPTION 'audit entries are immutable';
        END;
        $$ LANGUAGE plpgsql;
        """,
        """CREATE TRIGGER audit_entries_reject_update BEFORE UPDATE ON "AuditEntries" FOR EACH ROW EXECUTE FUNCTION custode_reject_audit_change();""",
        """CREATE TRIGGER audit_entries_reject_delete BEFORE DELETE ON "AuditEntries" FOR EACH ROW EXECUTE FUNCTION custode_reject_audit_change();""",
    ];

    public static readonly string[] PostgreSqlDown =
    [
        """DROP TRIGGER IF EXISTS audit_entries_reject_update ON "AuditEntries";""",
        """DROP TRIGGER IF EXISTS audit_entries_reject_delete ON "AuditEntries";""",
        "DROP FUNCTION IF EXISTS custode_reject_audit_change();",
    ];

    public static readonly string[] SqliteUp =
    [
        """
        CREATE TRIGGER IF NOT EXISTS audit_entries_reject_update BEFORE UPDATE ON "AuditEntries"
        BEGIN
            SELECT RAISE(ABORT, 'audit entries are immutable');
        END;
        """,
        """
        CREATE TRIGGER IF NOT EXISTS audit_entries_reject_delete BEFORE DELETE ON "AuditEntries"
        BEGIN
            SELECT RAISE(ABORT, 'audit entries are immutable');
        END;
        """,
    ];

    public static readonly string[] SqliteDown =
    [
        "DROP TRIGGER IF EXISTS audit_entries_reject_update;",
        "DROP TRIGGER IF EXISTS audit_entries_reject_delete;",
    ];
}
