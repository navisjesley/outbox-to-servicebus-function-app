using Microsoft.Data.SqlClient;
using System.Data;

namespace ShopApp.Function.Services;

public sealed class SqlAppLockHandle : IAsyncDisposable
{
    private readonly SqlConnection _connection;
    private readonly string _lockName;
    private bool _disposed;

    public SqlAppLockHandle(SqlConnection connection, string lockName)
    {
        _connection = connection;
        _lockName = lockName;
    }

    public SqlConnection Connection => _connection;

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await using var releaseCommand = new SqlCommand(
                "EXEC sp_releaseapplock @Resource = @Resource, @LockOwner = 'Session';",
                _connection);

            releaseCommand.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, 255){Value = _lockName});
            await releaseCommand.ExecuteNonQueryAsync();
        }
        catch
        {
            // Intentionally swallowed. We still want to close the connection.
        }
        finally
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
    }
}