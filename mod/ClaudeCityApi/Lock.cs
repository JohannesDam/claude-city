using System;
using System.Collections.Generic;
using System.Linq;

namespace ClaudeCityBridge
{
    /// <summary>
    /// Server-side build lock ("busy tone"). Several builders may think/plan at the same time, but only one may
    /// change the city at a time. Builders take the lock only for the actual change (POST /lock -> token), pass
    /// the token as ?lock=... on every build call (that also renews the lease) and release it (POST /unlock).
    /// Waiting is first-come-first-served: POST /lock returns a ticket + queue position; poll again with the ticket.
    /// A holder that stops calling loses the lock after its ttl; a waiter that stops polling drops out of the queue.
    /// Build calls without a token are allowed while nobody holds the lock (single implicit call), and answered
    /// 423 "busy" while somebody else does. Reads never need the lock. State lives in the plugin: a plugin reload
    /// or game restart frees it.
    /// </summary>
    public partial class Api
    {
        class LockHolder { public string Token, Who; public long Since, LastSeen; public int TtlMs; public int Calls; }
        class LockWaiter { public string Ticket, Who; public long Enqueued, LastPoll; }

        static LockHolder lockHolder;
        static readonly List<LockWaiter> lockQueue = new List<LockWaiter>();
        static long lockGrants, lockHeldMsTotal, lockWaitMsTotal, lockBusyReplies, lockExpired;
        static readonly long lockStatsSince = Environment.TickCount;
        static readonly Random lockRng = new Random();
        const int LockQueuePollTimeoutMs = 15000;

        // endpoints that change the map (geometry, zoning, objects); settings like taxes/budget/policies are not locked
        static readonly HashSet<string> LockedPaths = new HashSet<string> {
            "/road", "/zone", "/building", "/upgrade", "/bulldoze", "/buytile", "/roadupgrade", "/busstop", "/busline",
            "/nodeupgrade", "/roundabout", "/extension", "/area", "/area/delete", "/buildingaction",
        };

        static long Now => Environment.TickCount;

        static string NewToken() { lock (lockRng) return lockRng.Next().ToString("x8") + lockRng.Next().ToString("x8"); }

        static void LockHousekeeping()
        {
            var now = Now;
            if (lockHolder != null && now - lockHolder.LastSeen > lockHolder.TtlMs)
            {
                lockHeldMsTotal += lockHolder.LastSeen - lockHolder.Since;
                lockExpired++;
                lockHolder = null;
            }
            lockQueue.RemoveAll(w => now - w.LastPoll > LockQueuePollTimeoutMs);
        }

        object LockHolderInfo() => lockHolder == null ? null : new Dictionary<string, object> {
            ["who"] = lockHolder.Who, ["heldMs"] = Now - lockHolder.Since, ["idleMs"] = Now - lockHolder.LastSeen,
            ["calls"] = lockHolder.Calls, ["ttlMs"] = lockHolder.TtlMs };

        object LockQueueInfo() => lockQueue.Select((w, i) => (object)new Dictionary<string, object> {
            ["position"] = i + 1, ["who"] = w.Who, ["waitingMs"] = Now - w.Enqueued }).ToList();

        /// <summary>Called by Dispatch before every request. Returns an error for a build call while someone else holds the lock.</summary>
        object LockGate(Pending p)
        {
            LockHousekeeping();
            var token = p.Q("lock");
            bool mine = lockHolder != null && token != null && token == lockHolder.Token;
            if (mine) lockHolder.LastSeen = Now; // any call with the token renews the lease
            if (p.Method != "POST" || !LockedPaths.Contains(p.Path)) return null;
            if (lockHolder == null) return null; // free: implicit single call
            if (mine) { lockHolder.Calls++; return null; }
            lockBusyReplies++;
            var err = new ApiError("busy: the build lock is held by " + lockHolder.Who + " (POST /lock to queue)", 423);
            err["busy"] = true;
            err["holder"] = LockHolderInfo();
            err["queueLength"] = lockQueue.Count;
            return err;
        }

