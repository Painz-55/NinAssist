using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

/// <summary>Normal SendInput only, never background messages/hooks. Every dispatch rechecks the safety gate.</summary>
internal sealed class LiveInputController : IOrientationController, IActionExecutor, IDisposable
{
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
    public string StaminaBindingStatus { get; private set; } = "Corrida não validada";
    public string StaminaBindingProblem => _runKey == 0 ? StaminaBindingStatus
        : !BindingsUnchanged() ? "Bindings do jogo alterados; reconecte"
        : !_toggleMode ? "HoldToRun não está False ou arquivo de opções indisponível"
        : "Run validado";
    public bool StaminaBindingValid
    {
        get
        {
            try
            {
                var time = File.GetLastWriteTimeUtc(_runConfig);
                if (_runConfigTime != time)
                {
                    _toggleMode = RunBindingValidator.IsToggleMode(File.ReadAllText(_runConfig));
                    _runConfigTime = time;
                }
                return _runKey != 0 && _toggleMode && BindingsUnchanged();
            }
            catch { _runConfigTime = null; _toggleMode = false; return false; }
        }
    }
    private readonly SystemMonotonicClock _holdClock = new();
    private static int _emergencyLatched;
    public string? LastActionText { get; private set; }
    public string LastInputReason { get; private set; } = "No input dispatched";
    public Action? RefreshFollowDuringTap { get; set; }
    public bool MaintainingCastTurn { get; private set; }
    private readonly Dictionary<Direction, ushort> _turns = new()
    {
        [Direction.Up] = 0x24, [Direction.Down] = 0x23, [Direction.Left] = 0x2E, [Direction.Right] = 0x22
    };
    public LiveInputController(Func<LiveInputKind, bool> allowed, IAutoAssistLogger log, string gameDirectory,
        string[] comboKeys, string[] activationKeys, Func<ActionResult> prepareComboFacing, CombatAssistSettings combatSettings)
    {
        _allowed = allowed;
        _prepareComboFacing = prepareComboFacing;
        combatSettings.Validate();
        _combatSettings = combatSettings;
        _log = log;
        _heldMovement = new HeldDirectionController(d => IsDown(MovementKey(d)),
            d => SendMovement(d, false), d => SendMovement(d, true));
        _comboKeys = LiveKeySettings.Validate(comboKeys, activationKeys).Take(3).ToArray();
        _activationKeys = activationKeys.Select(k => LiveKeySettings.Keys[k]).ToHashSet();
        if (Marshal.SizeOf<Input>() != (IntPtr.Size == 8 ? 40 : 28))
            throw new InvalidOperationException("Unsupported native INPUT layout");
        _bindingFiles = new[] { Path.Combine(gameDirectory, "InputSystem", "input_defaults.json"),
            Path.Combine(gameDirectory, "data files", "input_overrides.json") };
        var bindings = new Dictionary<string, string[]>();
        var movementKeys = Array.Empty<string>();
        foreach (var file in _bindingFiles)
        {
            using var json = JsonDocument.Parse(File.ReadAllText(file));
            foreach (var action in json.RootElement.EnumerateArray())
            {
                if (action.GetProperty("Name").GetString() == "Movement")
                    movementKeys = action.GetProperty("Bindings").EnumerateArray()
                        .Where(b => b.GetProperty("Device").GetString() == "Keyboard" &&
                            b.TryGetProperty("Composite", out var composite) && composite.ValueKind == JsonValueKind.Object)
                        .SelectMany(b => b.GetProperty("Composite").EnumerateObject().Select(p => p.Value.GetString() ?? ""))
                        .Distinct().ToArray();
                var keys = action.GetProperty("Bindings").EnumerateArray().Where(b =>
                    b.GetProperty("Device").GetString() == "Keyboard" && b.GetProperty("Kind").GetString() == "Button" &&
                    (!b.TryGetProperty("RequiredModifiers", out var mods) || mods.ValueKind == JsonValueKind.Null ||
                        (mods.ValueKind == JsonValueKind.Array && mods.GetArrayLength() == 0)))
                    .Select(b => LiveKeySettings.NormalizeGameKey(b.GetProperty("Source").GetString() ?? "")).ToArray();
                bindings[action.GetProperty("Name").GetString()!] = keys;
            }
        }
        foreach (var expected in new Dictionary<string, string> { ["FaceUp"] = "Home", ["FaceDown"] = "End",
            ["FaceLeft"] = "Delete", ["FaceRight"] = "PageDown" })
            if (!bindings.TryGetValue(expected.Key, out var keys) || !keys.Contains(expected.Value))
                throw new InvalidOperationException($"Binding {expected.Key} changed; revalidate inputs before live mode.");
        if (!movementKeys.ToHashSet().SetEquals(new[] { "Up", "Down", "Left", "Right" }))
            throw new InvalidOperationException("Movement bindings changed; revalidate reserved-key conflicts before enabling input.");
        var actionBindings = bindings.Where(b => b.Key == "Attack" || CombatBindingValidator.IsHotbarSlot(b.Key))
            .SelectMany(b => b.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (_comboKeys.Any(k => !k.Equals(LiveKeySettings.Disabled, StringComparison.OrdinalIgnoreCase) && !actionBindings.Contains(k)))
            throw new InvalidOperationException("Cada tecla do combo precisa estar vinculada a Attack/Hotbar no jogo (sem modificadores).");
        _comboBindings = _comboKeys.Select(key => key.Equals(LiveKeySettings.Disabled, StringComparison.OrdinalIgnoreCase)
            ? "desabilitado" : string.Join("/", bindings.Where(b => (b.Key == "Attack" || CombatBindingValidator.IsHotbarSlot(b.Key)) &&
                b.Value.Contains(key, StringComparer.OrdinalIgnoreCase)).Select(b => b.Key))).ToArray();
        _log.Log(LogLevel.Info, "Bindings observados do combo: " + string.Join(" → ", _comboKeys.Select((key, i) =>
            $"posição {i + 1} {key}={_comboBindings[i]}")) + "; posição não prova efeito da ação", EventOrigin.Observed);
        if (combatSettings.UseAttack && (!bindings.TryGetValue("Attack", out var attack) ||
            !attack.Contains(combatSettings.AttackKey, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Configure a tecla de AA também no binding Attack do jogo.");
        _toolSlot = combatSettings.UseTool ? CombatBindingValidator.ResolveToolSlot(bindings, combatSettings.ToolKey) : null;
        _runConfig = Path.Combine(gameDirectory, "data files", "config.json");
        try
        {
            _runKey = RunBindingValidator.Resolve(bindings, LiveKeySettings.Keys[combatSettings.FollowHoldKey]);
            StaminaBindingStatus = $"Run = {bindings["Run"][0]}; modificador nativo permitido; exige HoldToRun=False";
        }
        catch (Exception ex) { StaminaBindingStatus = $"Configuração Run indisponível: {ex.Message}"; }
        _bindingTimes = _bindingFiles.Select(File.GetLastWriteTimeUtc).ToArray();
    }

    public TurnResult RequestTurn(Direction direction) => _turns.TryGetValue(direction, out var key)
        ? (Tap(key, extended: true, kind: LiveInputKind.Turning) ? TurnResult.Ok(LastInputReason) : TurnResult.Rejected(LastInputReason))
        : TurnResult.Rejected("Unsupported direction");

    public TurnResult RequestCastTurn(Direction direction, int maximumHoldMs) => _turns.TryGetValue(direction, out var key)
        ? (Tap(key, extended: true, kind: LiveInputKind.CastTurning, holdMs: Math.Clamp(maximumHoldMs, 40, 300)) ? TurnResult.Ok(LastInputReason) : TurnResult.Rejected(LastInputReason))
        : TurnResult.Rejected("Unsupported cast turn direction");

    public ActionResult Execute(string actionId, string key)
    {
        var index = actionId switch
        {
            "basic_attack" => 0, "followup_hotbar_Q" => 1, "followup_hotbar_S" => 2, _ => -1
        };
        if (index < 0 || key.Equals(LiveKeySettings.Disabled, StringComparison.OrdinalIgnoreCase) ||
            !key.Equals(_comboKeys[index], StringComparison.OrdinalIgnoreCase))
            return ActionResult.Rejected("Live action not configured");
        var virtualKey = LiveKeySettings.Keys[key];
        if (!_allowed(LiveInputKind.Punish)) return ActionResult.Rejected("Punish safety gate blocked");
        if (_activationKeys.Contains(virtualKey) && IsDown(virtualKey))
            return ActionResult.Deferred("Tecla compartilhada com Turn ainda segurada manualmente; solte para continuar o combo");
        var prepared = _prepareComboFacing();
        if (!prepared.Accepted) return prepared;
        if (!Tap(virtualKey, false, LiveInputKind.Punish)) return ActionResult.Rejected(LastInputReason);
        LastActionText = $"Combo posição {index + 1} ({key}, binding {_comboBindings[index]}) — tecla enviada, efeito/acerto NÃO confirmado";
        _log.Log(LogLevel.Info, LastActionText, EventOrigin.Executed);
        return ActionResult.Ok(LastActionText);
    }

    public ActionResult ExecuteCombat(CombatAction action, string key)
    {
        if (action is not (CombatAction.Attack or CombatAction.Tool)) return ActionResult.Rejected("Finalidade de ataque inválida");
        var expected = action == CombatAction.Attack ? _combatSettings.AttackKey : _combatSettings.ToolKey;
        if (!key.Equals(expected, StringComparison.OrdinalIgnoreCase)) return ActionResult.Rejected("Tecla AA/Tool não configurada");
        var kind = action == CombatAction.Attack ? LiveInputKind.Attack : LiveInputKind.Tool;
        if (!Tap(LiveKeySettings.Keys[key], false, kind)) return ActionResult.Rejected(LastInputReason);
        LastActionText = action == CombatAction.Tool
            ? $"Tool ({key}, slot {_toolSlot}) — atalho enviado; item/acerto NÃO confirmados pela memória"
            : $"AA {_combatSettings.AttackMode} ({key}) — tentativa enviada; acerto NÃO confirmado";
        _log.Log(LogLevel.Info, LastActionText, EventOrigin.Executed);
        return ActionResult.Ok(LastActionText);
    }

    public ActionResult Move(FollowStep step)
    {
        var key = MovementKey(step.Direction);
        if (key == 0 || step.Offset is null) return ActionResult.Rejected("Direção de movimento inválida");
        return _heldMovement.Set(step.Direction, !_faulted && BindingsUnchanged() && _allowed(LiveInputKind.Follow), step.SecondaryDirection)
            ? ActionResult.Ok("Setas mantidas pressionadas") : ActionResult.Rejected("Follow bloqueado ou seta manual/erro de input");
    }

    private bool BindingsUnchanged() => !_bindingFiles.Where((f, i) => File.GetLastWriteTimeUtc(f) != _bindingTimes[i]).Any();
    public void ReleaseFollow() => _heldMovement.Release();
    public void ServiceFollow()
    {
        try
        {
            if (_faulted || !BindingsUnchanged() || !_allowed(LiveInputKind.Follow)) ReleaseFollow();
        }
        catch { ReleaseFollow(); throw; }
    }
    private bool SendMovement(Direction direction, bool up)
    {
        if (!up && (_faulted || !_allowed(LiveInputKind.Follow))) return false;
        var scan = (ushort)MapVirtualKey(MovementKey(direction), 0);
        if (scan == 0) return false;
        var input = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput
            { ScanCode = scan, Flags = 8u | 1u | (up ? 2u : 0u) } } };
        var success = SendInput(1, new[] { input }, Marshal.SizeOf<Input>()) == 1;
        if (!success) { _faulted = true; LastInputReason = "Falha no input do Follow; reconecte"; }
        return success;
    }
    public void Dispose() => ReleaseFollow();

    public ActionResult ToggleRun()
    {
        if (!StaminaBindingValid) return ActionResult.Rejected("Configuração de corrida alterada/não validada; reconecte");
        var accepted = Tap(_runKey, _runKey == 0xA3, LiveInputKind.Stamina);
        if (accepted) _log.Log(LogLevel.Info, "Auto Stamina: toggle Run enviado; aguardando estado observado", EventOrigin.Executed);
        return accepted ? ActionResult.Ok("Toggle Run enviado") : ActionResult.Rejected(LastInputReason);
    }

    public static ushort MovementKey(Direction direction) => direction switch
    { Direction.Up => 0x26, Direction.Down => 0x28, Direction.Left => 0x25, Direction.Right => 0x27, _ => 0 };
    public bool OtherMovementHeld(FollowStep step)
    {
        return new[] { Direction.Up, Direction.Down, Direction.Left, Direction.Right }.Any(d =>
            d != step.Direction && d != step.SecondaryDirection && !_heldMovement.Owns(d) && IsDown(MovementKey(d)));
    }

    private bool Tap(ushort key, bool extended, LiveInputKind kind, int holdMs = 40)
    {
        if (_faulted) { LastInputReason = "Previous key-release fault; reconnect required"; return false; }
        if (!_allowed(kind)) { LastInputReason = $"{kind}: safety gate changed before dispatch"; return false; }
        if (IsDown(key)) { LastInputReason = $"Key 0x{key:X} already held manually"; return false; }
        if (_bindingFiles.Where((f, i) => File.GetLastWriteTimeUtc(f) != _bindingTimes[i]).Any())
        { LastInputReason = "Bindings file changed; reconnect required"; return false; }
        var scan = (ushort)MapVirtualKey(key, 0);
        if (scan == 0) { LastInputReason = "Key has no scan-code mapping"; return false; }
        var down = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { ScanCode = scan, Flags = 8u | (extended ? 1u : 0u) } } };
        var up = down;
        up.Data.Keyboard.Flags |= 2;
        var sent = false;
        try
        {
            if (!_allowed(kind)) { LastInputReason = $"{kind}: safety gate changed at dispatch"; return false; }
            sent = SendInput(1, new[] { down }, Marshal.SizeOf<Input>()) == 1;
            if (!sent)
            {
                LastInputReason = $"SendInput key-down rejected; Win32={Marshal.GetLastWin32Error()} (no bypass attempted)";
                _log.Log(LogLevel.Warning, LastInputReason);
                return false;
            }
            // Hold across at least one render/input poll; bounded so emergency/focus checks stay responsive.
            MaintainingCastTurn = kind == LiveInputKind.CastTurning;
            BoundedInputHold.Wait(holdMs, _holdClock, () =>
            {
                RefreshFollowDuringTap?.Invoke();
                ServiceFollow();
                return _allowed(kind);
            });
        }
        finally
        {
            MaintainingCastTurn = false;
            // Always release a key that this adapter pressed, including focus loss / exceptions.
            if (sent && SendInput(1, new[] { up }, Marshal.SizeOf<Input>()) != 1)
            {
                _faulted = true;
                LastInputReason = "Key-release dispatch failed; reconnect required";
                _log.Log(LogLevel.Error, "Key release dispatch failed; live input disabled until reconnect (key-down may have been received)");
            }
        }
        if (!_faulted) LastInputReason = $"Scan-code 0x{scan:X} dispatched; effect requires observed confirmation";
        return !_faulted;
    }

