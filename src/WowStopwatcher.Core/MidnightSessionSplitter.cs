namespace WowStopwatcher.Core;

/// <summary>Closes an open session at every local midnight it crossed and reopens it at that midnight.</summary>
internal sealed class MidnightSessionSplitter(ISessionRepository sessions)
{
    /// <summary>Returns the session that is open now (the original one when no midnight was crossed).</summary>
    public Session Split(Session session, DateTimeOffset now, DateOnly today, TimeZoneInfo zone)
    {
        while (LocalDays.ToLocalDate(session.StartUtc, zone) < today)
        {
            var boundary = LocalDays.StartOfDayUtc(LocalDays.ToLocalDate(session.StartUtc, zone).AddDays(1), zone);
            if (boundary <= session.StartUtc || boundary > now)
            {
                break;
            }

            sessions.Close(session.Id, boundary);
            var id = sessions.InsertOpen(session.StopwatchId, boundary);
            session = new Session(id, session.StopwatchId, boundary, null);
        }

        return session;
    }
}