        /// <summary>POST /lock {who, ttl (s, default 90), ticket?, token?, noQueue?} -> {granted, token} or {granted:false, ticket, position, holder}.</summary>
        object LockAcquire(Pending p)
        {
            LockHousekeeping();
            var who = p.Body.Str("who") ?? p.Q("who") ?? "anonymous";
            var ttlMs = (int)(Math.Max(10, Math.Min(600, p.Body.F("ttl", 90))) * 1000);
            var token = p.Body.Str("token") ?? p.Q("lock");
            var ticket = p.Body.Str("ticket");
            var now = Now;

            if (lockHolder != null && token != null && token == lockHolder.Token)
            {
                lockHolder.LastSeen = now;
                lockHolder.TtlMs = ttlMs;
                return new Dictionary<string, object> { ["granted"] = true, ["token"] = token, ["renewed"] = true };
            }

            LockWaiter me = ticket == null ? null : lockQueue.FirstOrDefault(w => w.Ticket == ticket);
            if (me != null) me.LastPoll = now;
            bool headOfQueue = lockQueue.Count == 0 || lockQueue[0] == me;

            if (lockHolder == null && headOfQueue)
            {
                if (me != null) { lockQueue.Remove(me); lockWaitMsTotal += now - me.Enqueued; }
                lockHolder = new LockHolder { Token = NewToken(), Who = who, Since = now, LastSeen = now, TtlMs = ttlMs };
                lockGrants++;
                return new Dictionary<string, object> { ["granted"] = true, ["token"] = lockHolder.Token, ["waitedMs"] = me == null ? 0 : now - me.Enqueued };
            }

            if (me == null && (p.Body.B("noQueue") || p.Q("noQueue") == "true"))
                return new Dictionary<string, object> { ["granted"] = false, ["holder"] = LockHolderInfo(), ["queueLength"] = lockQueue.Count };
            if (me == null)
            {
                me = new LockWaiter { Ticket = NewToken(), Who = who, Enqueued = now, LastPoll = now };
                lockQueue.Add(me);
            }
            return new Dictionary<string, object> {
                ["granted"] = false, ["ticket"] = me.Ticket, ["position"] = lockQueue.IndexOf(me) + 1,
                ["holder"] = LockHolderInfo(), ["queueLength"] = lockQueue.Count };
        }

        /// <summary>POST /unlock {token} -> {released, queueLength} (queueLength &gt; 0 = someone waits: no need to resume the game).</summary>
        object LockRelease(Pending p)
        {
            LockHousekeeping();
            var token = p.Body.Str("token") ?? p.Q("lock");
            bool released = false;
            if (lockHolder != null && token == lockHolder.Token)
            {
                lockHeldMsTotal += Now - lockHolder.Since;
                lockHolder = null;
                released = true;
            }
            return new Dictionary<string, object> { ["released"] = released, ["queueLength"] = lockQueue.Count };
        }

        /// <summary>GET /lock: who builds now, who waits, and utilisation since the plugin loaded.</summary>
        object LockStatus()
        {
            LockHousekeeping();
            var span = Math.Max(1, Now - lockStatsSince);
            var held = lockHeldMsTotal + (lockHolder == null ? 0 : Now - lockHolder.Since);
            return new Dictionary<string, object> {
                ["busy"] = lockHolder != null, ["holder"] = LockHolderInfo(), ["queue"] = LockQueueInfo(),
                ["stats"] = new Dictionary<string, object> {
                    ["sinceMin"] = Math.Round(span / 60000.0, 1), ["grants"] = lockGrants,
                    ["utilisation"] = Math.Round((double)held / span, 3),
                    ["avgWaitMs"] = lockGrants == 0 ? 0 : lockWaitMsTotal / lockGrants,
                    ["avgHoldMs"] = lockGrants == 0 ? 0 : held / lockGrants,
                    ["busyReplies"] = lockBusyReplies, ["expired"] = lockExpired },
                ["lockedPaths"] = LockedPaths.OrderBy(s => s).ToList(),
            };
        }
    }
}
