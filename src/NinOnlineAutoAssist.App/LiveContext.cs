using System.Diagnostics;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Core.Security;

namespace NinOnlineAutoAssist.App;

/// <summary>Live-only session. Reading continues while disarmed; inputs always require the safety gate.</summary>
public sealed class LiveContext : IDisposable
{
    private readonly AccessLease _access;
    private AppFeature SubFeature => Config.Live.PunishMode == Core.Config.LivePunishMode.AnchoredExperimental
        ? AppFeature.SubPunishNew : AppFeature.SubPunishLegacy;
    private int _accessMask;
    private int AccessMask => Enum.GetValues<AppFeature>().Aggregate(0, (mask, feature) =>
        _access.Allows(feature) ? mask | (1 << (int)feature) : mask);
    public AutoAssistConfig Config { get; }
    public MemoryLogger MemoryLog { get; } = new(400);
    public IAutoAssistLogger Log { get; }
    public ReadOnlyGameStateProvider? Provider { get; private set; }
    public AutoTurningEngine? AutoTurning { get; private set; }
    public ILivePunishEngine? Punish { get; private set; }
    public AutoAttackToolEngine? Combat { get; private set; }
    public AutoFollowEngine? Follow { get; private set; }
    public AutoStaminaEngine? Stamina { get; private set; }
    public AutoCastTurnEngine? CastTurn { get; private set; }
    private readonly SystemMonotonicClock _clock = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly object _automationLock = new();
    private readonly SubstitutionTurnWindow _subTurnWindow = new();
    private ResponsiveAutomationLoop? _automation;
    private LiveInputController? _input;
    private ComboFacingCoordinator? _comboFacing;
    private bool _preparingComboTurn;
    private bool _preparingFanTurn;
    private Direction _fanTurnDesired;
    private long? _fanTurnAt;
    private string? _fanTurnTarget;
    private Task? _reader;
    private LiveFrame? _frame, _dispatchFrame;
    private NavigationFrame? _navigation;
    private MapPlayersFrame? _mapPlayers;
    public MapPlayersFrame? MapPlayers => _access.Allows(AppFeature.MapPlayers) && Volatile.Read(ref _mapPlayers) is { } list && list.IsCurrent(Frame, _clock.NowMs) ? list : null;
    public string MapPlayersStatus => MapPlayers is { } list
        ? $"Mapa {list.Map}: {list.Players.Count} jogadores registrados · falhas de leitura: {list.UnreadableSlots}"
        : Provider?.MapPlayersStatus ?? "Desconectado / lista indisponível";
    private LiveInputKind? _dispatchingCombat;
    private FollowStep? _dispatchingFollow;
    private LiveFrame? _followFrame;
    private long _lastTurnCapture = -1;
    private volatile bool _autoEnabled, _subEnabled, _disposed, _authorized, _subReadBurst;
    private volatile bool _combatEnabled, _combatAuthorized, _followEnabled;
    private volatile bool _staminaEnabled, _castTurnEnabled;
    private bool _dispatchingCastTurn;
    public string CastTurnStatus { get; private set; } = "Auto Cast Turn OFF";
    private bool _dispatchingStamina;
    private string? _lastStaminaSafety;
    private bool _punishWasArmed;
    private string? _lastTurnSafetyReason, _lastPunishSafetyReason, _lastTurnDecision;
    private BuffPresence? _lastBuffPresence;
    private string? _lastBuffNames;
    private string _connectionStatus = "Desconectado";
    public bool CounterAttackAuthorized => _authorized;
    public string Status => Provider?.Status ?? _connectionStatus;
    public string TurningSafetyStatus { get; private set; } = "Auto Turning OFF";
    public string PunishSafetyStatus { get; private set; } = "SubPunish OFF";
    public string TurnStatus { get; private set; } = "Nenhum giro solicitado";
    public string CombatStatus { get; private set; } = "AA/Tool OFF";
    public string FollowStatus { get; private set; } = "Follow OFF";
    public string StaminaStatus { get; private set; } = "Auto Stamina OFF";
    private bool SubNeedsPriority => Punish?.BlocksAutoTurning == true ||
        (_subEnabled && CounterAttackAuthorized && Punish?.HasUnprocessedRemoval(Volatile.Read(ref _frame)) == true);
    public bool SubstitutionTurningActive
    {
        get
        {
            lock (_automationLock)
                return _subEnabled && CounterAttackAuthorized && _subTurnWindow.IsOpen(Volatile.Read(ref _frame), _clock.NowMs);
        }
    }
    public string? ActionStatus => _input?.LastActionText;
    public LiveBuffObservation BuffObservation => LiveBuffObservation.FromFrame(Volatile.Read(ref _frame), _clock.NowMs);
    public LiveFrame? Frame => Volatile.Read(ref _frame) is { } frame && _clock.NowMs - frame.CapturedAtMs <= 500 ? frame : null;
    public GameSnapshot? Snapshot() => Frame?.Snapshot;

