using System.Collections.Concurrent;

namespace DARI_API.AiSearch;

// Per-user rolling-window quota for AI search.
//
// Why server-side:
//   localStorage caps trivially get reset by clearing browser data, so
//   they're useless for abuse prevention. We track usage by JWT user-id
//   so the cap follows the account regardless of device.
//
// Why in-memory and not a DB table:
//   For the graduation phase the simplicity beats the persistence cost.
//   A backend restart resets quotas — that's acceptable: users get up to
//   3 fresh searches after each restart, which is a graduation-friendly
//   "soft reset" rather than a security hole. If/when this needs to
//   survive restarts and scale across multiple instances, swap the
//   ConcurrentDictionary for a SQL table or Redis sorted-set without
//   touching the controller.
//
// Concurrency: ConcurrentDictionary keyed by user-id; each value is a
// short list of recent UTC timestamps. Lock per-user during prune+record.
public class UserAiSearchQuota
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(7);
    private readonly ConcurrentDictionary<Guid, List<DateTime>> _usage = new();

    // Look at the user's current count without consuming a slot.
    // Returns (allowed, usedInWindow, soonest-reset-time).
    public (bool Allowed, int Used, DateTime ResetAtUtc) PeekStatus(Guid userId, int limit)
    {
        var list = _usage.GetOrAdd(userId, _ => new List<DateTime>());
        lock (list)
        {
            Prune(list);
            var resetAt = list.Count > 0 ? list[0].Add(Window) : DateTime.UtcNow.Add(Window);
            return (list.Count < limit, list.Count, resetAt);
        }
    }

    // Consume a slot. Should only be called AFTER the request succeeded so
    // failed calls don't count against the user.
    public void Record(Guid userId)
    {
        var list = _usage.GetOrAdd(userId, _ => new List<DateTime>());
        lock (list)
        {
            Prune(list);
            list.Add(DateTime.UtcNow);
        }
    }

    // Drop entries that fell out of the rolling window. Keeps the per-user
    // list bounded to the quota size (3) so memory stays tiny even at
    // 100k users.
    private static void Prune(List<DateTime> list)
    {
        var cutoff = DateTime.UtcNow.Subtract(Window);
        list.RemoveAll(t => t < cutoff);
    }
}