    public static bool IsDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool EmergencyPending()
    {
        if ((GetAsyncKeyState(0x7B) & 0x8001) != 0) Interlocked.Exchange(ref _emergencyLatched, 1);
        return Volatile.Read(ref _emergencyLatched) != 0;
    }
    // UI observes the latch but must NOT clear it before revoking module permissions.
    public static bool ConsumeEmergency() => EmergencyPending();
    public static void AcknowledgeEmergencyAfterDisarm() => Interlocked.Exchange(ref _emergencyLatched, 0);
    public static bool IsGameForeground(int pid)
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundPid);
        return foregroundPid == pid;
    }
    public static bool TextEntryFocused()
    {
        var info = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        if (!GetGUIThreadInfo(0, ref info)) return true;
        if (info.Caret != IntPtr.Zero) return true;
        var name = new StringBuilder(128);
        GetClassName(info.Focus, name, name.Capacity);
        return name.ToString().Contains("Edit", StringComparison.OrdinalIgnoreCase);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    {
        [FieldOffset(0)] public KeyboardInput Keyboard;
        [FieldOffset(0)] public MouseInput Mouse; // Ensures native INPUT union size on x86 and x64.
    }
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
    { public ushort VirtualKey, ScanCode; public uint Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput
    { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    { public uint Size, Flags; public IntPtr Active, Focus, Capture, MenuOwner, MoveSize, Caret; public Rect CaretRect; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int pid);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
}