    public LiveContext(AutoAssistConfig config, AccessLease access, bool detectCharacterAutomatically = false)
    {
        _access = access;
        if (!access.IsValid) throw new AccessDeniedException("Login obrigatório antes da conexão.");
        _accessMask = AccessMask;
        Config = config;
        Log = config.General.Logging ? new CompositeLogger(MemoryLog, new FileLogger(AppPaths.Settings.ResolveLogPath(config.General.LogFile))) : MemoryLog;
        _authorized = config.Live.AllowCounterAttackSequence;
        try
        {
            var processes = Process.GetProcessesByName("NinOnline");
            int pid;
            try
            {
                if (processes.Length != 1) throw new InvalidOperationException("Abra exatamente um cliente NinOnline para conectar.");
                pid = processes[0].Id;
            }
            finally { foreach (var process in processes) process.Dispose(); }
            Provider = new ReadOnlyGameStateProvider(pid, detectCharacterAutomatically ? "" : config.Live.CharacterName, _clock);
            Log.Log(LogLevel.Info, $"Perfil detectado: {Provider.Profile.Id}; instalação: {Provider.GameDirectory}; hashes do par verificados");
            if (!Provider.Profile.ActuationValidated)
                Log.Log(LogLevel.Warning, "Perfil externo apenas para leitura: automação bloqueada até validação passiva e assinatura de um perfil aprovado.");
            LiveKeySettings.Validate(config.Live.PunishKeys, config.Live.TurnActivationKeys);
            _input = new LiveInputController(CanDispatch, Log, Provider.GameDirectory,
                config.Live.PunishKeys, config.Live.TurnActivationKeys, PrepareComboFacing, config.CombatAssist);
            _comboFacing = new ComboFacingCoordinator(_input, _clock, () => Frame,
                () => CheckSafety(LiveInputKind.Turning).Allowed, logger: Log);
            CastTurn = new AutoCastTurnEngine(_clock, config.AutoCastTurn, direction =>
            {
                _dispatchFrame = Frame; _dispatchingCastTurn = true;
                try
                {
                    var result = _input.RequestCastTurn(direction, config.AutoCastTurn.LeadTimeMs + 50);
                    Log.Log(result.Accepted ? LogLevel.Info : LogLevel.Warning,
                        $"Auto Cast Turn: {direction} · {result.Reason}; efeito/aceitação da magia não confirmados", EventOrigin.Requested);
                    return result;
                }
                finally { _dispatchingCastTurn = false; }
            });
            AutoTurning = new AutoTurningEngine(_input, _clock, Log, config.ToAutoTurningOptions() with
            {
                Enabled = false, DiagnosticMode = true, MinCommandIntervalMs = 40, ManualMovementPriority = false,
                Geometry = new Core.Geometry.DirectionCalculatorOptions { PositionTolerance = 0.1, HysteresisRatio = 1.25 }
            });
            Punish = LivePunishFactory.Create(config.Live.PunishMode, _input, _clock, new EarlyPunishOptions
            {
                EarlyExpiryMarginMs = config.Live.EarlyExpiryMarginMs,
                AbsentSamplesRequired = config.Live.EarlyExpiryAbsentSamples,
                KeyIntervalMs = config.Live.PunishKeyIntervalMs,
                ComboKeys = config.Live.PunishKeys,
                FirstToSecondMs = config.Live.PunishFirstToSecondMs,
                TriggerDelayMs = config.Live.PunishTriggerDelayMs,
                SecondToThirdMs = config.Live.PunishSecondToThirdMs,
                SequenceTimeoutMs = config.Live.PunishSequenceTimeoutMs
            }, Log);
            Log.Log(LogLevel.Info, $"Modo escolhido: {LivePunishFactory.Name(config.Live.PunishMode)}; motores mutuamente exclusivos; início OFF");
            Combat = new AutoAttackToolEngine(_clock, config.CombatAssist, DispatchCombat, PrepareFanAttack);
            Follow = new AutoFollowEngine(_clock, config.CombatAssist, DispatchFollow, () => _input.ReleaseFollow());
            _input.RefreshFollowDuringTap = UpdateFollow;
            Stamina = new AutoStaminaEngine(_clock, desired =>
            {
                _dispatchFrame = Frame; _dispatchingStamina = true;
                try
                {
                    var result = _input.ToggleRun();
                    Log.Log(result.Accepted ? LogLevel.Info : LogLevel.Warning,
                        $"Auto Stamina solicitou corrida {(desired ? "ON" : "OFF")}; SP={Frame?.Stamina?.Percent:0.0}%; {result.Reason}", EventOrigin.Requested);
                    return result;
                }
                finally { _dispatchingStamina = false; }
            }, config.CombatAssist.StaminaDisableBelowPercent, config.CombatAssist.StaminaEnableAbovePercent);
            Log.Log(LogLevel.Info, $"Live connection: PID {pid}, character {(detectCharacterAutomatically ? "detecção automática pelo índice local" : config.Live.CharacterName)}; modules OFF; Turn activation: {string.Join(" / ", config.Live.TurnActivationKeys)}; giro automático com SubPunish autorizado durante buff/+1s; combo sem alvo {string.Join(" → ", config.Live.PunishKeys)} {(CounterAttackAuthorized ? "authorized" : "DISABLED")}");
            Log.Log(LogLevel.Info, $"AA/Tool OFF e desautorizado: {config.CombatAssist.AttackKey} {CombatGeometry.DescribeAttack(config.CombatAssist)} / Tool {config.CombatAssist.ToolKey} frontal <= {config.CombatAssist.ToolRange} tiles; Follow OFF, tecla {config.CombatAssist.FollowHoldKey}");
            Log.Log(LogLevel.Info, $"Auto Stamina OFF; binding válido={_input.StaminaBindingValid}; {_input.StaminaBindingStatus}; {_input.StaminaBindingProblem}");
            _automation = new ResponsiveAutomationLoop(Step, ex =>
            {
                _autoEnabled = _subEnabled = _authorized = _combatEnabled = _combatAuthorized = _followEnabled = false;
                _staminaEnabled = _castTurnEnabled = false;
                lock (_automationLock) { _input?.ReleaseFollow(); ResetPunish(); AutoTurning?.ResetCadence(); }
                Log.Log(LogLevel.Error, $"Automação interrompida; reconecte: {ex.Message}");
            }, () =>
            {
                lock (_automationLock)
                    return Punish?.NextKeyDueAtMs is { } due ? due - _clock.NowMs : null;
            });
            _reader = Task.Run(async () =>
            {
                try
                {
                    while (!_stop.IsCancellationRequested)
                    {
                        var followHeld = _access.Allows(AppFeature.Follow) && _followEnabled && LiveInputController.IsDown(LiveKeySettings.Keys[Config.CombatAssist.FollowHoldKey]);
                        var oldNavigation = Volatile.Read(ref _navigation);
                        Provider.ReadNavigation = followHeld && (oldNavigation is null ||
                            oldNavigation.Map != Volatile.Read(ref _frame)?.Map || _clock.NowMs - oldNavigation.CapturedAtMs >= 100);
                        var oldList = Volatile.Read(ref _mapPlayers);
                        Provider.ReadMapPlayers = _access.Allows(AppFeature.MapPlayers) && (oldList is null || oldList.Map != Volatile.Read(ref _frame)?.Map ||
                            _clock.NowMs - oldList.CapturedAtMs >= 500);
                        if (!_access.Allows(AppFeature.MapPlayers)) Volatile.Write(ref _mapPlayers, null);
                        Provider.GetSnapshot();
                        Volatile.Write(ref _frame, Provider.Frame);
                        if (Provider.ReadMapPlayers) Volatile.Write(ref _mapPlayers, Provider.MapPlayers);
                        if (Provider.Frame is null) Volatile.Write(ref _mapPlayers, null);
                        if (Provider.ReadNavigation) Volatile.Write(ref _navigation, Provider.Navigation);
                        else if (!followHeld) Volatile.Write(ref _navigation, null);
                        _automation.Wake();
                        // Burst reads only while the buff is watched/removed or Turn is being held.
                        // Never skip FlushCachedData or reuse managed object addresses to gain speed.
                        var urgent = LiveInputController.IsGameForeground(Provider.ProcessId) &&
                            ((_castTurnEnabled && Provider.Frame?.Cast?.Active == true) ||
                            (_combatEnabled || followHeld || (_subEnabled && CounterAttackAuthorized &&
                                (Provider.Frame?.SubstitutionActive == true || _subReadBurst)) ||
                            (_autoEnabled && Config.Live.TurnActivationKeys.Any(k => LiveInputController.IsDown(LiveKeySettings.Keys[k])))));
                        if (!urgent || Provider.Frame is null)
                            await Task.Delay(5, _stop.Token).ConfigureAwait(false);
                        else Thread.Yield();
                    }
                }
                catch (OperationCanceledException) { }
                finally { Provider.Dispose(); }
            });
        }
        catch (Exception ex)
        {
            _automation?.Dispose();
            _automation = null;
            _connectionStatus = ex.Message;
            Provider?.Dispose();
            Provider = null;
            Log.Log(LogLevel.Error, $"Conexão recusada: {ex.Message}");
        }
    }

