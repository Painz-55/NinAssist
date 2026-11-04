using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Core.Security;

/// <summary>Refreshes Auth AND entitlements every 30 seconds. Any validation failure revokes access.</summary>
public sealed class FirebaseSession : IAsyncDisposable
{
    private readonly FirebaseClient _client;
    private readonly IMonotonicClock _clock;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _serial = new(1, 1);
    private FirebaseIdentity? _identity;
    private Task? _monitor;
    private bool _presenceCreated;
    private readonly string _sessionId = Guid.NewGuid().ToString("N");
    private readonly string _version;
    private readonly object _charactersLock = new();
    private readonly Queue<CharacterUsageChange> _characters = new();
    private string _lastObservedCharacter = "";
    private long _characterSequence, _lastValidationAtMs;
    private string _usageStatus = "";
    public bool UsageExempt => FirebaseUsagePolicy.IsExempt(Email);
    public int PendingCharacterChanges { get { lock (_charactersLock) return _characters.Count; } }
    public AccessLease Access { get; }
    public string Email { get; }
    public string Uid { get; }
    private string _status = "Sessão validada";
    public string Status => Volatile.Read(ref _status) + (UsageExempt ? " · registros de uso dispensados"
        : Volatile.Read(ref _usageStatus) is { Length: > 0 } usage ? " · " + usage : "");
    private FirebaseSession(FirebaseClient client, FirebaseIdentity identity, IMonotonicClock clock, string version)
    {
        _client = client; _identity = identity; _clock = clock; _version = version;
        Email = identity.Email; Uid = identity.Uid; Access = new(clock);
    }
    public static async Task<FirebaseSession> LoginAsync(FirebaseClient client, string email, string password,
        IMonotonicClock clock, string version, CancellationToken cancellation = default)
    {
        var identity = await client.SignInAsync(email, password, cancellation).ConfigureAwait(false);
        var session = new FirebaseSession(client, identity, clock, version);
        try
        {
            await session.ValidateAsync(false, cancellation).ConfigureAwait(false);
            session._monitor = session.MonitorAsync();
            return session;
        }
        catch { session.Access.Revoke(); session._identity = null; session._stop.Dispose(); session._serial.Dispose(); throw; }
    }

    // Serialized with shutdown; no delayed response may restore a session after logout.
    public async Task ValidateAsync(bool refresh = true, CancellationToken cancellation = default)
    {
        await _serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (_stop.IsCancellationRequested || _identity is null) throw new AccessDeniedException("Sessão encerrada.");
            var started = _clock.NowMs;
            if (refresh) _identity = await _client.RefreshAsync(_identity, cancellation).ConfigureAwait(false);
            var (permissions, serverTime) = await _client.ReadEntitlementsAsync(_identity, cancellation).ConfigureAwait(false);
            if (_stop.IsCancellationRequested) throw new AccessDeniedException("Sessão encerrada.");
            Access.Apply(permissions, started, serverTime);
            _lastValidationAtMs = _clock.NowMs;
            Volatile.Write(ref _status, "Sessão validada · verificação a cada 30s");
            if (!UsageExempt)
            {
                using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                budget.CancelAfter(TimeSpan.FromSeconds(3));
                try
                {
                    await EnsurePresenceAsync(budget.Token).ConfigureAwait(false);
                    await FlushCharactersLockedAsync(budget.Token).ConfigureAwait(false);
                    Volatile.Write(ref _usageStatus, "");
                }
                catch (Exception ex) when (ex is FirebaseUnavailableException or AccessDeniedException ||
                    ex is OperationCanceledException && !cancellation.IsCancellationRequested)
                { UsageFailed(); }
            }
        }
        catch
        {
            Access.Revoke();
            Volatile.Write(ref _status, "Sessão não validada; entre novamente");
            throw;
        }
        finally { _serial.Release(); }
    }
    private async Task MonitorAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
            while (await timer.WaitForNextTickAsync(_stop.Token).ConfigureAwait(false))
            {
                if (_clock.NowMs - _lastValidationAtMs >= 30_000)
                    await ValidateAsync(true, _stop.Token).ConfigureAwait(false);
                else if (PendingCharacterChanges > 0)
                    await FlushCharacterUsageAsync(_stop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch { Access.Revoke(); }
    }

    /// <summary>Call only with a character confirmed by the live reader, never the editable connection textbox.</summary>
    public bool ObserveCharacter(string characterName)
    {
        if (UsageExempt || !CharacterUsageChange.ValidName(characterName)) return false;
        lock (_charactersLock)
        {
            if (_stop.IsCancellationRequested || !Access.IsValid || characterName == _lastObservedCharacter) return false;
            _characters.Enqueue(new(++_characterSequence, _lastObservedCharacter, characterName));
            _lastObservedCharacter = characterName;
        }
        Volatile.Write(ref _usageStatus, "personagem aguardando registro");
        return true;
    }

    private void UsageFailed() => Volatile.Write(ref _usageStatus, PendingCharacterChanges > 0
        ? $"registro de uso indisponível · {PendingCharacterChanges} alterações pendentes"
        : "registro de presença indisponível");

    private async Task EnsurePresenceAsync(CancellationToken cancellation)
    {
        await _client.WritePresenceAsync(_identity!, _sessionId, _version, !_presenceCreated, false, cancellation).ConfigureAwait(false);
        _presenceCreated = true;
    }

    private async Task FlushCharactersLockedAsync(CancellationToken cancellation)
    {
        while (true)
        {
            CharacterUsageChange? next;
            lock (_charactersLock) next = _characters.TryPeek(out var head) ? head : null;
            if (next is null) return;
            await _client.WriteCharacterChangeAsync(_identity!, _sessionId, next, cancellation).ConfigureAwait(false);
            lock (_charactersLock) _characters.Dequeue();
        }
    }

    public async Task FlushCharacterUsageAsync(CancellationToken cancellation = default)
    {
        await _serial.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            if (UsageExempt || _stop.IsCancellationRequested || !Access.IsValid || _identity is null || PendingCharacterChanges == 0) return;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            budget.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                if (!_presenceCreated) await EnsurePresenceAsync(budget.Token).ConfigureAwait(false);
                await FlushCharactersLockedAsync(budget.Token).ConfigureAwait(false);
                Volatile.Write(ref _usageStatus, "");
            }
            catch (Exception ex) when (ex is FirebaseUnavailableException or AccessDeniedException ||
                ex is OperationCanceledException && !cancellation.IsCancellationRequested)
            { UsageFailed(); }
        }
        finally { _serial.Release(); }
    }
    public void Revoke() { Access.Close(); _stop.Cancel(); }
    public async ValueTask DisposeAsync()
    {
        Revoke();
        if (_monitor is not null) await _monitor.ConfigureAwait(false);
        await _serial.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!UsageExempt && _identity is not null)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                try
                {
                    if (PendingCharacterChanges > 0)
                    {
                        if (!_presenceCreated) await EnsurePresenceAsync(timeout.Token).ConfigureAwait(false);
                        await FlushCharactersLockedAsync(timeout.Token).ConfigureAwait(false);
                    }
                    if (_presenceCreated) await _client.WritePresenceAsync(_identity, _sessionId, _version, false, true, timeout.Token).ConfigureAwait(false);
                }
                catch { /* Best effort; recent lastSeenAt is the source of online status. */ }
            }
            _identity = null;
        }
        finally { _serial.Release(); _serial.Dispose(); _stop.Dispose(); }
    }
}
