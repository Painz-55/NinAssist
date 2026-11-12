using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;

namespace NinOnlineAutoAssist.Live;

/// <summary>Send a needed turn BEFORE an automated combo key, then require a fresh facing observation.</summary>
public sealed class ComboFacingCoordinator
{
    private readonly IOrientationController _orientation;
    private readonly IMonotonicClock _clock;
    private readonly Func<LiveFrame?> _latest;
    private readonly Func<bool> _turnAllowed;
    private readonly DirectionCalculatorOptions _geometry;
    private readonly IAutoAssistLogger _log;
    private long? _lastTurnAt;
    private string? _turnTargetId;

    public ComboFacingCoordinator(IOrientationController orientation, IMonotonicClock clock,
        Func<LiveFrame?> latest, Func<bool> turnAllowed, DirectionCalculatorOptions? geometry = null,
        IAutoAssistLogger? logger = null)
    {
        _orientation = orientation; _clock = clock; _latest = latest; _turnAllowed = turnAllowed;
        _geometry = geometry ?? new DirectionCalculatorOptions { PositionTolerance = 0.1, HysteresisRatio = 1.25 };
        _log = logger ?? NullLogger.Instance;
    }

    public void Reset() { _lastTurnAt = null; _turnTargetId = null; }

    public ActionResult BeforeAction()
    {
        var frame = _latest();
        var now = _clock.NowMs;
        if (frame is null || now < frame.CapturedAtMs || now - frame.CapturedAtMs > 500 ||
            frame.ReadDurationMs is < 0 or > 500 || !frame.Snapshot.Player.IsAlive)
            return ActionResult.Rejected("Sem leitura válida para preparar giro do combo");
        // Never send an offensive key to a selected target when the preparatory turn is denied.
        // Old targetless profiles keep their independent combo; current profiles require a target.
        if (frame.Snapshot.Target is not { IsAlive: true, IsTargeted: true } target)
        { Reset(); return ActionResult.Ok("Combo independente: sem alvo de giro"); }
        if (!_turnAllowed()) return ActionResult.Rejected("Giro preparatório do combo sem permissão");
        var player = frame.Snapshot.Player;
        var desired = DirectionCalculator.Calculate(target.PositionX - player.PositionX,
            target.PositionY - player.PositionY, player.Facing, _geometry);
        if (desired == Direction.None || player.Facing == Direction.None)
            return ActionResult.Rejected("Direção do combo indisponível; tecla bloqueada");
        if (_lastTurnAt is { } issued)
        {
            if (target.Id != _turnTargetId)
                return ActionResult.Rejected("Alvo mudou após giro preparatório; tecla bloqueada");
            if (frame.CapturedAtMs <= issued || desired != player.Facing)
                return ActionResult.Deferred("Aguardando direção observada após giro; não repetir tecla de giro");
            Reset();
            _log.Log(LogLevel.Info, $"Direção {player.Facing} observada após giro prioritário; próxima tecla liberada", EventOrigin.Observed);
            return ActionResult.Ok("Direção observada alinhada; próxima tecla liberada");
        }
        if (desired == player.Facing)
            return ActionResult.Ok("Já alinhado; próxima tecla liberada");
        var result = _orientation.RequestTurn(desired); // native adapter still checks its OWN turn gate
        if (!result.Accepted) return ActionResult.Rejected($"Giro antes do combo recusado: {result.Reason}");
        _lastTurnAt = now;
        _turnTargetId = target.Id;
        _log.Log(LogLevel.Info, $"Giro prioritário {desired} enviado antes da próxima tecla do combo; aguardando leitura nova", EventOrigin.Executed);
        return ActionResult.Deferred($"Giro {desired} enviado antes da magia; aguardando direção na memória");
    }
}
