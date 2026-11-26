using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.AutoTurning;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Geometry;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

public sealed class LiveContext : IDisposable
{
	private readonly AccessLease _access;

	private int _accessMask;

	private readonly SystemMonotonicClock _clock = new SystemMonotonicClock();

	private readonly CancellationTokenSource _stop = new CancellationTokenSource();

	private readonly object _automationLock = new object();

	private ResponsiveAutomationLoop? _automation;

	private LiveInputController? _input;

	private ComboFacingCoordinator? _comboFacing;

	private bool _preparingComboTurn;

	private bool _preparingFanTurn;

	private Direction _fanTurnDesired;

	private long? _fanTurnAt;

	private string? _fanTurnTarget;

	private string? _comboTargetId;

	private Task? _reader;

	private LiveFrame? _frame;

	private LiveFrame? _dispatchFrame;

	private string? _settingsBoundToCharacter;

	private NavigationFrame? _navigation;

	private MapPlayersFrame? _mapPlayers;

	private LiveInputKind? _dispatchingCombat;

	private FollowStep? _dispatchingFollow;

	private LiveFrame? _followFrame;

	private long _lastTurnCapture = -1L;

	private volatile bool _autoEnabled;

	private volatile bool _subEnabled;

	private volatile bool _disposed;

	private volatile bool _authorized;

	private volatile bool _subReadBurst;

	private volatile bool _combatEnabled;

	private volatile bool _attackEnabled;

	private volatile bool _toolEnabled;

	private volatile bool _combatAuthorized;

	private volatile bool _followEnabled;

	private volatile bool _staminaEnabled;

	private volatile bool _castTurnEnabled;

	private volatile bool _autoTargetEnabled;
	private volatile bool _smartTargetEnabled;

	private bool _dispatchingTargetMemory;
	private LiveInputKind _dispatchTargetKind;
	private bool SmartHoldDown => LiveInputController.IsDown(SmartTargetSettings.HoldKeys[Config.SmartTarget.HoldKey]);
	private TargetMemoryWriter? _targetWriter;

	private bool _dispatchingCastTurn;

	private string? _lastCastTurnDiagnostic;

	private bool _dispatchingStamina;

	private string? _lastStaminaSafety;

	private bool _punishWasArmed;

	private string? _lastTurnSafetyReason;

	private string? _lastPunishSafetyReason;

	private string? _lastTurnDecision;

	private BuffPresence? _lastBuffPresence;

	private string? _lastBuffNames;

	private string _connectionStatus = "Desconectado";

	private volatile bool _overlayEnabled;

	private AppFeature SubFeature => AppFeature.SubPunishLegacy;

	private int AccessMask => Enum.GetValues<AppFeature>().Aggregate(0, (int mask, AppFeature feature) => (!_access.Allows(feature)) ? mask : (mask | (1 << (int)feature)));

	public AutoAssistConfig Config { get; }

	public MemoryLogger MemoryLog { get; } = new MemoryLogger(400);

	public IAutoAssistLogger Log { get; }

	public ReadOnlyGameStateProvider? Provider { get; private set; }

	public AutoTurningEngine? AutoTurning { get; private set; }

	public ILivePunishEngine? Punish { get; private set; }

	public AutoAttackToolEngine? Combat { get; private set; }

	public AutoFollowEngine? Follow { get; private set; }

	public AutoStaminaEngine? Stamina { get; private set; }

	public AutoCastTurnEngine? CastTurn { get; private set; }

	public AutoTargetEngine? AutoTarget { get; private set; }
	public AutoTargetEngine? SmartTarget { get; private set; }

	public NavigationFrame? Navigation
	{
		get
		{
			NavigationFrame? navigation = Volatile.Read(in _navigation);
			LiveFrame? frame = Frame;
			return navigation != null && frame != null && navigation.Map == frame.Map &&
				_clock.NowMs >= navigation.CapturedAtMs && _clock.NowMs - navigation.CapturedAtMs <= 1500
				? navigation : null;
		}
	}

	public MapPlayersFrame? MapPlayers
	{
		get
		{
			if (_access.Allows(AppFeature.MapPlayers))
			{
				MapPlayersFrame mapPlayersFrame = Volatile.Read(in _mapPlayers);
				if ((object)mapPlayersFrame != null && mapPlayersFrame.IsCurrent(Frame, _clock.NowMs))
				{
					return mapPlayersFrame;
				}
			}
			return null;
		}
	}

	public FullMapFrame? FullMap
	{
		get
		{
			FullMapFrame? map = Provider?.FullMap;
			LiveFrame? frame = Frame;
			return map != null && frame != null && map.Map == frame.Map ? map : null;
		}
	}

	public string FullMapStatus => Provider?.FullMapStatus ?? "Desconectado / mapa indisponível";

	public string MapPlayersStatus
	{
		get
		{
			MapPlayersFrame mapPlayers = MapPlayers;
			object obj;
			if ((object)mapPlayers == null)
			{
				obj = Provider?.MapPlayersStatus;
				if (obj == null)
				{
					return "Desconectado / lista indisponível";
				}
			}
			else
			{
				obj = $"Mapa {mapPlayers.Map}: {mapPlayers.Players.Count} jogadores registrados · falhas de leitura: {mapPlayers.UnreadableSlots}";
			}
			return (string)obj;
		}
	}

	public string CastTurnStatus { get; private set; } = "Auto Cast Turn OFF";

	public string AutoTargetStatus { get; private set; } = "Auto Target OFF";
	public string SmartTargetStatus { get; private set; } = "Smart Target OFF";

	public bool CounterAttackAuthorized => _authorized;

	private bool PunishComboApproved
	{
		get
		{
			ReadOnlyGameStateProvider? provider = Provider;
			if (provider == null)
			{
				return true;
			}
			return provider.Profile.PunishComboValidated != false;
		}
	}

	private bool StrictSubTurnProfile
	{
		get
		{
			GameMemoryProfile gameMemoryProfile = Provider?.Profile;
			if ((object)gameMemoryProfile != null)
			{
				if (gameMemoryProfile.PunishComboValidated != false)
				{
					return gameMemoryProfile.PunishComboRequiresTarget == true;
				}
				return true;
			}
			return false;
		}
	}

	public string Status => Provider?.Status ?? _connectionStatus;

	public string TurningSafetyStatus { get; private set; } = "Auto Turning OFF";

	public string PunishSafetyStatus { get; private set; } = "SubPunish OFF";

	public string TurnStatus { get; private set; } = "Nenhum giro solicitado";

	public string CombatStatus { get; private set; } = "AA/Tool OFF";

	public string FollowStatus { get; private set; } = "Follow OFF";

	public string StaminaStatus { get; private set; } = "Auto Stamina OFF";

	public bool StaminaBindingValid => _input?.StaminaBindingValid == true;

	public string StaminaBindingProblem => _input?.StaminaBindingProblem ?? "Corrida não validada";

	private bool SubNeedsPriority
	{
		get
		{
			ILivePunishEngine? punish = Punish;
			if (punish == null || !punish.BlocksAutoTurning)
			{
				if (_subEnabled && CounterAttackAuthorized)
				{
					return Punish?.HasUnprocessedRemoval(Volatile.Read(in _frame)) ?? false;
				}
				return false;
			}
			return true;
		}
	}

	public string? ActionStatus => _input?.LastActionText;

	public LiveBuffObservation BuffObservation => LiveBuffObservation.FromFrame(Volatile.Read(in _frame), _clock.NowMs);

	public LiveFrame? Frame
	{
		get
		{
			LiveFrame liveFrame = Volatile.Read(in _frame);
			if ((object)liveFrame == null || _clock.NowMs - liveFrame.CapturedAtMs > 500)
			{
				return null;
			}
			return liveFrame;
		}
	}

	public long ObservationNowMs => _clock.NowMs;

