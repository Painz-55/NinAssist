using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

internal sealed class LiveInputController : IOrientationController, IActionExecutor, IDisposable
{
	private struct Input
	{
		public uint Type;

		public InputUnion Data;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public KeyboardInput Keyboard;

	}

	private struct KeyboardInput
	{
		public ushort VirtualKey;

		public ushort ScanCode;

		public uint Flags;

		public uint Time;

		public nuint ExtraInfo;
	}

	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}


	private struct GuiThreadInfo
	{
		public uint Size;

		public uint Flags;

		public nint Active;

		public nint Focus;

		public nint Capture;

		public nint MenuOwner;

		public nint MoveSize;

		public nint Caret;

		public Rect CaretRect;
	}

	private readonly Func<LiveInputKind, bool> _allowed;

	private readonly Func<ActionResult> _prepareComboFacing;

	private readonly IAutoAssistLogger _log;

	private readonly string[] _bindingFiles;

	private readonly DateTime[] _bindingTimes;

	private bool _faulted;

	private readonly string[] _comboKeys;

	private readonly string[] _comboBindings;

	private readonly HashSet<ushort> _activationKeys;

	private readonly CombatAssistSettings _combatSettings;

	private readonly string? _toolSlot;

	private readonly HeldDirectionController _heldMovement;

	private readonly ushort _runKey;

	private readonly string _runConfig;

	private DateTime? _runConfigTime;

	private bool _toggleMode;

	private readonly SystemMonotonicClock _holdClock = new SystemMonotonicClock();

	private static int _emergencyLatched;

	private readonly Dictionary<Direction, ushort> _turns = new Dictionary<Direction, ushort>
	{
		[Direction.Up] = 36,
		[Direction.Down] = 35,
		[Direction.Left] = 46,
		[Direction.Right] = 34
	};

	public string StaminaBindingStatus { get; private set; } = "Corrida não validada";

	public string StaminaBindingProblem
	{
		get
		{
			if (_runKey != 0)
			{
				if (BindingsUnchanged())
				{
					if (_toggleMode)
					{
						return "Run validado";
					}
					return "HoldToRun não está False ou arquivo de opções indisponível";
				}
				return "Bindings do jogo alterados; reconecte";
			}
			return StaminaBindingStatus;
		}
	}

	public bool StaminaBindingValid
	{
		get
		{
			try
			{
				DateTime lastWriteTimeUtc = File.GetLastWriteTimeUtc(_runConfig);
				if (_runConfigTime != lastWriteTimeUtc)
				{
					_toggleMode = RunBindingValidator.IsToggleMode(File.ReadAllText(_runConfig));
					_runConfigTime = lastWriteTimeUtc;
				}
				return _runKey != 0 && _toggleMode && BindingsUnchanged();
			}
			catch
			{
				_runConfigTime = null;
				_toggleMode = false;
				return false;
			}
		}
	}

	public string? LastActionText { get; private set; }

	public string LastInputReason { get; private set; } = "No input dispatched";

	public Action? RefreshFollowDuringTap { get; set; }

	public bool MaintainingCastTurn { get; private set; }

	public LiveInputController(Func<LiveInputKind, bool> allowed, IAutoAssistLogger log, string gameDirectory, string[] comboKeys, string[] activationKeys, Func<ActionResult> prepareComboFacing, CombatAssistSettings combatSettings)
	{
		_allowed = allowed;
		_prepareComboFacing = prepareComboFacing;
		combatSettings.Validate();
		_combatSettings = combatSettings;
		_log = log;
		_heldMovement = new HeldDirectionController((Direction d) => IsDown(MovementKey(d)), (Direction d) => SendMovement(d, up: false), (Direction d) => SendMovement(d, up: true));
		_comboKeys = LiveKeySettings.Validate(comboKeys, activationKeys).Take(3).ToArray();
		_activationKeys = activationKeys.Select((string k) => LiveKeySettings.Keys[k]).ToHashSet();
		if (Marshal.SizeOf<Input>() != ((IntPtr.Size == 8) ? 40 : 28))
		{
			throw new InvalidOperationException("Unsupported native INPUT layout");
		}
		_bindingFiles = new string[2]
		{
			Path.Combine(gameDirectory, "InputSystem", "input_defaults.json"),
			Path.Combine(gameDirectory, "data files", "input_overrides.json")
		};
		Dictionary<string, string[]> bindings = new Dictionary<string, string[]>();
		string[] source = Array.Empty<string>();
		string[] bindingFiles = _bindingFiles;
		for (int num = 0; num < bindingFiles.Length; num++)
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(File.ReadAllText(bindingFiles[num]));
			foreach (JsonElement item in jsonDocument.RootElement.EnumerateArray())
			{
				JsonElement value4;
				if (item.GetProperty("Name").GetString() == "Movement")
				{
					source = (from b in item.GetProperty("Bindings").EnumerateArray()
						where b.GetProperty("Device").GetString() == "Keyboard" && b.TryGetProperty("Composite", out value4) && value4.ValueKind == JsonValueKind.Object
						select b).SelectMany((JsonElement b) => from p in b.GetProperty("Composite").EnumerateObject()
						select p.Value.GetString() ?? "").Distinct().ToArray();
				}
				string[] value = (from b in item.GetProperty("Bindings").EnumerateArray()
					where b.GetProperty("Device").GetString() == "Keyboard" && b.GetProperty("Kind").GetString() == "Button" && (!b.TryGetProperty("RequiredModifiers", out value4) || value4.ValueKind == JsonValueKind.Null || (value4.ValueKind == JsonValueKind.Array && value4.GetArrayLength() == 0))
					select LiveKeySettings.NormalizeGameKey(b.GetProperty("Source").GetString() ?? "")).ToArray();
				bindings[item.GetProperty("Name").GetString()] = value;
			}
		}
		foreach (KeyValuePair<string, string> item2 in new Dictionary<string, string>
		{
			["FaceUp"] = "Home",
			["FaceDown"] = "End",
			["FaceLeft"] = "Delete",
			["FaceRight"] = "PageDown"
		})		{
			if (!bindings.TryGetValue(item2.Key, out string[] value2) || !value2.Contains(item2.Value))
			{
				throw new InvalidOperationException("Binding " + item2.Key + " changed; revalidate inputs before live mode.");
			}
		}
		if (!source.ToHashSet().SetEquals(new string[4] { "Up", "Down", "Left", "Right" }))
		{
			throw new InvalidOperationException("Movement bindings changed; revalidate reserved-key conflicts before enabling input.");
		}
		HashSet<string> actionBindings = bindings.Where<KeyValuePair<string, string[]>>((KeyValuePair<string, string[]> b) => b.Key == "Attack" || CombatBindingValidator.IsHotbarSlot(b.Key)).SelectMany((KeyValuePair<string, string[]> b) => b.Value).ToHashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (_comboKeys.Any((string k) => !k.Equals("null", StringComparison.OrdinalIgnoreCase) && !actionBindings.Contains(k)))
		{
			throw new InvalidOperationException("Cada tecla do combo precisa estar vinculada a Attack/Hotbar no jogo (sem modificadores).");
		}
		_comboBindings = _comboKeys.Select((string key) => (!key.Equals("null", StringComparison.OrdinalIgnoreCase)) ? string.Join("/", from b in bindings
			where (b.Key == "Attack" || CombatBindingValidator.IsHotbarSlot(b.Key)) && b.Value.Contains<string>(key, StringComparer.OrdinalIgnoreCase)
			select b.Key) : "desabilitado").ToArray();
		_log.Log(LogLevel.Info, "Bindings observados do combo: " + string.Join(" → ", _comboKeys.Select((string key, int i) => $"posição {i + 1} {key}={_comboBindings[i]}")) + "; posição não prova efeito da ação", EventOrigin.Observed);
		if (combatSettings.UseAttack && (!bindings.TryGetValue("Attack", out string[] value3) || !value3.Contains<string>(combatSettings.AttackKey, StringComparer.OrdinalIgnoreCase)))
		{
			throw new InvalidOperationException("Configure a tecla de AA também no binding Attack do jogo.");
		}
		_toolSlot = (combatSettings.UseTool ? CombatBindingValidator.ResolveToolSlot(bindings, combatSettings.ToolKey) : null);
		_runConfig = Path.Combine(gameDirectory, "data files", "config.json");
		try
		{
			_runKey = RunBindingValidator.Resolve(bindings, LiveKeySettings.Keys[combatSettings.FollowHoldKey]);
			StaminaBindingStatus = "Run = " + bindings["Run"][0] + "; modificador nativo permitido; exige HoldToRun=False";
		}
		catch (Exception ex)
		{
			StaminaBindingStatus = "Configuração Run indisponível: " + ex.Message;
		}
		_bindingTimes = _bindingFiles.Select(File.GetLastWriteTimeUtc).ToArray();
	}

	public TurnResult RequestTurn(Direction direction)
	{
		if (!_turns.TryGetValue(direction, out var value))
		{
			return TurnResult.Rejected("Unsupported direction");
		}
		if (!Tap(value, extended: true, LiveInputKind.Turning))
		{
			return TurnResult.Rejected(LastInputReason);
		}
		return TurnResult.Ok(LastInputReason);
	}

	public TurnResult RequestCastTurn(Direction direction, int maximumHoldMs)
	{
		if (!_turns.TryGetValue(direction, out var value))
		{
			return TurnResult.Rejected("Unsupported cast turn direction");
		}
		if (!Tap(value, extended: true, LiveInputKind.CastTurning, Math.Clamp(maximumHoldMs, 40, 320)))
		{
			return TurnResult.Rejected(LastInputReason);
		}
		return TurnResult.Ok(LastInputReason);
	}

	public ActionResult Execute(string actionId, string key)
	{
		int num = actionId switch
		{
			"basic_attack" => 0,
			"followup_hotbar_Q" => 1,
			"followup_hotbar_S" => 2,
			_ => -1,
		};
		if (num < 0 || key.Equals("null", StringComparison.OrdinalIgnoreCase) || !key.Equals(_comboKeys[num], StringComparison.OrdinalIgnoreCase))
		{
			return ActionResult.Rejected("Live action not configured");
		}
		ushort num2 = LiveKeySettings.Keys[key];
		if (!_allowed(LiveInputKind.Punish))
		{
			return ActionResult.Rejected("Punish safety gate blocked");
		}
		if (_activationKeys.Contains(num2) && IsDown(num2))
		{
			return ActionResult.Deferred("Tecla compartilhada com Turn ainda segurada manualmente; solte para continuar o combo");
		}
		ActionResult actionResult = _prepareComboFacing();
		if (!actionResult.Accepted)
		{
			return actionResult;
		}
		if (!Tap(num2, extended: false, LiveInputKind.Punish))
		{
			return ActionResult.Rejected(LastInputReason);
		}
		LastActionText = $"Combo posição {num + 1} ({key}, binding {_comboBindings[num]}) — tecla enviada, efeito/acerto NÃO confirmado";
		_log.Log(LogLevel.Info, LastActionText, EventOrigin.Executed);
		return ActionResult.Ok(LastActionText);
	}

	public ActionResult ExecuteCombat(CombatAction action, string key)
	{
		if ((uint)action > 1u)
		{
			return ActionResult.Rejected("Finalidade de ataque inválida");
		}
		string value = ((action == CombatAction.Attack) ? _combatSettings.AttackKey : _combatSettings.ToolKey);
		if (!key.Equals(value, StringComparison.OrdinalIgnoreCase))
		{
			return ActionResult.Rejected("Tecla AA/Tool não configurada");
		}
		LiveInputKind kind = ((action == CombatAction.Attack) ? LiveInputKind.Attack : LiveInputKind.Tool);
		if (!Tap(LiveKeySettings.Keys[key], extended: false, kind))
		{
			return ActionResult.Rejected(LastInputReason);
		}
		LastActionText = ((action == CombatAction.Tool) ? $"Tool ({key}, slot {_toolSlot}) — atalho enviado; item/acerto NÃO confirmados pela memória" : $"AA {_combatSettings.AttackMode} ({key}) — tentativa enviada; acerto NÃO confirmado");
		_log.Log(LogLevel.Info, LastActionText, EventOrigin.Executed);
		return ActionResult.Ok(LastActionText);
	}

	public ActionResult Move(FollowStep step)
	{
		if (MovementKey(step.Direction) == 0 || !step.Offset.HasValue)
		{
			return ActionResult.Rejected("Direção de movimento inválida");
		}
		if (!_heldMovement.Set(step.Direction, !_faulted && BindingsUnchanged() && _allowed(LiveInputKind.Follow), step.SecondaryDirection))
		{
			return ActionResult.Rejected("Follow bloqueado ou seta manual/erro de input");
		}
		return ActionResult.Ok("Setas mantidas pressionadas");
	}

	private bool BindingsUnchanged()
	{
		return !_bindingFiles.Where((string f, int i) => File.GetLastWriteTimeUtc(f) != _bindingTimes[i]).Any();
	}

	public void ReleaseFollow()
	{
		_heldMovement.Release();
	}

	public void ServiceFollow()
	{
		try
		{
			if (_faulted || !BindingsUnchanged() || !_allowed(LiveInputKind.Follow))
			{
				ReleaseFollow();
			}
		}
		catch
		{
			ReleaseFollow();
			throw;
		}
	}

	private bool SendMovement(Direction direction, bool up)
	{
		if (!up && (_faulted || !_allowed(LiveInputKind.Follow)))
		{
			return false;
		}
		ushort num = (ushort)MapVirtualKey(MovementKey(direction), 0u);
		if (num == 0)
		{
			return false;
		}
		Input input = new Input
		{
			Type = 1u,
			Data = new InputUnion
			{
				Keyboard = new KeyboardInput
				{
					ScanCode = num,
					Flags = (uint)(9 | (up ? 2 : 0))
				}
			}
		};
		bool num2 = SendInput(1u, new Input[1] { input }, Marshal.SizeOf<Input>()) == 1;
		if (!num2)
		{
			_faulted = true;
			LastInputReason = "Falha no input do Follow; reconecte";
		}
		return num2;
	}

	public void Dispose()
	{
		ReleaseFollow();
	}

	public ActionResult ToggleRun()
	{
		if (!StaminaBindingValid)
		{
			return ActionResult.Rejected("Configuração de corrida alterada/não validada; reconecte");
		}
		bool num = Tap(_runKey, _runKey == 163, LiveInputKind.Stamina);
		if (num)
		{
			_log.Log(LogLevel.Info, "Auto Stamina: toggle Run enviado; aguardando estado observado", EventOrigin.Executed);
		}
		if (!num)
		{
			return ActionResult.Rejected(LastInputReason);
		}
		return ActionResult.Ok("Toggle Run enviado");
	}

	public static ushort MovementKey(Direction direction)
	{
		return direction switch
		{
			Direction.Up => 38,
			Direction.Down => 40,
			Direction.Left => 37,
			Direction.Right => 39,
			_ => 0,
		};
	}

	public bool OtherMovementHeld(FollowStep step)
	{
		return new Direction[4]
		{
			Direction.Up,
			Direction.Down,
			Direction.Left,
			Direction.Right
		}.Any((Direction d) => d != step.Direction && d != step.SecondaryDirection && !_heldMovement.Owns(d) && IsDown(MovementKey(d)));
	}

	private bool Tap(ushort key, bool extended, LiveInputKind kind, int holdMs = 40)
	{
		if (_faulted)
		{
			LastInputReason = "Previous key-release fault; reconnect required";
			return false;
		}
		if (!_allowed(kind))
		{
			LastInputReason = $"{kind}: safety gate changed before dispatch";
			return false;
		}
		if (IsDown(key))
		{
			LastInputReason = $"Key 0x{key:X} already held manually";
			return false;
		}
		if (_bindingFiles.Where((string f, int i) => File.GetLastWriteTimeUtc(f) != _bindingTimes[i]).Any())
		{
			LastInputReason = "Bindings file changed; reconnect required";
			return false;
		}
		ushort num = (ushort)MapVirtualKey(key, 0u);
		if (num == 0)
		{
			LastInputReason = "Key has no scan-code mapping";
			return false;
		}
		Input input = new Input
		{
			Type = 1u,
			Data = new InputUnion
			{
				Keyboard = new KeyboardInput
				{
					ScanCode = num,
					Flags = (8u | (extended ? 1u : 0u))
				}
			}
		};
		Input input2 = input;
		input2.Data.Keyboard.Flags |= 2u;
		bool flag = false;
		try
		{
			if (!_allowed(kind))
			{
				LastInputReason = $"{kind}: safety gate changed at dispatch";
				return false;
			}
			if ((kind == LiveInputKind.Tool || (kind == LiveInputKind.Attack && _combatSettings.AttackMode != AutoAttackMode.Melee)) && ManualDirectionHeld())
			{
				LastInputReason = "Tecla de direção/giro manual ativa; atalho frontal adiado";
				return false;
			}
			flag = SendInput(1u, new Input[1] { input }, Marshal.SizeOf<Input>()) == 1;
			if (!flag)
			{
				LastInputReason = $"SendInput key-down rejected; Win32={Marshal.GetLastWin32Error()} (no bypass attempted)";
				_log.Log(LogLevel.Warning, LastInputReason);
				return false;
			}
			MaintainingCastTurn = kind == LiveInputKind.CastTurning;
			BoundedInputHold.Wait(holdMs, _holdClock, delegate
			{
				RefreshFollowDuringTap?.Invoke();
				ServiceFollow();
				return _allowed(kind);
			});
		}
		finally
		{
			MaintainingCastTurn = false;
			if (flag && SendInput(1u, new Input[1] { input2 }, Marshal.SizeOf<Input>()) != 1)
			{
				_faulted = true;
				LastInputReason = "Key-release dispatch failed; reconnect required";
				_log.Log(LogLevel.Error, "Key release dispatch failed; live input disabled until reconnect (key-down may have been received)");
			}
		}
		if (!_faulted)
		{
			LastInputReason = $"Scan-code 0x{num:X} dispatched; effect requires observed confirmation";
		}
		return !_faulted;
	}

	public static bool IsDown(int key)
	{
		return (GetAsyncKeyState(key) & 0x8000) != 0;
	}

	private bool ManualDirectionHeld()
	{
		if (!new Direction[4]
		{
			Direction.Left,
			Direction.Up,
			Direction.Right,
			Direction.Down
		}.Any((Direction d) => !_heldMovement.Owns(d) && IsDown(MovementKey(d))))
		{
			return new int[4] { 36, 35, 46, 34 }.Any(IsDown);
		}
		return true;
	}

	public static bool EmergencyPending()
	{
		if ((GetAsyncKeyState(123) & 0x8001) != 0)
		{
			Interlocked.Exchange(ref _emergencyLatched, 1);
		}
		return Volatile.Read(in _emergencyLatched) != 0;
	}

	public static bool ConsumeEmergency()
	{
		return EmergencyPending();
	}

	public static void AcknowledgeEmergencyAfterDisarm()
	{
		Interlocked.Exchange(ref _emergencyLatched, 0);
	}

	public static bool IsGameForeground(int pid)
	{
		GetWindowThreadProcessId(GetForegroundWindow(), out var pid2);
		return pid2 == pid;
	}

	public static bool TextEntryFocused()
	{
		GuiThreadInfo info = new GuiThreadInfo
		{
			Size = (uint)Marshal.SizeOf<GuiThreadInfo>()
		};
		if (!GetGUIThreadInfo(0u, ref info))
		{
			return true;
		}
		if (info.Caret != IntPtr.Zero)
		{
			return true;
		}
		StringBuilder stringBuilder = new StringBuilder(128);
		GetClassName(info.Focus, stringBuilder, stringBuilder.Capacity);
		return stringBuilder.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase);
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint count, Input[] inputs, int size);

	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint code, uint mapType);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int key);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint window, out int pid);

	[DllImport("user32.dll")]
	private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint window, StringBuilder name, int length);
}
