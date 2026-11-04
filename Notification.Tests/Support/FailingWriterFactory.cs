using Npgsql;

namespace Notification.Tests.Support;

/// <summary>A writer that cannot open a transaction, as when the app is not allowed to write to its database.</summary>
public sealed class FailingWriterFactory : IWriterConnectionFactory
{
    /// <inheritdoc />
    public Task<IWriterConnection> OpenWriteTransactionAsync(string action, CancellationToken cancellationToken = default)
    {
        throw new NpgsqlException("permission denied for table known_bookings");
    }
}