	public void BindCharacterSettings(string verifiedName)
	{
		if (Frame?.PlayerName != verifiedName)
		{
			throw new InvalidOperationException("Identidade local não confirmada.");
		}
		Volatile.Write(ref _settingsBoundToCharacter, verifiedName);
	}

	public void SetOverlayEnabled(bool enabled)
	{
		_overlayEnabled = enabled;
	}

	public GameSnapshot? Snapshot()
	{
		return Frame?.Snapshot;
	}

	public LiveContext(AutoAssistConfig config, AccessLease access, bool detectCharacterAutomatically = false)
	{
		LiveContext liveContext = this;
		_access = access;
		if (!access.IsValid)
		{
			throw new AccessDeniedException("Login obrigatório antes da conexão.");
		}
		_accessMask = AccessMask;
		Config = config;
		IAutoAssistLogger autoAssistLogger;
		if (!config.General.Logging)
		{
			IAutoAssistLogger memoryLog = MemoryLog;
			autoAssistLogger = memoryLog;
		}
		else
		{
			IAutoAssistLogger memoryLog = new CompositeLogger(MemoryLog, new FileLogger(AppPaths.Settings.ResolveLogPath(config.General.LogFile)));
			autoAssistLogger = memoryLog;
		}
		Log = autoAssistLogger;
		_authorized = false; // Only the current session's SubPunish ON button can authorize a combo.
		try
		{
			Process[] processesByName = Process.GetProcessesByName("NinOnline");
			int id;
			try
			{
				if (processesByName.Length != 1)
				{
					throw new InvalidOperationException("Abra exatamente um cliente NinOnline para conectar.");
				}
				id = processesByName[0].Id;
			}
			finally
			{
				Process[] array = processesByName;
				for (int i = 0; i < array.Length; i++)
				{
					array[i].Dispose();
				}
			}
			Provider = new ReadOnlyGameStateProvider(id, detectCharacterAutomatically ? "" : config.Live.CharacterName, _clock);
			Log.Log(LogLevel.Info, $"Perfil detectado: {Provider.Profile.Id}; instalação: {Provider.GameDirectory}; hashes do par verificados");
			if (Provider.Profile.ExperimentalTrial)
			{
				Log.Log(LogLevel.Warning, "ENSAIO LOCAL NÃO VALIDADO: inputs exigem permissões, ativação manual, foco no jogo e F12 para parar.");
			}
			else if (!Provider.Profile.ActuationValidated)
			{
				Log.Log(LogLevel.Warning, "Perfil externo apenas para leitura: automação bloqueada até validação passiva e assinatura de um perfil aprovado.");
			}
			LiveKeySettings.Validate(config.Live.PunishKeys, config.Live.TurnActivationKeys);
			_input = new LiveInputController(CanDispatch, Log, Provider.GameDirectory, config.Live.PunishKeys, config.Live.TurnActivationKeys,
				PrepareComboFacing, config.CombatAssist,
				Provider.Profile.ValidatedInputKinds.Contains(nameof(LiveInputKind.Follow), StringComparer.Ordinal) &&
				LocalTrialPolicy.CanActuate(Provider.Profile));
			_comboFacing = new ComboFacingCoordinator(_input, _clock, () => liveContext.Frame, () => liveContext.CheckSafety(LiveInputKind.Turning).Allowed, null, Log);
			CastTurn = new AutoCastTurnEngine(_clock, config.AutoCastTurn, delegate(Direction direction)
			{
				liveContext._dispatchFrame = liveContext.Frame;
				liveContext._dispatchingCastTurn = true;
				try
				{
					TurnResult turnResult = liveContext._input.RequestCastTurn(direction, config.AutoCastTurn.LeadTimeMs + 70);
					liveContext.Log.Log(turnResult.Accepted ? LogLevel.Info : LogLevel.Warning, $"Auto Cast Turn: {direction} · {turnResult.Reason}; efeito/aceitação da magia não confirmados", EventOrigin.Requested);
					return turnResult;
				}
				finally
				{
					liveContext._dispatchingCastTurn = false;
				}
			});
			_targetWriter = new TargetMemoryWriter();
			AutoTarget = new AutoTargetEngine(_clock, config.AutoTarget,
				target => liveContext.RequestTargetMemory(target, LiveInputKind.TargetMemory, config.AutoTarget));
			config.SmartTarget.Validate();
			AutoTargetSettings smartSelection = config.SmartTarget.Selection();
			SmartTarget = new AutoTargetEngine(_clock, smartSelection,
				target => liveContext.RequestTargetMemory(target, LiveInputKind.SmartTargetMemory, smartSelection), "Smart Target");
			AutoTurning = new AutoTurningEngine(_input, _clock, Log, config.ToAutoTurningOptions()with
			{
				Enabled = false,
				DiagnosticMode = true,
				MinCommandIntervalMs = 40L,
				ManualMovementPriority = false,
				Geometry = new DirectionCalculatorOptions
				{
					PositionTolerance = 0.1,
					HysteresisRatio = 1.25
				}
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
				SequenceTimeoutMs = config.Live.PunishSequenceTimeoutMs,
				EligibleSources = new string[2] { "Speed Mirage Technique", "Water Substitution Technique" }
			}, Log);
			Log.Log(LogLevel.Info, "Modo escolhido: " + LivePunishFactory.Name(config.Live.PunishMode) + "; motores mutuamente exclusivos; início OFF");
			Combat = new AutoAttackToolEngine(_clock, config.CombatAssist, DispatchCombat, PrepareFanAttack);
			Follow = new AutoFollowEngine(_clock, config.CombatAssist, DispatchFollow, delegate
			{
				liveContext._input.ReleaseFollow();
			});
			_input.RefreshFollowDuringTap = UpdateFollow;
			Stamina = new AutoStaminaEngine(_clock, delegate(bool desired)
			{
				liveContext._dispatchFrame = liveContext.Frame;
				liveContext._dispatchingStamina = true;
				try
				{
					ActionResult actionResult = liveContext._input.ToggleRun();
					liveContext.Log.Log(actionResult.Accepted ? LogLevel.Info : LogLevel.Warning, $"Auto Stamina solicitou corrida {(desired ? "ON" : "OFF")}; SP={liveContext.Frame?.Stamina?.Percent:0.0}%; {actionResult.Reason}", EventOrigin.Requested);
					return actionResult;
				}
				finally
				{
					liveContext._dispatchingStamina = false;
				}
			}, config.CombatAssist.StaminaDisableBelowPercent, config.CombatAssist.StaminaEnableAbovePercent);
			Log.Log(LogLevel.Info, $"Live connection: PID {id}, character {(detectCharacterAutomatically ? "detecção automática pelo índice local" : config.Live.CharacterName)}; modules OFF; Turn activation: {string.Join(" / ", config.Live.TurnActivationKeys)}; SubPunish somente Tai/Água; Sub normal (decoy) nunca dispara combo; combo {string.Join(" → ", config.Live.PunishKeys)} {(CounterAttackAuthorized ? "authorized" : "DISABLED")}");
			Log.Log(LogLevel.Info, $"AA/Tool OFF e desautorizado: {config.CombatAssist.AttackKey} {CombatGeometry.DescribeAttack(config.CombatAssist)} / Tool {config.CombatAssist.ToolKey} frontal <= {config.CombatAssist.ToolRange} tiles; Follow OFF, tecla {config.CombatAssist.FollowHoldKey}");
			Log.Log(LogLevel.Info, $"Auto Stamina OFF; binding válido={_input.StaminaBindingValid}; {_input.StaminaBindingStatus}; {_input.StaminaBindingProblem}");
			_automation = new ResponsiveAutomationLoop(Step, delegate(Exception ex2)
			{
				liveContext._autoEnabled = (liveContext._subEnabled = (liveContext._authorized = (liveContext._combatEnabled = (liveContext._combatAuthorized = (liveContext._followEnabled = false)))));
				liveContext._staminaEnabled = (liveContext._castTurnEnabled = (liveContext._autoTargetEnabled = (liveContext._smartTargetEnabled = false)));
				liveContext._attackEnabled = liveContext._toolEnabled = false;
				lock (liveContext._automationLock)
				{
					liveContext._input?.ReleaseFollow();
					liveContext.ResetPunish();
					liveContext.AutoTurning?.ResetCadence();
				}
				liveContext.Log.Log(LogLevel.Error, "Automação interrompida; reconecte: " + ex2.Message);
			}, delegate
			{
				lock (liveContext._automationLock)
				{
					long? num = liveContext.Punish?.NextKeyDueAtMs;
					long? result;
					if (num.HasValue)
					{
						long valueOrDefault = num.GetValueOrDefault();
						result = valueOrDefault - liveContext._clock.NowMs;
					}
					else
					{
						result = null;
					}
					return result;
				}
			});
			_reader = Task.Run(async delegate
			{
				try
				{
					while (!liveContext._stop.IsCancellationRequested)
					{
						bool flag = liveContext._access.Allows(AppFeature.Follow) && liveContext._followEnabled && LiveInputController.IsDown(LiveKeySettings.Keys[liveContext.Config.CombatAssist.FollowHoldKey]);
						int num;
						if (liveContext._castTurnEnabled)
						{
							LiveFrame? frame = liveContext.Provider.Frame;
							num = (((object)frame != null && frame.Cast?.Active == true) ? 1 : 0);
						}
						else
						{
							num = 0;
						}
						bool flag2 = (byte)num != 0;
						NavigationFrame navigationFrame = Volatile.Read(in liveContext._navigation);
						liveContext.Provider.ReadNavigation = !liveContext._subReadBurst && !flag2 && flag && ((object)navigationFrame == null || navigationFrame.Map != Volatile.Read(in liveContext._frame)?.Map || liveContext._clock.NowMs - navigationFrame.CapturedAtMs >= 100);
						MapPlayersFrame mapPlayersFrame = Volatile.Read(in liveContext._mapPlayers);
						int playerRefreshMs = (liveContext._autoTargetEnabled || (liveContext._smartTargetEnabled && liveContext.SmartHoldDown)) ? 100 :
							(liveContext._castTurnEnabled || liveContext._combatEnabled) ? 350 : liveContext._overlayEnabled ? 100 : 200;
						liveContext.Provider.ReadMapPlayers = !liveContext._subReadBurst && (!flag2 || (liveContext._castTurnEnabled && liveContext.Config.AutoCastTurn.TargetMode == CastTurnTargetMode.Nearest)) && liveContext._access.Allows(AppFeature.MapPlayers) && ((object)mapPlayersFrame == null || mapPlayersFrame.Map != Volatile.Read(in liveContext._frame)?.Map || liveContext._clock.NowMs - mapPlayersFrame.CapturedAtMs >= playerRefreshMs);
						liveContext.Provider.ReadFullMap = !liveContext._subReadBurst && !flag2 && liveContext._access.Allows(AppFeature.MapPlayers) && liveContext.Provider.FullMap?.Map != Volatile.Read(in liveContext._frame)?.Map;
						if (!liveContext._access.Allows(AppFeature.MapPlayers))
						{
							Volatile.Write(ref liveContext._mapPlayers, null);
						}
						liveContext.Provider.GetSnapshot();
						if (liveContext.Provider.ReadMapPlayers)
						{
							Volatile.Write(ref liveContext._mapPlayers, liveContext.Provider.MapPlayers);
						}
						LiveFrame frame3 = liveContext.Provider.Frame;
						if ((object)frame3 != null && liveContext._access.Allows(AppFeature.MapPlayers))
						{
							MapPlayersFrame mapPlayersFrame2 = Volatile.Read(in liveContext._mapPlayers);
							if ((object)mapPlayersFrame2 != null && mapPlayersFrame2.Map == frame3.Map && mapPlayersFrame2.LocalId == frame3.Snapshot.Player.Id && mapPlayersFrame2.CapturedAtMs <= frame3.CapturedAtMs && frame3.CapturedAtMs - mapPlayersFrame2.CapturedAtMs <= 600)
							{
								frame3 = frame3 with
								{
									MapPlayers = mapPlayersFrame2
								};
							}
						}
						Volatile.Write(ref liveContext._frame, frame3);
						if ((object)frame3 == null)
						{
							Volatile.Write(ref liveContext._mapPlayers, null);
						}
						if (liveContext.Provider.ReadNavigation)
						{
							Volatile.Write(ref liveContext._navigation, liveContext.Provider.Navigation);
						}
						else if (!flag)
						{
							Volatile.Write(ref liveContext._navigation, null);
						}
						liveContext._automation.Wake();
						int num2;
						if (LiveInputController.IsGameForeground(liveContext.Provider.ProcessId))
						{
							if (liveContext._castTurnEnabled)
							{
								LiveFrame? frame2 = liveContext.Provider.Frame;
								if ((object)frame2 != null && frame2.Cast?.Active == true)
								{
									num2 = 1;
									goto IL_04a6;
								}
							}
							num2 = ((liveContext._combatEnabled || flag || (liveContext._subEnabled && liveContext.CounterAttackAuthorized && ((liveContext.Provider.Frame?.ObservedActiveBuffs.Any((ObservedBuff b) => SubstitutionBuffs.IsPunishEligible(b.Name)) ?? false) || liveContext._subReadBurst)) || (liveContext._autoEnabled && liveContext.Config.Live.TurnActivationKeys.Any((string k) => LiveInputController.IsDown(LiveKeySettings.Keys[k])))) ? 1 : 0);
						}
						else
						{
							num2 = 0;
						}
						goto IL_04a6;
						IL_04a6:
						if (num2 == 0 || (object)liveContext.Provider.Frame == null)
						{
							await Task.Delay(5, liveContext._stop.Token).ConfigureAwait(continueOnCapturedContext: false);
						}
						else
						{
							Thread.Yield();
						}
					}
				}
				catch (OperationCanceledException)
				{
				}
				finally
				{
					liveContext.Provider.Dispose();
				}
			});
		}
		catch (Exception ex)
		{
			_automation?.Dispose();
			_targetWriter?.Dispose();
			_automation = null;
			_connectionStatus = ex.Message;
			Provider?.Dispose();
			Provider = null;
			Log.Log(LogLevel.Error, "Conexão recusada: " + ex.Message);
		}
	}

