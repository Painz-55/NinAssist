using System.Collections.Frozen;
using System.Text.Json;
using NinOnlineAutoAssist.Core.Abstractions;

namespace NinOnlineAutoAssist.Core.Security;

public enum AppFeature { AutoTurning, SubPunishLegacy, SubPunishNew, AutoAttack, Tool, Follow, Stamina, MapPlayers, ResourceInspection, AutoCastTurn }

public sealed class Entitlements
{
    private readonly FrozenSet<AppFeature> _features;
    public DateTimeOffset? ExpiresAt { get; }
    public Entitlements(IEnumerable<AppFeature> features, DateTimeOffset? expiresAt = null)
    { _features = features.ToFrozenSet(); ExpiresAt = expiresAt; }
    public bool Allows(AppFeature feature) => _features.Contains(feature);

    public static Entitlements Parse(JsonElement document)
    {
        try
        {
            var fields = document.GetProperty("fields");
            if (!fields.GetProperty("enabled").GetProperty("booleanValue").GetBoolean())
                throw new AccessDeniedException("Conta desativada pelo administrador.");
            var map = fields.GetProperty("features").GetProperty("mapValue");
            var granted = new List<AppFeature>();
            if (map.TryGetProperty("fields", out var permissions))
            {
                foreach (var property in permissions.EnumerateObject())
                {
                    var value = property.Value.GetProperty("booleanValue").GetBoolean();
                    if (Enum.TryParse<AppFeature>(property.Name, false, out var feature) && Enum.IsDefined(feature) &&
                        feature.ToString() == property.Name && value) granted.Add(feature);
                }
            }
            DateTimeOffset? expires = null;
            if (fields.TryGetProperty("expiresAt", out var expiry))
                expires = expiry.GetProperty("timestampValue").GetDateTimeOffset();
            return new(granted, expires);
        }
        catch (AccessDeniedException) { throw; }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException or ArgumentException)
        { throw new AccessDeniedException("Permissões ausentes ou inválidas; contate o administrador."); }
    }
}

public sealed class AccessDeniedException(string message) : Exception(message);
public sealed class FirebaseUnavailableException() : Exception("Não foi possível validar a sessão no Firebase. Verifique a conexão e entre novamente.");

/// <summary>Monotonic, immutable snapshots; never grants access from disk or before a server check.</summary>
public sealed class AccessLease(IMonotonicClock clock)
{
    private readonly object _sync = new();
    private bool _closed;
    private sealed record Grant(Entitlements Entitlements, long Start, long Duration);
    private Grant? _grant;
    public bool IsValid => Current is not null;
    private Grant? Current
    {
        get
        {
            var grant = Volatile.Read(ref _grant);
            var now = clock.NowMs;
            return grant is not null && now >= grant.Start && now - grant.Start < grant.Duration ? grant : null;
        }
    }
    public bool Allows(AppFeature feature) => Current?.Entitlements.Allows(feature) == true;
    public void Revoke() { lock (_sync) Volatile.Write(ref _grant, null); }
    public void Close() { lock (_sync) { _closed = true; Volatile.Write(ref _grant, null); } }
    public void Apply(Entitlements entitlements, long requestStartedMs, DateTimeOffset serverTime)
    {
        lock (_sync)
        {
            if (_closed) throw new AccessDeniedException("Sessão encerrada.");
            var duration = 75_000L;
            if (entitlements.ExpiresAt is { } expiry)
                duration = (long)Math.Min(duration, Math.Max(0, (expiry - serverTime).TotalMilliseconds - 2000));
            if (duration <= 0 || clock.NowMs < requestStartedMs || clock.NowMs - requestStartedMs >= duration)
            { Revoke(); throw new AccessDeniedException("Autorização expirada; contate o administrador."); }
            Volatile.Write(ref _grant, new(entitlements, requestStartedMs, duration));
        }
    }
}
