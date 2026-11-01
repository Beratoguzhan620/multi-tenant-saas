using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Helpdesk.Infrastructure.Tenancy;

/// <summary>
/// Tells PostgreSQL which organization the connection works for, every time EF Core opens one. The row-level
/// security policies compare each row's <c>tenant_id</c> with this setting, so even a query that forgets to
/// filter cannot read or write another organization's rows. With no organization the setting is empty and the
/// policies match nothing.
/// </summary>
public sealed class TenantConnectionInterceptor(TenantContext tenant) : DbConnectionInterceptor
{
    public const string SettingName = "app.tenant_id";

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await using var command = Command(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = Command(connection);
        command.ExecuteNonQuery();
    }

    private DbCommand Command(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = $"SELECT set_config('{SettingName}', @tenant, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant";
        parameter.Value = tenant.TenantId?.ToString() ?? string.Empty;
        command.Parameters.Add(parameter);
        return command;
    }
}