	private ActionResult RequestTargetMemory(TargetInfo target, LiveInputKind kind, AutoTargetSettings settings)
	{
		_dispatchFrame = Frame;
		if (_dispatchFrame is null)
			return ActionResult.Rejected("Leitura indisponível para seleção");
		_dispatchTargetKind = kind;
		_dispatchingTargetMemory = true;
		try
		{
			LiveFrame? current = Frame;
			TargetInfo? nearest = NearestTargetPolicy.Resolve(current, settings.RadiusTiles, settings.IgnoreSameVillage);
			if (current is null || nearest?.Id != target.Id ||
				Math.Abs(nearest.PositionX - target.PositionX) > 0.25 ||
				Math.Abs(nearest.PositionY - target.PositionY) > 0.25)
				return ActionResult.Rejected("Alvo mudou desde a leitura anterior; sem escrita");
			long startedAt = Stopwatch.GetTimestamp();
			ActionResult result = _targetWriter!.Select(Provider!.ProcessId, Provider.Profile,
				current, nearest, () => CanDispatch(kind));
			Log.Log(result.Accepted ? LogLevel.Info : LogLevel.Warning,
				(kind == LiveInputKind.SmartTargetMemory ? "Smart Target: " : "Auto Target: ") + result.Reason +
				$"; escrita/validação {Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:0} ms",
				EventOrigin.Requested);
			return result;
		}
		finally { _dispatchingTargetMemory = false; }
	}

