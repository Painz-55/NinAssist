using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.Live;

public sealed record StaminaObservation(int Current, int Maximum, bool Running)
{
    public bool Valid => Maximum > 0 && Current >= 0 && Current <= Maximum;
    public double Percent => Valid ? Current * 100.0 / Maximum : double.NaN;
}

/// <summary>Hysteresis with observed state confirmation. No blind repeated toggle on timeout.</summary>
public sealed class AutoStaminaEngine
{
    private readonly IMonotonicClock _clock;
    private readonly Func<bool, ActionResult> _toggle;
    private readonly int _disableBelow, _enableAbove;
    private string? _player;
    private int _map;
    private long? _sentAt, _sentCapture;
    private bool _faulted;
    public bool? DesiredRunning { get; private set; }
    public string Status { get; private set; } = "Auto Stamina OFF";
    public AutoStaminaEngine(IMonotonicClock clock, Func<bool, ActionResult> toggle, int disableBelowPercent = 20, int enableAbovePercent = 50)
    {
        CombatAssistSettings.ValidateStaminaThresholds(disableBelowPercent, enableAbovePercent);
        _clock = clock; _toggle = toggle; _disableBelow = disableBelowPercent; _enableAbove = enableAbovePercent;
    }
    public void Reset()
    {
        DesiredRunning = null; _sentAt = _sentCapture = null; _player = null; _map = 0; _faulted = false;
        Status = "Auto Stamina OFF";
    }
    public bool NeedsToggle(LiveFrame frame) => !_faulted && _sentAt is null && DesiredRunning is { } desired &&
        frame.Stamina is { Valid: true } state && state.Running != desired &&
        // Never enable running when a newer frame has already crossed the lower limit (or the converse).
        (desired ? state.Percent >= _disableBelow : state.Percent <= _enableAbove);

    public void Update(LiveFrame? frame, bool enabled, bool allowed, bool higherPriorityBusy)
    {
        if (!enabled) { Reset(); return; }
        if (frame?.Stamina is not { Valid: true } state || _clock.NowMs < frame.CapturedAtMs ||
            _clock.NowMs - frame.CapturedAtMs > 500 || frame.ReadDurationMs is < 0 or > 500)
        { Status = "Stamina: dados ausentes/antigos/inválidos"; return; }
        if (_player != frame.Snapshot.Player.Id || _map != frame.Map)
        { Reset(); _player = frame.Snapshot.Player.Id; _map = frame.Map; }
        if (state.Percent < _disableBelow) DesiredRunning = false;
        else if (state.Percent > _enableAbove) DesiredRunning = true;
        else DesiredRunning ??= state.Running;
        var summary = $"SP {state.Current}/{state.Maximum} ({state.Percent:0}%) · corrida {(state.Running ? "ON" : "OFF")}";
        if (_sentAt is { } sent)
        {
            if (frame.CapturedAtMs > _sentCapture && state.Running == _sentDesired)
            { _sentAt = _sentCapture = null; }
            else if (_clock.NowMs - sent >= 1500)
            { _faulted = true; _sentAt = _sentCapture = null; }
            else { Status = $"{summary} · aguardando confirmação"; return; }
        }
        if (_faulted) { Status = $"{summary} · sem confirmação; desligue/ligue o módulo para rearmar"; return; }
        if (!allowed || higherPriorityBusy) { Status = $"{summary} · bloqueado/aguardando prioridade"; return; }
        if (!NeedsToggle(frame)) { Status = summary; return; }
        var result = _toggle(DesiredRunning!.Value);
        // Refusals also latch: never retry rapidly on a physically held key, invalid binding or native failure.
        if (!result.Accepted) { _faulted = true; Status = $"{summary} · {result.Reason}; rearme o módulo"; return; }
        _sentAt = _clock.NowMs; _sentCapture = frame.CapturedAtMs; _sentDesired = DesiredRunning.Value;
        Status = $"{summary} · solicitado {(_sentDesired ? "ON" : "OFF")}; aguardando confirmação";
    }
    private bool _sentDesired;
}