    public void SetCounterAttackAuthorized(bool authorized)
    {
        authorized &= _access.Allows(SubFeature);
        // Revocation is visible to the native adapter even if a tap currently owns the lock.
        if (!authorized) _authorized = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            // Consent changes never replay an old trigger or append keys to an in-progress sequence.
            ResetPunish();
            _authorized = authorized;
            Log.Log(LogLevel.Info, $"Combo {string.Join(" → ", Config.Live.PunishKeys)} + giro Sub {(authorized ? "AUTORIZADOS — heurística" : "DESAUTORIZADOS")}; fluxo anterior cancelado", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetAutoTurningEnabled(bool enabled)
    {
        enabled &= _access.Allows(AppFeature.AutoTurning);
        if (!enabled) _autoEnabled = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            _autoEnabled = enabled;
            AutoTurning?.UpdateOptions(AutoTurning.Options with { Enabled = false });
            if (!enabled) { AutoTurning?.ResetCadence(); _lastTurnCapture = -1; }
            Log.Log(LogLevel.Info, $"Live Auto Turning {(enabled ? "ON" : "OFF")}", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetSubPunishEnabled(bool enabled)
    {
        enabled &= _access.Allows(SubFeature);
        if (!enabled) _subEnabled = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            _subEnabled = enabled;
            ResetPunish();
            Log.Log(LogLevel.Info, $"Live Punish {(enabled ? "ON" : "OFF")}; F manual; remoção antecipada HEURÍSTICA; combo {(CounterAttackAuthorized ? "authorized" : "DISABLED")}", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetCombatEnabled(bool enabled)
    {
        enabled &= _access.Allows(AppFeature.AutoAttack) || _access.Allows(AppFeature.Tool);
        if (!enabled) _combatEnabled = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            Combat?.Reset(); _fanTurnAt = null; _fanTurnTarget = null; _combatEnabled = enabled;
            Log.Log(LogLevel.Info, $"AA/Tool {(enabled ? "ON" : "OFF")}; automático com autorização, alcances/intervalos configurados", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetCombatAuthorized(bool authorized)
    {
        authorized &= _access.Allows(AppFeature.AutoAttack) || _access.Allows(AppFeature.Tool);
        if (!authorized) _combatAuthorized = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            Combat?.Reset(); _fanTurnAt = null; _fanTurnTarget = null; _combatAuthorized = authorized;
            Log.Log(LogLevel.Info, $"AA/Tool {(authorized ? "AUTORIZADO" : "DESAUTORIZADO")}", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetFollowEnabled(bool enabled)
    {
        enabled &= _access.Allows(AppFeature.Follow);
        if (!enabled) _followEnabled = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            Follow?.Reset(); _dispatchingFollow = null; _followFrame = null; _followEnabled = enabled;
            Log.Log(LogLevel.Info, $"Follow {(enabled ? "ON" : "OFF")}; exige tecla {Config.CombatAssist.FollowHoldKey} e navegação válida", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetStaminaEnabled(bool enabled)
    {
        enabled &= _access.Allows(AppFeature.Stamina);
        if (!enabled) _staminaEnabled = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            Stamina?.Reset(); _staminaEnabled = enabled;
            Log.Log(LogLevel.Info, $"Auto Stamina {(enabled ? "ON" : "OFF")}: OFF <{Config.CombatAssist.StaminaDisableBelowPercent}%, ON >{Config.CombatAssist.StaminaEnableAbovePercent}%; {_input?.StaminaBindingStatus}", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    public void SetCastTurnEnabled(bool enabled)
    {
        enabled &= _access.Allows(AppFeature.AutoCastTurn) && Provider?.Profile.CastTiming is not null;
        if (!enabled) _castTurnEnabled = false;
        lock (_automationLock)
        {
            if (_disposed) return;
            CastTurn?.Reset(); _castTurnEnabled = enabled;
            Log.Log(LogLevel.Info, $"Auto Cast Turn {(enabled ? "ON" : "OFF")}; final do cast observado, sem tecla de ativação", EventOrigin.Requested);
        }
        _automation?.Wake();
    }

    private SafetyDecision CheckSafety(LiveInputKind kind)
    {
        if (Provider?.Profile.ActuationValidated != true)
            return new(false, "Perfil de jogo não aprovado para enviar teclas");
        if (Provider.Profile.ExternalProfile && !Provider.Profile.ValidatedInputKinds.Contains(kind.ToString(), StringComparer.Ordinal))
            return new(false, $"{kind}: sinal ainda não validado para esta build");
        var feature = kind switch
        {
            LiveInputKind.Attack => AppFeature.AutoAttack, LiveInputKind.Tool => AppFeature.Tool,
            LiveInputKind.Follow => AppFeature.Follow, LiveInputKind.Stamina => AppFeature.Stamina,
            LiveInputKind.Punish => SubFeature, LiveInputKind.CastTurning => AppFeature.AutoCastTurn, _ => AppFeature.AutoTurning
        };
        if (kind != LiveInputKind.Turning && !_access.Allows(feature)) return new(false, "Recurso não autorizado pelo Firebase");
        var frame = Volatile.Read(ref _frame);
        var now = _clock.NowMs;
        var foreground = Provider is not null && LiveInputController.IsGameForeground(Provider.ProcessId);
        var emergency = LiveInputController.EmergencyPending();
        var text = LiveInputController.TextEntryFocused();
        if (kind == LiveInputKind.CastTurning)
            return LiveSafetyGate.CheckCastTurning(frame, now, _castTurnEnabled, foreground, emergency, text);
        if (kind == LiveInputKind.Stamina)
        {
            var gate = LiveSafetyGate.CheckStamina(frame, now, _staminaEnabled, foreground, emergency, text);
            return gate.Allowed && _input?.StaminaBindingValid != true
                ? new(false, _input?.StaminaBindingProblem ?? "Run indisponível") : gate;
        }
        if (kind is LiveInputKind.Attack or LiveInputKind.Tool)
            return LiveSafetyGate.CheckTargetedModule(frame, now, _combatEnabled, _combatAuthorized, foreground, emergency, text);
        if (kind == LiveInputKind.Follow)
        {
            var gate = LiveSafetyGate.CheckTargetedModule(frame, now, _followEnabled, true, foreground, emergency, text);
            if (!gate.Allowed) return gate;
            if (!LiveInputController.IsDown(LiveKeySettings.Keys[Config.CombatAssist.FollowHoldKey]))
                return new(false, "Follow: segure a tecla configurada");
            var nav = Volatile.Read(ref _navigation);
            if (nav is null || nav.Map != frame!.Map || now < nav.CapturedAtMs || now - nav.CapturedAtMs > 500 || !nav.DynamicHazardsKnown)
                return new(false, Provider?.NavigationStatus ?? "Navegação ausente");
            var cell = new GridPoint((int)Math.Floor(frame.Snapshot.Player.PositionX), (int)Math.Floor(frame.Snapshot.Player.PositionY));
            if (nav.Cell(cell.X, cell.Y) == NavigationCell.Unknown) return new(false, "Posição fora do terreno observado");
            return gate;
        }
        if (kind == LiveInputKind.Turning)
        {
            if (_preparingFanTurn)
            {
                if (Provider?.Profile.ValidatedInputKinds.Contains(nameof(LiveInputKind.Attack), StringComparer.Ordinal) != true ||
                    !_access.Allows(AppFeature.AutoAttack)) return new(false, "Giro Fan sem aprovação de Attack");
                return LiveSafetyGate.CheckTargetedModule(frame, now, _combatEnabled, _combatAuthorized,
                    foreground, emergency, text);
            }
            var subTurn = LiveSafetyGate.CheckSubstitutionTurning(frame, now, _subEnabled && _access.Allows(SubFeature), CounterAttackAuthorized,
                _subTurnWindow.IsOpen(frame, now), foreground, emergency, false, text);
            if (subTurn.Allowed) return subTurn;
            var regular = LiveSafetyGate.CheckTurning(frame, now, _autoEnabled && _access.Allows(AppFeature.AutoTurning), foreground,
                Config.Live.TurnActivationKeys.Any(k => LiveInputController.IsDown(LiveKeySettings.Keys[k])), emergency, false, text);
            return !regular.Allowed && SubstitutionTurningActive ? subTurn : regular;
        }
        return kind == LiveInputKind.Punish
            ? LiveSafetyGate.CheckPunish(frame, now, _subEnabled, CounterAttackAuthorized, foreground, emergency, false, text)
            : new(false, "Finalidade de input desconhecida");
    }

    private bool CanDispatch(LiveInputKind kind)
    {
        var dispatch = kind == LiveInputKind.Follow ? _followFrame : _dispatchFrame;
        if (_disposed || !CheckSafety(kind).Allowed || Volatile.Read(ref _frame) is not { } current ||
            dispatch is null || current.Map != dispatch.Map || current.Snapshot.Player.Id != dispatch.Snapshot.Player.Id)
            return false;
        if (kind is LiveInputKind.Attack or LiveInputKind.Tool)
            return _dispatchingCombat == kind && !SubNeedsPriority &&
                current.Snapshot.Target?.Id == dispatch.Snapshot.Target?.Id && CombatGeometry.InRange(current,
                    kind == LiveInputKind.Attack ? CombatAction.Attack : CombatAction.Tool, Config.CombatAssist);
        if (kind == LiveInputKind.Turning && _preparingFanTurn)
            return !SubNeedsPriority && CastTurn?.InFinalWindow != true &&
                current.Snapshot.Target?.Id == dispatch.Snapshot.Target?.Id &&
                current.Snapshot.Target?.Id == _fanTurnTarget &&
                CombatGeometry.AttackDirection(current, Config.CombatAssist) == _fanTurnDesired &&
                current.Snapshot.Player.Facing != _fanTurnDesired;
        if (kind == LiveInputKind.Stamina)
            return _dispatchingStamina && !SubNeedsPriority && Stamina?.NeedsToggle(current) == true;
        if (kind == LiveInputKind.CastTurning)
            return _dispatchingCastTurn && !SubNeedsPriority && (_input?.MaintainingCastTurn == true
                ? CastTurn?.CanMaintain(current, _clock.NowMs) == true : CastTurn?.CanDispatch(current, _clock.NowMs) == true);
        if (kind == LiveInputKind.Follow)
        {
            if (_dispatchingFollow is not { } step || SubNeedsPriority ||
                current.Snapshot.Target?.Id != dispatch.Snapshot.Target?.Id || _input?.OtherMovementHeld(step) != false) return false;
            var p = current.Snapshot.Player;
            var nav = Volatile.Read(ref _navigation);
            var cell = new GridPoint((int)Math.Floor(p.PositionX), (int)Math.Floor(p.PositionY));
            // Safety service checks the current swept step; the worker replans direction independently.
            // Do not release both keys merely because the new route keeps only one shared component.
            if (nav is null || !nav.SafeStep(cell, step.From(cell))) return false;
            var target = current.Snapshot.Target!;
            return Math.Sqrt(Math.Pow(target.PositionX - p.PositionX, 2) + Math.Pow(target.PositionY - p.PositionY, 2)) > Config.CombatAssist.FollowStopDistance;
        }
        return (kind == LiveInputKind.Turning
            ? (_preparingComboTurn || (SubstitutionTurningActive ? Punish?.HasTimeForTurn != false : Punish?.BlocksAutoTurning != true))
            : Punish?.State == EarlyPunishState.Executing) &&
        (kind == LiveInputKind.Punish ? Punish?.TriggerBuffsRemainAbsent(current) == true
            : current.Snapshot.Target?.Id == dispatch.Snapshot.Target?.Id &&
                !(SubstitutionTurningActive == false && _subEnabled && CounterAttackAuthorized &&
                    dispatch.SubstitutionActive && !current.SubstitutionActive));
    }

    private ActionResult DispatchCombat(CombatAction action, string key)
    {
        _dispatchFrame = Frame;
        _dispatchingCombat = action == CombatAction.Attack ? LiveInputKind.Attack : LiveInputKind.Tool;
        try { return _input!.ExecuteCombat(action, key); }
        finally { _dispatchingCombat = null; }
    }

    private ActionResult PrepareFanAttack(LiveFrame frame, Direction desired)
    {
        if (_disposed || SubNeedsPriority || CastTurn?.InFinalWindow == true ||
            frame.Snapshot.Target is not { Id: { } targetId } ||
            !CheckSafety(LiveInputKind.Attack).Allowed)
            return ActionResult.Rejected("Giro Fan bloqueado por segurança");
        if (_fanTurnTarget != targetId) { _fanTurnAt = null; _fanTurnTarget = targetId; }
        if (_fanTurnAt is { } issued && frame.CapturedAtMs <= issued)
            return ActionResult.Deferred("Fan: aguardando leitura nova após giro");
        if (desired == frame.Snapshot.Player.Facing)
        {
            _fanTurnAt = null;
            return CombatGeometry.InRange(frame, CombatAction.Attack, Config.CombatAssist)
                ? ActionResult.Ok("Fan alinhado na leitura atual")
                : ActionResult.Rejected("Fan fora da faixa frontal");
        }
        if (_fanTurnAt is { } last && _clock.NowMs - last < 40)
            return ActionResult.Deferred("Fan: intervalo mínimo do giro");
        _dispatchFrame = frame;
        _fanTurnDesired = desired;
        _preparingFanTurn = true;
        try
        {
            var turn = _input!.RequestTurn(desired);
            if (!turn.Accepted) return ActionResult.Rejected($"Giro Fan recusado: {turn.Reason}");
            _fanTurnAt = _clock.NowMs;
            Log.Log(LogLevel.Info, $"Giro Fan {desired} enviado antes do AA; aguardando direção observada", EventOrigin.Executed);
            return ActionResult.Deferred("Fan: aguardando direção observada antes do AA");
        }
        finally { _preparingFanTurn = false; }
    }

    private ActionResult DispatchFollow(FollowStep step)
    {
        _followFrame = Frame;
        if (_followFrame is null) return ActionResult.Rejected("Sem frame para seguir");
        _dispatchingFollow = step;
        return _input!.Move(step);
    }

    private ActionResult PrepareComboFacing()
    {
        if (_disposed || _comboFacing is null || !CheckSafety(LiveInputKind.Punish).Allowed)
            return ActionResult.Rejected("Preparação de giro bloqueada por segurança");
        // Runs on the same serialized automation worker, immediately before EACH offensive key.
        // Only priority arbitration is bypassed, NEVER the turn's foreground/target/key/window gate.
        _dispatchFrame = Frame;
        _preparingComboTurn = true;
        try
        {
            var decision = _comboFacing.BeforeAction();
            if (!decision.Accepted) TurnStatus = decision.Reason;
            return decision;
        }
        finally { _preparingComboTurn = false; }
    }

    private void ResetPunish()
    {
        Punish?.Reset();
        _comboFacing?.Reset();
        _subTurnWindow.Reset();
        _punishWasArmed = false;
        _subReadBurst = false;
    }

    public void Step()
    {
        lock (_automationLock)
        {
            if (!_disposed) StepCore();
        }
    }

    private void StepCore()
    {
        var mask = AccessMask;
        if (mask != _accessMask || !_access.IsValid)
        {
            _accessMask = mask;
            _autoEnabled = _subEnabled = _authorized = _combatEnabled = _combatAuthorized = _followEnabled = _staminaEnabled = _castTurnEnabled = false;
            _input?.ReleaseFollow(); Follow?.Reset(); Combat?.Reset(); Stamina?.Reset(); CastTurn?.Reset(); ResetPunish(); AutoTurning?.ResetCadence();
        }
        _input?.ServiceFollow();
        // Raw observation is updated even with no target, no Ctrl, lost focus or modules OFF.
        var observation = BuffObservation;
        var names = string.Join(" + ", observation.ActiveSubs.Select(b => b.Name).OrderBy(n => n));
        if (_lastBuffPresence != observation.Presence || _lastBuffNames != names)
        {
            _lastBuffPresence = observation.Presence;
            _lastBuffNames = names;
            Log.Log(LogLevel.Info, $"Leitura Subs: {observation.Presence}; {string.Join(" | ", observation.ActiveSubs.Select(b => $"{b.Name}: {b.RemainingSeconds?.ToString() ?? "?"}s"))}; {observation.Reason}", EventOrigin.Observed);
        }
        var frame = Frame;
        var punishSafety = CheckSafety(LiveInputKind.Punish);
        _subTurnWindow.Update(frame, _clock.NowMs, punishSafety.Allowed);
        var turnSafety = CheckSafety(LiveInputKind.Turning);
        TurningSafetyStatus = turnSafety.Reason;
        PunishSafetyStatus = punishSafety.Reason;
        if (_lastTurnSafetyReason != turnSafety.Reason)
        {
            _lastTurnSafetyReason = turnSafety.Reason;
            Log.Log(LogLevel.Info, $"Turn safety: {turnSafety.Reason}", EventOrigin.Observed);
        }
        if (_lastPunishSafetyReason != punishSafety.Reason)
        {
            _lastPunishSafetyReason = punishSafety.Reason;
            Log.Log(LogLevel.Info, $"Punish safety: {punishSafety.Reason}", EventOrigin.Observed);
        }
        AutoTurning?.UpdateOptions(AutoTurning.Options with
        {
            Enabled = turnSafety.Allowed,
            // User policy: neither physical movement keys nor position changes suppress live turning.
            ManualMovementPriority = false
        });
        if (!turnSafety.Allowed)
        {
            _lastTurnCapture = -1;
            AutoTurning?.ResetCadence();
        }
        _dispatchFrame = frame;
        if (_punishWasArmed && !punishSafety.Allowed)
            Log.Log(LogLevel.Warning, $"Fluxo SubPunish cancelado por segurança: {punishSafety.Reason}", EventOrigin.Observed);
        _punishWasArmed = punishSafety.Allowed;
        // No Ctrl dependency here. The engine deduplicates samples and cancels on its OWN gate only.
        Punish?.Update(frame, _subEnabled, CounterAttackAuthorized, punishSafety.Allowed, punishSafety.Reason);
        _subReadBurst = punishSafety.Allowed && (SubstitutionTurningActive ||
            Punish?.State is EarlyPunishState.WatchingBuff or EarlyPunishState.RemovalPending);
        var castGate = CheckSafety(LiveInputKind.CastTurning);
        CastTurn?.Update(frame, castGate.Allowed, SubNeedsPriority);
        CastTurnStatus = castGate.Allowed ? CastTurn?.Status ?? "Indisponível" : castGate.Reason;
        UpdateFollow();
        var canRunTurn = CastTurn?.InFinalWindow != true &&
            (SubstitutionTurningActive ? Punish?.HasTimeForTurn != false : Punish?.BlocksAutoTurning != true);
        if (turnSafety.Allowed && frame is not null && frame.CapturedAtMs != _lastTurnCapture && canRunTurn)
        {
            _lastTurnCapture = frame.CapturedAtMs;
            var turn = AutoTurning?.Update(frame.Snapshot);
            if (turn is not null)
            {
                TurnStatus = $"{turn.Kind}: {turn.Reason}";
                var signature = $"{turn.Kind}:{turn.Desired}:{turn.Reason}";
                if (_lastTurnDecision != signature)
                {
                    _lastTurnDecision = signature;
                    Log.Log(LogLevel.Info, $"Live turn {TurnStatus}; player={frame.Snapshot.Player.Facing}, target={frame.TargetName}", EventOrigin.Observed);
                }
            }
        }
        var combatGate = CheckSafety(LiveInputKind.Attack);
        var priorityBusy = SubNeedsPriority || CastTurn?.InFinalWindow == true;
        var toolGate = CheckSafety(LiveInputKind.Tool);
        Combat?.Update(Frame, combatGate.Allowed, toolGate.Allowed, priorityBusy);
        CombatStatus = combatGate.Allowed || toolGate.Allowed ? Combat?.Status ?? "Indisponível" : combatGate.Reason;
        var staminaGate = CheckSafety(LiveInputKind.Stamina);
        if (_lastStaminaSafety != staminaGate.Reason)
        {
            _lastStaminaSafety = staminaGate.Reason;
            Log.Log(LogLevel.Info, $"Stamina safety: {staminaGate.Reason}", EventOrigin.Observed);
        }
        Stamina?.Update(Frame, _staminaEnabled, staminaGate.Allowed, priorityBusy);
        StaminaStatus = staminaGate.Allowed ? Stamina?.Status ?? "Indisponível"
            : Frame?.Stamina is { Valid: true } sp
                ? $"{staminaGate.Reason} · SP {sp.Current}/{sp.Maximum} ({sp.Percent:0}%) · corrida {(sp.Running ? "ON" : "OFF")}" : staminaGate.Reason;
    }

    private void UpdateFollow()
    {
        if (_disposed) { _input?.ReleaseFollow(); return; }
        var gate = CheckSafety(LiveInputKind.Follow);
        Follow?.Update(Frame, Volatile.Read(ref _navigation), gate.Allowed, SubNeedsPriority);
        FollowStatus = gate.Allowed ? Follow?.Status ?? "Indisponível" : gate.Reason;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _autoEnabled = _subEnabled = _authorized = _combatEnabled = _combatAuthorized = _followEnabled = false;
        _staminaEnabled = _castTurnEnabled = false;
        _stop.Cancel();
        lock (_automationLock) { _input?.Dispose(); }
        // Stop publisher before disposing its wake handle; join outside the engine lock.
        _reader?.GetAwaiter().GetResult();
        _automation?.Dispose();
        lock (_automationLock) { _input?.Dispose(); ResetPunish(); _dispatchFrame = null; }
        Volatile.Write(ref _frame, null);
        _stop.Dispose();
    }
}