	public void SetAutoTurningEnabled(bool enabled)
	{
		enabled &= _access.Allows(AppFeature.AutoTurning);
		if (!enabled)
		{
			_autoEnabled = false;
		}
		lock (_automationLock)
		{
			if (_disposed)
			{
				return;
			}
			_autoEnabled = enabled;
			AutoTurning?.UpdateOptions(AutoTurning.Options with
			{
				Enabled = false
			});
			if (!enabled)
			{
				AutoTurning?.ResetCadence();
				_lastTurnCapture = -1L;
			}
			Log.Log(LogLevel.Info, "Live Auto Turning " + (enabled ? "ON" : "OFF"), EventOrigin.Requested);
		}
		_automation?.Wake();
	}

	public void SetSubPunishEnabled(bool enabled)
	{
		enabled &= _access.Allows(SubFeature);
		if (!enabled)
		{
			_subEnabled = false;
			_authorized = false;
		}
		lock (_automationLock)
		{
			if (_disposed)
			{
				return;
			}
			_subEnabled = enabled;
			_authorized = MacroAuthorization.SubPunish(enabled, _access.Allows(SubFeature), PunishComboApproved);
			ResetPunish();
			Log.Log(LogLevel.Info, "Live Punish " + (enabled ? "ON" : "OFF") + "; F manual; remoção antecipada HEURÍSTICA; combo " + (CounterAttackAuthorized ? "authorized" : "DISABLED"), EventOrigin.Requested);
		}
		_automation?.Wake();
	}

	public bool SetAttackEnabled(bool enabled) => SetCombatFeatureEnabled(AppFeature.AutoAttack, enabled);

	public bool SetToolEnabled(bool enabled) => SetCombatFeatureEnabled(AppFeature.Tool, enabled);

	private bool SetCombatFeatureEnabled(AppFeature feature, bool enabled)
	{
		GameMemoryProfile? profile = Provider?.Profile;
		enabled &= _access.Allows(feature) && LocalTrialPolicy.CanActuate(profile) &&
			(!profile.ExternalProfile || profile.ValidatedInputKinds.Contains(feature == AppFeature.AutoAttack ? "Attack" : "Tool", StringComparer.Ordinal)) &&
			(feature != AppFeature.AutoAttack || profile.ApprovesAttackMode(Config.CombatAssist.AttackMode.ToString()));
		lock (_automationLock)
		{
			if (_disposed)
			{
				return false;
			}
			Combat?.Reset();
			_fanTurnAt = null;
			_fanTurnTarget = null;
			if (feature == AppFeature.AutoAttack) _attackEnabled = enabled;
			else _toolEnabled = enabled;
			_combatEnabled = _combatAuthorized = _attackEnabled || _toolEnabled;
			Log.Log(LogLevel.Info, $"{feature} {(enabled ? "ON" : "OFF")}; módulo independente; alcance/intervalo configurados", EventOrigin.Requested);
		}
		_automation?.Wake();
		return enabled;
	}

	public void SetFollowEnabled(bool enabled)
	{
		enabled &= _access.Allows(AppFeature.Follow);
		if (!enabled)
		{
			_followEnabled = false;
		}
		lock (_automationLock)
		{
			if (_disposed)
			{
				return;
			}
			Follow?.Reset();
			_dispatchingFollow = null;
			_followFrame = null;
			_followEnabled = enabled;
			Log.Log(LogLevel.Info, $"Follow {(enabled ? "ON" : "OFF")}; exige tecla {Config.CombatAssist.FollowHoldKey} e navegação válida", EventOrigin.Requested);
		}
		_automation?.Wake();
	}

	public void SetStaminaEnabled(bool enabled)
	{
		enabled &= _access.Allows(AppFeature.Stamina);
		if (!enabled)
		{
			_staminaEnabled = false;
		}
		lock (_automationLock)
		{
			if (_disposed)
			{
				return;
			}
			Stamina?.Reset();
			_staminaEnabled = enabled;
			Log.Log(LogLevel.Info, $"Auto Stamina {(enabled ? "ON" : "OFF")}: OFF <{Config.CombatAssist.StaminaDisableBelowPercent}%, ON >{Config.CombatAssist.StaminaEnableAbovePercent}%; {_input?.StaminaBindingStatus}", EventOrigin.Requested);
		}
		_automation?.Wake();
	}

	public bool SetCastTurnEnabled(bool enabled)
	{
		enabled &= _access.Allows(AppFeature.AutoCastTurn) && (object)Provider?.Profile.CastTiming != null &&
			(Config.AutoCastTurn.TargetMode != CastTurnTargetMode.Nearest || _access.Allows(AppFeature.MapPlayers));
		if (!enabled)
		{
			_castTurnEnabled = false;
		}
		lock (_automationLock)
		{
			if (_disposed)
			{
				return false;
			}
			CastTurn?.Reset();
			_castTurnEnabled = enabled;
			Log.Log(LogLevel.Info, "Auto Cast Turn " + (enabled ? "ON" : "OFF") + "; alvo " + Config.AutoCastTurn.TargetMode + "; todos os casts válidos exceto IDs excluídos: " + string.Join(", ", Config.AutoCastTurn.ExcludedSpellIds), EventOrigin.Requested);
		}
		_automation?.Wake();
		return enabled;
	}

	public bool SetAutoTargetEnabled(bool enabled)
	{
		if (enabled && _smartTargetEnabled)
		{
			Log.Log(LogLevel.Warning, "Auto Target: desligue Smart Target antes de ligar este módulo", EventOrigin.Requested);
			return false;
		}
		enabled &= _access.Allows(AppFeature.AutoTarget) && _access.Allows(AppFeature.MapPlayers) && !_smartTargetEnabled;
		if (!enabled)
		{
			_autoTargetEnabled = false;
		}
		lock (_automationLock)
		{
			if (_disposed)
			{
				return false;
			}
			AutoTarget?.Reset();
			_autoTargetEnabled = enabled;
			Log.Log(LogLevel.Info, "Auto Target " + (enabled ? "ON" : "OFF") + $"; escrita experimental; raio {Config.AutoTarget.RadiusTiles:0.#} tiles", EventOrigin.Requested);
		}
		_automation?.Wake();
		return enabled;
	}

	public bool SetSmartTargetEnabled(bool enabled)
	{
		if (enabled && _autoTargetEnabled)
		{
			Log.Log(LogLevel.Warning, "Smart Target: desligue Auto Target antes de ligar este módulo", EventOrigin.Requested);
			return false;
		}
		enabled &= _access.Allows(AppFeature.SmartTarget) && _access.Allows(AppFeature.MapPlayers) && !_autoTargetEnabled;
		if (!enabled) _smartTargetEnabled = false;
		lock (_automationLock)
		{
			if (_disposed) return false;
			SmartTarget?.Reset();
			_smartTargetEnabled = enabled;
			Log.Log(LogLevel.Info, $"Smart Target {(enabled ? "ON" : "OFF")}; segure {Config.SmartTarget.HoldKey} para selecionar; soltar mantém o alvo", EventOrigin.Requested);
		}
		_automation?.Wake();
		return enabled;
	}

