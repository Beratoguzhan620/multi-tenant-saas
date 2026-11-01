namespace Helpdesk.Infrastructure.Tenancy;

/// <summary>
/// The SQL behind the second layer of isolation, used by migrations. The API connects as
/// <see cref="AppRole"/>, which owns nothing, is not a superuser and cannot bypass row-level security; migrations
/// run as the owner.
/// </summary>
public static class TenantIsolationSql
{
    public const string AppRole = "helpdesk_app";

    /// <summary>
    /// Creates the application role if the deployment has not (without a password, so it cannot sign in until the
    /// deployment gives it one) and lets it work with every table, now and later.
    /// </summary>
    public const string GrantAppRole = $"""
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '{AppRole}') THEN
                CREATE ROLE {AppRole} NOLOGIN;
            END IF;
        END
        $$;
        GRANT USAGE ON SCHEMA public TO {AppRole};
        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole};
        GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole};
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {AppRole};
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO {AppRole};
        """;

    /// <summary>Only rows of the connection's organization can be read, and only such rows can be written.</summary>
    public static string Enable(string table) => $"""
        ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON {table}
            USING (tenant_id = NULLIF(current_setting('{TenantConnectionInterceptor.SettingName}', true), '')::uuid)
            WITH CHECK (tenant_id = NULLIF(current_setting('{TenantConnectionInterceptor.SettingName}', true), '')::uuid);
        """;

    public static string Disable(string table) => $"""
        DROP POLICY IF EXISTS tenant_isolation ON {table};
        ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;
        """;
}
