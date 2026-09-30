/// <summary>
/// Asks logind for this session's LockedHint over the system bus. GNOME and KDE set it from their
/// lock screens; a locker that does not leaves it false, and polling carries on as though nobody
/// had locked anything. A process outside any login session, as one a user service started, has no
/// "auto" session to ask about, and <see cref="LockWatcher"/> logs that once.
/// </summary>
sealed class LoginSessionLock : ISessionLock
{
    DBusConnection? connection;

    public async ValueTask<bool?> IsLocked(Cancel cancel)
    {
        if (DBusAddress.System is not { } address)
        {
            return null;
        }

        try
        {
            if (connection is null)
            {
                connection = new(address);
                await connection.ConnectAsync();
            }

            var writer = connection.GetMessageWriter();
            writer.WriteMethodCallHeader("org.freedesktop.login1", "/org/freedesktop/login1/session/auto", "org.freedesktop.DBus.Properties", "Get", "ss");
            writer.WriteString("org.freedesktop.login1.Session");
            writer.WriteString("LockedHint");
            var message = writer.CreateMessage();
            writer.Dispose();
            return await connection.CallMethodAsync(message, ReadLocked);
        }
        catch
        {
            // Connected afresh on the next look, in case the bus restarted.
            connection?.Dispose();
            connection = null;
            throw;
        }
    }

    static bool? ReadLocked(Tmds.DBus.Protocol.Message message, object? state) =>
        message.GetBodyReader().ReadVariantValue().GetBool();
}