	private SafetyDecision CheckSafety(LiveInputKind kind)
	{
		if (!LocalTrialPolicy.CanActuate(Provider?.Profile))
		{
			return new SafetyDecision(Allowed: false, "Perfil de jogo não aprovado para enviar teclas");
		}
		string profileKind = kind == LiveInputKind.SmartTargetMemory ? nameof(LiveInputKind.TargetMemory) : kind.ToString();
		if (Provider.Profile.ExternalProfile && !Provider.Profile.ValidatedInputKinds.Contains(profileKind, StringComparer.Ordinal))
		{
			return new SafetyDecision(Allowed: false, $"{kind}: sinal ainda não validado para esta build");
		}
		if (kind == LiveInputKind.Punish && !PunishComboApproved)
		{
			return new SafetyDecision(Allowed: false, "Combo SubPunish bloqueado neste perfil de giro experimental");
		}
		if (kind == LiveInputKind.Punish)
		{
			ILivePunishEngine? punish = Punish;
			if (punish != null && punish.State == EarlyPunishState.Executing && (Punish.TriggerSources.Count == 0 || Punish.TriggerSources.Any((string source) => !SubstitutionBuffs.IsPunishEligible(source))))
			{
				return new SafetyDecision(Allowed: false, "SubPunish exige remoção de Sub Tai ou Água; Sub normal não dispara combo");
			}
		}
		if (kind == LiveInputKind.Attack && !Provider.Profile.ApprovesAttackMode(Config.CombatAssist.AttackMode.ToString()))
		{
			return new SafetyDecision(Allowed: false, $"Attack: modo {Config.CombatAssist.AttackMode} não aprovado para esta build");
		}
		if (kind == LiveInputKind.Attack && Config.CombatAssist.AttackMode == AutoAttackMode.Fan && !Provider.Profile.ValidatedInputKinds.Contains<string>("Turning", StringComparer.Ordinal))
		{
			return new SafetyDecision(Allowed: false, "Fan exige aprovação de giro para esta build");
		}
		if ((kind is LiveInputKind.TargetMemory or LiveInputKind.SmartTargetMemory || (kind == LiveInputKind.CastTurning && Config.AutoCastTurn.TargetMode == CastTurnTargetMode.Nearest)) && !_access.Allows(AppFeature.MapPlayers))
		{
			return new SafetyDecision(Allowed: false, "Lista de jogadores não autorizada pelo Firebase");
		}
		AppFeature feature = kind switch
		{
			LiveInputKind.Attack => AppFeature.AutoAttack,
			LiveInputKind.Tool => AppFeature.Tool,
			LiveInputKind.Follow => AppFeature.Follow,
			LiveInputKind.Stamina => AppFeature.Stamina,
			LiveInputKind.Punish => SubFeature,
			LiveInputKind.CastTurning => AppFeature.AutoCastTurn,
			LiveInputKind.TargetMemory => AppFeature.AutoTarget,
			LiveInputKind.SmartTargetMemory => AppFeature.SmartTarget,
			_ => AppFeature.AutoTurning,
		};
		if (kind != LiveInputKind.Turning && !_access.Allows(feature))
		{
			return new SafetyDecision(Allowed: false, "Recurso não autorizado pelo Firebase");
		}
		LiveFrame liveFrame = Volatile.Read(in _frame);
		if ((object)liveFrame == null || Volatile.Read(in _settingsBoundToCharacter) != liveFrame.PlayerName)
		{
			return new SafetyDecision(Allowed: false, "Configuração do personagem ainda não verificada; inputs OFF");
		}
		if ((Provider.Profile.ExperimentalTrial ||
			Provider.Profile is { Id: "Nin Online Steam 2026-11-26 (validated core; restricted)",
				Sha256: "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525",
				CommonSha256: "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102" }) &&
			(liveFrame.Map != 190 || liveFrame.PlayerName != "Raizen Kaen"))
		{
			return new SafetyDecision(Allowed: false, "Perfil restrito a Raizen no mapa 190");
		}
		bool flag = Provider.Profile.ExperimentalTrial && liveFrame.Snapshot.Player.Facing == Direction.None;
		if (flag)
		{
			bool flag2 = (uint)kind <= 2u;
			flag = flag2;
		}
		if (flag)
		{
			return new SafetyDecision(Allowed: false, "Ensaio de giro/combate exige direção cardinal observada");
		}
		long nowMs = _clock.NowMs;
		bool foreground = Provider != null && LiveInputController.IsGameForeground(Provider.ProcessId);
		bool emergency = LiveInputController.EmergencyPending();
		bool textEntryFocused = LiveInputController.TextEntryFocused();
		if (kind == LiveInputKind.Punish)
		{
			ILivePunishEngine? punish2 = Punish;
			if (punish2 != null && punish2.State == EarlyPunishState.Executing)
			{
				ReadOnlyGameStateProvider? provider = Provider;
				if (provider != null && provider.Profile.PunishComboRequiresTarget == true)
				{
					TargetInfo targetInfo = liveFrame?.Snapshot.Target;
					if ((object)targetInfo == null || !targetInfo.IsAlive || !targetInfo.IsTargeted)
					{
						return new SafetyDecision(Allowed: false, "Combo exige jogador selecionado e vivo nesta build");
					}
				}
			}
		}
		switch (kind)
		{
		case LiveInputKind.CastTurning:
			return LiveSafetyGate.CheckCastTurning(liveFrame, nowMs, _castTurnEnabled, foreground, emergency, textEntryFocused, Config.AutoCastTurn.TargetMode == CastTurnTargetMode.Selected);
		case LiveInputKind.TargetMemory:
			return LiveSafetyGate.CheckAutoTarget(liveFrame, nowMs, _autoTargetEnabled, foreground, emergency, textEntryFocused);
		case LiveInputKind.SmartTargetMemory:
			return LiveSafetyGate.CheckAutoTarget(liveFrame, nowMs, _smartTargetEnabled && SmartHoldDown, foreground, emergency, textEntryFocused);
		case LiveInputKind.Stamina:
		{
			SafetyDecision result = LiveSafetyGate.CheckStamina(liveFrame, nowMs, _staminaEnabled, foreground, emergency, textEntryFocused);
			if (result.Allowed)
			{
				LiveInputController? input = _input;
				if (input == null || !input.StaminaBindingValid)
				{
					return new SafetyDecision(Allowed: false, _input?.StaminaBindingProblem ?? "Run indisponível");
				}
			}
			return result;
		}
		case LiveInputKind.Attack:
		case LiveInputKind.Tool:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return LiveSafetyGate.CheckTargetedModule(liveFrame, nowMs, kind == LiveInputKind.Attack ? _attackEnabled : _toolEnabled, _combatAuthorized, foreground, emergency, textEntryFocused);
		}
		switch (kind)
		{
		case LiveInputKind.Follow:
		{
			SafetyDecision result2 = LiveSafetyGate.CheckTargetedModule(liveFrame, nowMs, _followEnabled, authorized: true, foreground, emergency, textEntryFocused);
			if (!result2.Allowed)
			{
				return result2;
			}
			if (!LiveInputController.IsDown(LiveKeySettings.Keys[Config.CombatAssist.FollowHoldKey]))
			{
				return new SafetyDecision(Allowed: false, "Follow: segure a tecla configurada");
			}
			NavigationFrame navigationFrame = Volatile.Read(in _navigation);
			if ((object)navigationFrame == null || navigationFrame.Map != liveFrame.Map || nowMs < navigationFrame.CapturedAtMs || nowMs - navigationFrame.CapturedAtMs > 500 || !navigationFrame.DynamicHazardsKnown)
			{
				return new SafetyDecision(Allowed: false, Provider?.NavigationStatus ?? "Navegação ausente");
			}
			GridPoint gridPoint = new GridPoint((int)Math.Floor(liveFrame.Snapshot.Player.PositionX), (int)Math.Floor(liveFrame.Snapshot.Player.PositionY));
			if (navigationFrame.Cell(gridPoint.X, gridPoint.Y) == NavigationCell.Unknown)
			{
				return new SafetyDecision(Allowed: false, "Posição fora do terreno observado");
			}
			return result2;
		}
		case LiveInputKind.Turning:
			if (StrictSubTurnProfile && (object)liveFrame != null && liveFrame.Snapshot.Player.Facing == Direction.None)
			{
				return new SafetyDecision(Allowed: false, "Giro Sub: direção local não observada");
			}
			if (_preparingFanTurn)
			{
				ReadOnlyGameStateProvider? provider2 = Provider;
				if (provider2 == null || !provider2.Profile.ApprovesAttackMode(Config.CombatAssist.AttackMode.ToString()) || !_access.Allows(AppFeature.AutoAttack))
				{
					return new SafetyDecision(Allowed: false, "Giro Fan sem aprovação de Attack");
				}
				return LiveSafetyGate.CheckTargetedModule(liveFrame, nowMs, _attackEnabled, _combatAuthorized, foreground, emergency, textEntryFocused);
			}
			if (_preparingComboTurn)
			{
				SafetyDecision safetyDecision = CheckSafety(LiveInputKind.Punish);
				ILivePunishEngine? punish3 = Punish;
				if (punish3 == null || punish3.State != EarlyPunishState.Executing || !safetyDecision.Allowed)
				{
					return new SafetyDecision(Allowed: false, safetyDecision.Allowed ? "Combo não está executando" : safetyDecision.Reason);
				}
				return LiveSafetyGate.CheckTargetedModule(liveFrame, nowMs, _subEnabled, CounterAttackAuthorized, foreground, emergency, textEntryFocused);
			}
			return LiveSafetyGate.CheckTurning(liveFrame, nowMs, _autoEnabled && _access.Allows(AppFeature.AutoTurning), foreground, Config.Live.TurnActivationKeys.Any((string k) => LiveInputController.IsDown(LiveKeySettings.Keys[k])), emergency, manualMovement: false, textEntryFocused);
		default:
			return new SafetyDecision(Allowed: false, "Finalidade de input desconhecida");
		case LiveInputKind.Punish:
			return LiveSafetyGate.CheckPunish(liveFrame, nowMs, _subEnabled, CounterAttackAuthorized, foreground, emergency, manualMovement: false, textEntryFocused);
		}
	}

