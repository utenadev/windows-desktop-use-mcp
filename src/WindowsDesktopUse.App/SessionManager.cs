using System.Collections.Concurrent;
using WindowsDesktopUse.Core;

namespace WindowsDesktopUse.App;

/// <summary>
/// Unified session manager for all visual and input operations
/// </summary>
public sealed class SessionManager : IDisposable
{
    private readonly ConcurrentDictionary<string, UnifiedSession> _sessions = new();
    private bool _disposed;

    /// <summary>
    /// Register a new session
    /// </summary>
    public string RegisterSession(UnifiedSession session)
    {
        var sessionId = Guid.NewGuid().ToString();
        session.Id = sessionId;
        _sessions.TryAdd(sessionId, session);
        Console.Error.WriteLine($"[SessionManager] Session registered: {sessionId}, Type: {session.Type}");
        return sessionId;
    }

    /// <summary>
    /// Get session by ID
    /// </summary>
    public UnifiedSession? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return session;
    }

    /// <summary>
    /// Stop and remove a session
    /// </summary>
    public bool StopSession(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            session.Cancel();
            session.Dispose();
            Console.Error.WriteLine($"[SessionManager] Session stopped: {sessionId}");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Stop all sessions of a specific type
    /// </summary>
    public int StopSessionsByType(SessionType type)
    {
        var toStop = _sessions.Where(s => s.Value.Type == type).Select(s => s.Key).ToList();
        int count = 0;
        foreach (var sessionId in toStop)
        {
            if (StopSession(sessionId))
                count++;
        }
        Console.Error.WriteLine($"[SessionManager] Stopped {count} sessions of type {type}");
        return count;
    }

    /// <summary>
    /// Stop all sessions
    /// </summary>
    public void StopAllSessions()
    {
        var allSessionIds = _sessions.Keys.ToList();
        foreach (var sessionId in allSessionIds)
        {
            StopSession(sessionId);
        }
        Console.Error.WriteLine("[SessionManager] All sessions stopped");
    }

    /// <summary>
    /// List all active sessions
    /// </summary>
    public IReadOnlyCollection<UnifiedSession> GetActiveSessions()
    {
        return _sessions.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// List sessions by type
    /// </summary>
    public IReadOnlyCollection<UnifiedSession> GetSessionsByType(SessionType type)
    {
        return _sessions.Values.Where(s => s.Type == type).ToList().AsReadOnly();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopAllSessions();
            _disposed = true;
        }
    }
}

/// <summary>
/// Unified session type
/// </summary>
public enum SessionType
{
    Watch,
    Capture,
    Audio,
    Monitor
}

/// <summary>
/// Unified session information
/// </summary>
public class UnifiedSession : IDisposable
{
    public string Id { get; set; } = "";
    public SessionType Type { get; set; }
    public string Target { get; set; } = "";
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public CancellationTokenSource Cts { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    private bool _disposed;

    public void Cancel()
    {
        if (!Cts.IsCancellationRequested)
        {
            Cts.Cancel();
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            Cancel();
            Cts.Dispose();
            _disposed = true;
        }
    }
}