	private bool CanDispatch(LiveInputKind kind)
	{
		LiveFrame liveFrame = ((kind == LiveInputKind.Follow) ? _followFrame : _dispatchFrame);
		LiveFrame liveFrame2;
		bool num;
		if (!_disposed && CheckSafety(kind).Allowed)
		{
			liveFrame2 = Volatile.Read(in _frame);
			if ((object)liveFrame2 != null && (object)liveFrame != null && liveFrame2.Map == liveFrame.Map && !(liveFrame2.Snapshot.Player.Id != liveFrame.Snapshot.Player.Id))
			{
				if ((uint)(kind - 2) <= 1u)
				{
					if (_dispatchingCombat == kind && !SubNeedsPriority && ((kind == LiveInputKind.Attack && Config.CombatAssist.AttackMode == AutoAttackMode.Melee) || CombatDispatchFreshness.Allows(liveFrame2, _clock.NowMs)) && liveFrame2.Snapshot.Player.Facing == liveFrame.Snapshot.Player.Facing && liveFrame2.Snapshot.Target?.Id == liveFrame.Snapshot.Target?.Id && CombatGeometry.InRange(liveFrame2, (kind != LiveInputKind.Attack) ? CombatAction.Tool : CombatAction.Attack, Config.CombatAssist))
					{
						if (kind == LiveInputKind.Attack && Config.CombatAssist.AttackMode == AutoAttackMode.Fan)
						{
							return CombatGeometry.AttackDirection(liveFrame2, Config.CombatAssist) == liveFrame2.Snapshot.Player.Facing;
						}
						return true;
					}
					return false;
				}
				if (kind == LiveInputKind.Turning && _preparingFanTurn)
				{
					if (!SubNeedsPriority)
					{
						AutoCastTurnEngine? castTurn = CastTurn;
						if ((castTurn == null || !castTurn.InFinalWindow) && liveFrame2.Snapshot.Target?.Id == liveFrame.Snapshot.Target?.Id && liveFrame2.Snapshot.Target?.Id == _fanTurnTarget && CombatGeometry.AttackDirection(liveFrame2, Config.CombatAssist) == _fanTurnDesired)
						{
							return liveFrame2.Snapshot.Player.Facing != _fanTurnDesired;
						}
					}
					return false;
				}
				switch (kind)
				{
				case LiveInputKind.Stamina:
					if (_dispatchingStamina && !SubNeedsPriority)
					{
						return Stamina?.NeedsToggle(liveFrame2) ?? false;
					}
					return false;
				case LiveInputKind.Punish:
				{
					ReadOnlyGameStateProvider? provider = Provider;
					if (provider != null && provider.Profile.PunishComboRequiresTarget == true)
					{
						TargetInfo target = liveFrame2.Snapshot.Target;
						if ((object)target == null || !target.IsAlive || !target.IsTargeted || target.Id != liveFrame.Snapshot.Target?.Id || (_comboTargetId != null && _comboTargetId != target.Id))
						{
							return false;
						}
						if (_comboTargetId == null)
						{
							_comboTargetId = target.Id;
							Log.Log(LogLevel.Info, "Combo restrito ao alvo selecionado " + target.Id + " nesta sequência", EventOrigin.Observed);
						}
					}
					break;
				}
				}
				if (kind == LiveInputKind.CastTurning)
				{
					if (_dispatchingCastTurn && !SubNeedsPriority)
					{
						LiveInputController? input = _input;
						if (input == null || !input.MaintainingCastTurn)
						{
							return CastTurn?.CanDispatch(liveFrame2, _clock.NowMs) ?? false;
						}
						return CastTurn?.CanMaintain(liveFrame2, _clock.NowMs) ?? false;
					}
					return false;
				}
				if (kind is LiveInputKind.TargetMemory or LiveInputKind.SmartTargetMemory)
				{
					if (!_dispatchingTargetMemory || _dispatchTargetKind != kind || SubNeedsPriority || liveFrame2.Cast?.Active == true ||
						(kind == LiveInputKind.SmartTargetMemory && !SmartHoldDown))
					{
						return false;
					}
					AutoTargetSettings settings = kind == LiveInputKind.SmartTargetMemory ? Config.SmartTarget.Selection() : Config.AutoTarget;
					TargetInfo? expected = NearestTargetPolicy.Resolve(liveFrame, settings.RadiusTiles, settings.IgnoreSameVillage);
					TargetInfo? current = NearestTargetPolicy.Resolve(liveFrame2, settings.RadiusTiles, settings.IgnoreSameVillage);
					return expected != null && current?.Id == expected.Id &&
						Math.Abs(current.PositionX - expected.PositionX) <= 0.25 &&
						Math.Abs(current.PositionY - expected.PositionY) <= 0.25 &&
						liveFrame2.Snapshot.Target?.Id != expected.Id;
				}
				if (kind == LiveInputKind.Follow)
				{
					FollowStep dispatchingFollow = _dispatchingFollow;
					if ((object)dispatchingFollow != null && !SubNeedsPriority && !(liveFrame2.Snapshot.Target?.Id != liveFrame.Snapshot.Target?.Id))
					{
						LiveInputController? input2 = _input;
						if (input2 != null && !input2.OtherMovementHeld(dispatchingFollow))
						{
							PlayerState player = liveFrame2.Snapshot.Player;
							NavigationFrame navigationFrame = Volatile.Read(in _navigation);
							GridPoint start = new GridPoint((int)Math.Floor(player.PositionX), (int)Math.Floor(player.PositionY));
							if ((object)navigationFrame == null || !navigationFrame.SafeStep(start, dispatchingFollow.From(start)))
							{
								return false;
							}
							TargetInfo target2 = liveFrame2.Snapshot.Target;
							return Math.Sqrt(Math.Pow(target2.PositionX - player.PositionX, 2.0) + Math.Pow(target2.PositionY - player.PositionY, 2.0)) > Config.CombatAssist.FollowStopDistance;
						}
					}
					return false;
				}
				if (kind != LiveInputKind.Turning)
				{
					ILivePunishEngine? punish = Punish;
					if (punish != null)
					{
						num = punish.State == EarlyPunishState.Executing;
						goto IL_0568;
					}
					goto IL_05b5;
				}
				if (!_preparingComboTurn)
				{
					ILivePunishEngine? punish2 = Punish;
					if (punish2 != null)
					{
						num = !punish2.BlocksAutoTurning;
						goto IL_0568;
					}
				}
				else
				{
					ILivePunishEngine? punish3 = Punish;
					if (punish3 == null || punish3.State != EarlyPunishState.Executing || !Punish.TriggerBuffsRemainAbsent(liveFrame2))
					{
						goto IL_05b5;
					}
					if (_comboTargetId != null)
					{
						num = _comboTargetId == liveFrame2.Snapshot.Target?.Id;
						goto IL_0568;
					}
				}
				goto IL_056a;
			}
		}
		return false;
		IL_056a:
		if (kind != LiveInputKind.Punish)
		{
			return liveFrame2.Snapshot.Target?.Id == liveFrame.Snapshot.Target?.Id;
		}
		return Punish?.TriggerBuffsRemainAbsent(liveFrame2) ?? false;
		IL_05b5:
		return false;
		IL_0568:
		if (num)
		{
			goto IL_056a;
		}
		goto IL_05b5;
	}

	private ActionResult DispatchCombat(CombatAction action, string key)
	{
		_dispatchFrame = Frame;
		_dispatchingCombat = ((action == CombatAction.Attack) ? LiveInputKind.Attack : LiveInputKind.Tool);
		try
		{
			return _input.ExecuteCombat(action, key);
		}
		finally
		{
			_dispatchingCombat = null;
		}
	}

	private ActionResult PrepareFanAttack(LiveFrame frame, Direction desired)
	{
		if (!_disposed && !SubNeedsPriority)
		{
			AutoCastTurnEngine? castTurn = CastTurn;
			if (castTurn == null || !castTurn.InFinalWindow)
			{
				TargetInfo target = frame.Snapshot.Target;
				if ((object)target != null)
				{
					string id = target.Id;
					if (id != null && CheckSafety(LiveInputKind.Attack).Allowed)
					{
						if (_fanTurnTarget != id)
						{
							_fanTurnAt = null;
							_fanTurnTarget = id;
						}
						long? fanTurnAt = _fanTurnAt;
						if (fanTurnAt.HasValue)
						{
							long valueOrDefault = fanTurnAt.GetValueOrDefault();
							if (frame.CapturedAtMs <= valueOrDefault)
							{
								return ActionResult.Deferred("Fan: aguardando leitura nova após giro");
							}
						}
						if (desired == frame.Snapshot.Player.Facing)
						{
							_fanTurnAt = null;
							if (!CombatGeometry.InRange(frame, CombatAction.Attack, Config.CombatAssist))
							{
								return ActionResult.Rejected("Fan fora da faixa frontal");
							}
							return ActionResult.Ok("Fan alinhado na leitura atual");
						}
						fanTurnAt = _fanTurnAt;
						if (fanTurnAt.HasValue)
						{
							long valueOrDefault2 = fanTurnAt.GetValueOrDefault();
							if (_clock.NowMs - valueOrDefault2 < 250)
							{
								return ActionResult.Deferred("Fan: intervalo mínimo do giro");
							}
						}
						_dispatchFrame = frame;
						_fanTurnDesired = desired;
						_preparingFanTurn = true;
						try
						{
							TurnResult turnResult = _input.RequestTurn(desired);
							if (!turnResult.Accepted)
							{
								return ActionResult.Rejected("Giro Fan recusado: " + turnResult.Reason);
							}
							_fanTurnAt = _clock.NowMs;
							Log.Log(LogLevel.Info, $"Giro Fan {desired} enviado antes do AA; aguardando direção observada", EventOrigin.Executed);
							return ActionResult.Deferred("Fan: aguardando direção observada antes do AA");
						}
						finally
						{
							_preparingFanTurn = false;
						}
					}
				}
			}
		}
		return ActionResult.Rejected("Giro Fan bloqueado por segurança");
	}

	private ActionResult DispatchFollow(FollowStep step)
	{
		_followFrame = Frame;
		if ((object)_followFrame == null)
		{
			return ActionResult.Rejected("Sem frame para seguir");
		}
		_dispatchingFollow = step;
		return _input.Move(step);
	}

	private ActionResult PrepareComboFacing()
	{
		if (_disposed || _comboFacing == null || !CheckSafety(LiveInputKind.Punish).Allowed)
		{
			return ActionResult.Rejected("Preparação de giro bloqueada por segurança");
		}
		_dispatchFrame = Frame;
		_preparingComboTurn = true;
		try
		{
			ActionResult actionResult = _comboFacing.BeforeAction();
			if (!actionResult.Accepted)
			{
				TurnStatus = actionResult.Reason;
			}
			return actionResult;
		}
		finally
		{
			_preparingComboTurn = false;
		}
	}

	private void ResetPunish()
	{
		Punish?.Reset();
		_comboFacing?.Reset();
		_comboTargetId = null;
		_punishWasArmed = false;
		_subReadBurst = false;
	}

	public void Step()
	{
		lock (_automationLock)
		{
			if (!_disposed)
			{
				StepCore();
			}
		}
	}

	private void StepCore()
	{
		int accessMask = AccessMask;
		if (accessMask != _accessMask || !_access.IsValid)
		{
			_accessMask = accessMask;
			_autoEnabled = _subEnabled = _authorized = _combatEnabled = _combatAuthorized = false;
			_followEnabled = _staminaEnabled = _castTurnEnabled = _autoTargetEnabled = _smartTargetEnabled = false;
			_input?.ReleaseFollow();
			Follow?.Reset();
			Combat?.Reset();
			Stamina?.Reset();
			CastTurn?.Reset();
			AutoTarget?.Reset();
			SmartTarget?.Reset();
			_attackEnabled = _toolEnabled = false;
			ResetPunish();
			AutoTurning?.ResetCadence();
		}
		_input?.ServiceFollow();
		LiveBuffObservation buffObservation = BuffObservation;
		string text = string.Join(" + ", from b in buffObservation.ActiveSubs
			select b.Name into n
			orderby n
			select n);
		if (_lastBuffPresence != buffObservation.Presence || _lastBuffNames != text)
		{
			_lastBuffPresence = buffObservation.Presence;
			_lastBuffNames = text;
			Log.Log(LogLevel.Info, $"Leitura Subs: {buffObservation.Presence}; {string.Join(" | ", buffObservation.ActiveSubs.Select((ObservedBuff b) => b.Name + ": " + (b.RemainingSeconds?.ToString() ?? "?") + "s"))}; {buffObservation.Reason}", EventOrigin.Observed);
		}
		LiveFrame frame = Frame;
		SafetyDecision safetyDecision = CheckSafety(LiveInputKind.Punish);
		SafetyDecision safetyDecision2 = CheckSafety(LiveInputKind.Turning);
		TurningSafetyStatus = safetyDecision2.Reason;
		PunishSafetyStatus = safetyDecision.Reason;
		if (_lastTurnSafetyReason != safetyDecision2.Reason)
		{
			_lastTurnSafetyReason = safetyDecision2.Reason;
			Log.Log(LogLevel.Info, "Turn safety: " + safetyDecision2.Reason, EventOrigin.Observed);
		}
		if (_lastPunishSafetyReason != safetyDecision.Reason)
		{
			_lastPunishSafetyReason = safetyDecision.Reason;
			Log.Log(LogLevel.Info, "Punish safety: " + safetyDecision.Reason, EventOrigin.Observed);
		}
		AutoTurning?.UpdateOptions(AutoTurning.Options with
		{
			Enabled = safetyDecision2.Allowed,
			MinCommandIntervalMs = (StrictSubTurnProfile ? Math.Max(250L, AutoTurning.Options.MinCommandIntervalMs) : AutoTurning.Options.MinCommandIntervalMs),
			ManualMovementPriority = false
		});
		if (!safetyDecision2.Allowed)
		{
			_lastTurnCapture = -1L;
		}
		_dispatchFrame = frame;
		if (_punishWasArmed && !safetyDecision.Allowed)
		{
			Log.Log(LogLevel.Warning, "Fluxo SubPunish cancelado por segurança: " + safetyDecision.Reason, EventOrigin.Observed);
		}
		_punishWasArmed = safetyDecision.Allowed;
		Punish?.Update(frame, _subEnabled, CounterAttackAuthorized, safetyDecision.Allowed, safetyDecision.Reason);
		bool flag;
		switch (Punish?.State)
		{
		case EarlyPunishState.Idle:
		case EarlyPunishState.WatchingBuff:
		case EarlyPunishState.Expired:
		case EarlyPunishState.Cancelled:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			_comboFacing?.Reset();
		}
		flag = safetyDecision.Allowed;
		bool flag2;
		bool flag3;
		if (flag)
		{
			flag2 = frame?.ObservedActiveBuffs.Any((ObservedBuff b) => SubstitutionBuffs.IsPunishEligible(b.Name)) ?? false;
			if (!flag2)
			{
				EarlyPunishState? earlyPunishState = Punish?.State;
				if (earlyPunishState.HasValue)
				{
					EarlyPunishState valueOrDefault = earlyPunishState.GetValueOrDefault();
					if ((uint)(valueOrDefault - 1) <= 2u)
					{
						flag3 = true;
						goto IL_04ea;
					}
				}
				flag3 = false;
				goto IL_04ea;
			}
			goto IL_04ee;
		}
		goto IL_04f2;
		IL_04ea:
		flag2 = flag3;
		goto IL_04ee;
		IL_04ee:
		flag = flag2;
		goto IL_04f2;
		IL_04f2:
		_subReadBurst = flag;
		SafetyDecision safetyDecision3 = CheckSafety(LiveInputKind.CastTurning);
		CastTurn?.Update(frame, safetyDecision3.Allowed, SubNeedsPriority, _castTurnEnabled);
		CastTurnStatus = ((!safetyDecision3.Allowed) ? safetyDecision3.Reason : (CastTurn?.Status ?? "Indisponível"));
		if (_castTurnEnabled && (object)frame != null && frame.Cast?.Active == true)
		{
			string text2 = (CastTurnStatus.StartsWith("Auto Cast Turn: cast ", StringComparison.Ordinal) ? $"cast {frame.Cast.SpellId} em progresso" : CastTurnStatus);
			if (_lastCastTurnDiagnostic != text2)
			{
				_lastCastTurnDiagnostic = text2;
				Log.Log(LogLevel.Info, $"Cast Turn diagnóstico: {text2}; ID={frame.Cast.SpellId}; restante={frame.Cast.RemainingMs}ms; leitura={frame.ReadDurationMs}ms; alvo={frame.TargetName ?? "nenhum"}", EventOrigin.Observed);
			}
		}
		else
		{
			_lastCastTurnDiagnostic = null;
		}
		_targetWriter?.ObserveTrialFrame(frame);
		SafetyDecision autoTargetSafety = CheckSafety(LiveInputKind.TargetMemory);
		AutoTarget?.Update(frame, _autoTargetEnabled, autoTargetSafety.Allowed, frame?.Cast?.Active == true || SubNeedsPriority);
		AutoTargetStatus = (autoTargetSafety.Allowed ? (AutoTarget?.Status ?? "Indisponível") : autoTargetSafety.Reason);
		bool smartHeld = _smartTargetEnabled && SmartHoldDown;
		SafetyDecision smartSafety = CheckSafety(LiveInputKind.SmartTargetMemory);
		SmartTarget?.Update(frame, smartHeld, smartSafety.Allowed, frame?.Cast?.Active == true || SubNeedsPriority);
		SmartTargetStatus = !_smartTargetEnabled ? "Smart Target OFF" : !smartHeld ? $"Smart Target: aguardando {Config.SmartTarget.HoldKey}; último alvo mantido" :
			(smartSafety.Allowed ? SmartTarget?.Status ?? "Indisponível" : smartSafety.Reason);
		UpdateFollow();
		AutoCastTurnEngine? castTurn = CastTurn;
		int num;
		if (castTurn == null || !castTurn.InFinalWindow)
		{
			ILivePunishEngine? punish = Punish;
			num = ((punish == null || !punish.BlocksAutoTurning) ? 1 : 0);
		}
		else
		{
			num = 0;
		}
		bool flag4 = (byte)num != 0;
		if (safetyDecision2.Allowed && (object)frame != null && frame.CapturedAtMs != _lastTurnCapture && flag4)
		{
			_lastTurnCapture = frame.CapturedAtMs;
			TurnDecision turnDecision = AutoTurning?.Update(frame.Snapshot);
			if ((object)turnDecision != null)
			{
				TurnStatus = $"{turnDecision.Kind}: {turnDecision.Reason}";
				string text3 = $"{turnDecision.Kind}:{turnDecision.Desired}:{turnDecision.Reason}";
				if (_lastTurnDecision != text3)
				{
					_lastTurnDecision = text3;
					Log.Log(LogLevel.Info, $"Live turn {TurnStatus}; player={frame.Snapshot.Player.Facing}, target={frame.TargetName}", EventOrigin.Observed);
				}
			}
		}
		SafetyDecision safetyDecision4 = CheckSafety(LiveInputKind.Attack);
		bool higherPriorityBusy = SubNeedsPriority || (CastTurn?.InFinalWindow ?? false);
		SafetyDecision safetyDecision5 = CheckSafety(LiveInputKind.Tool);
		Combat?.Update(Frame, safetyDecision4.Allowed, safetyDecision5.Allowed, higherPriorityBusy);
		CombatStatus = ((!safetyDecision4.Allowed && !safetyDecision5.Allowed) ? safetyDecision4.Reason : (Combat?.Status ?? "Indisponível"));
		SafetyDecision safetyDecision6 = CheckSafety(LiveInputKind.Stamina);
		if (_lastStaminaSafety != safetyDecision6.Reason)
		{
			_lastStaminaSafety = safetyDecision6.Reason;
			Log.Log(LogLevel.Info, "Stamina safety: " + safetyDecision6.Reason, EventOrigin.Observed);
		}
		Stamina?.Update(Frame, _staminaEnabled, safetyDecision6.Allowed, higherPriorityBusy);
		object staminaStatus;
		if (safetyDecision6.Allowed)
		{
			staminaStatus = Stamina?.Status ?? "Indisponível";
		}
		else
		{
			StaminaObservation staminaObservation = Frame?.Stamina;
			staminaStatus = (((object)staminaObservation != null && staminaObservation.Valid) ? $"{safetyDecision6.Reason} · SP {staminaObservation.Current}/{staminaObservation.Maximum} ({staminaObservation.Percent:0}%) · corrida {(staminaObservation.Running ? "ON" : "OFF")}" : safetyDecision6.Reason);
		}
		StaminaStatus = (string)staminaStatus;
	}

	private void UpdateFollow()
	{
		if (_disposed)
		{
			_input?.ReleaseFollow();
			return;
		}
		SafetyDecision safetyDecision = CheckSafety(LiveInputKind.Follow);
		Follow?.Update(Frame, Volatile.Read(in _navigation), safetyDecision.Allowed, SubNeedsPriority);
		FollowStatus = ((!safetyDecision.Allowed) ? safetyDecision.Reason : (Follow?.Status ?? "Indisponível"));
	}

	public void Dispose()
	{
		if (!_disposed)
		{
			_disposed = true;
			_autoEnabled = (_subEnabled = (_authorized = (_combatEnabled = (_combatAuthorized = (_followEnabled = false)))));
			_staminaEnabled = (_castTurnEnabled = (_autoTargetEnabled = (_smartTargetEnabled = false)));
			_attackEnabled = _toolEnabled = false;
			_stop.Cancel();
			lock (_automationLock)
			{
				_input?.Dispose();
			}
			_reader?.GetAwaiter().GetResult();
			_automation?.Dispose();
			_targetWriter?.Dispose();
			lock (_automationLock)
			{
				_input?.Dispose();
				ResetPunish();
				_dispatchFrame = null;
			}
			Volatile.Write(ref _frame, null);
			_stop.Dispose();
		}
	}
}
