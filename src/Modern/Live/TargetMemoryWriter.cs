using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

/// <summary>Experimental, fail-closed selection writer. Never cache a managed object address.</summary>
public sealed class TargetMemoryWriter : IDisposable
{
	private bool _faulted;
	private int _trialWriteCount;
	private long _trialWriteFrameAt;
	private string? _trialSelectedId;
	private string? _trialLocalId;
	private bool _trialFirstConfirmed;
	private Process? _process;
	private DataTarget? _target;
	private ClrRuntime? _runtime;
	private nint _handle;
	private int _pid;
	private string? _gameHash;
	private string? _commonHash;
	private const uint VmOperation = 0x0008, VmRead = 0x0010, VmWrite = 0x0020;
	private const string TestedHash = "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634";
	private const string TrialHash = "8A163FC3D8FA13CCB3AA1FBABD9D1EBA1C829FBAE4ED14CC05AC688CAD328D50";
	private const string TrialCommonHash = "43B99E8E93F063DFA1239978C7B41C51520E765CAB8D21499160FCA14CD01F62";
	private const string SteamTrialHash = "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525";
	private const string SteamTrialCommonHash = "637E4DB9D08CA4C9285B80E36088C15AEF0D2547FA92D11F48980C2B821C3102";
	private const string SteamOfficialId = "Nin Online Steam 2026-11-26 (validated core; restricted)";

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern nint OpenProcess(uint access, bool inherit, int pid);
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint handle);
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool ReadProcessMemory(nint handle, nint address, byte[] buffer, nuint size, out nuint read);
	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool WriteProcessMemory(nint handle, nint address, byte[] buffer, nuint size, out nuint written);

	public ActionResult Select(int pid, GameMemoryProfile profile, LiveFrame frame, TargetInfo candidate, Func<bool> gate)
	{
		if (_faulted) return ActionResult.Rejected("Falha de escrita anterior; reconecte antes de tentar novamente");
		bool steamTrial = profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false } &&
			profile.Sha256 == SteamTrialHash && profile.CommonSha256 == SteamTrialCommonHash &&
			(profile.Id == "LOCAL TRIAL Steam Raizen TargetMemory 2026-11-26" &&
			 profile.PunishComboValidated == false &&
			 profile.ValidatedInputKinds.SequenceEqual([nameof(LiveInputKind.TargetMemory)]) &&
			 profile.ValidatedAttackModes.Length == 0 ||
			 profile.Id == "LOCAL TRIAL Steam Integrated Raizen 2026-11-26" &&
			 profile.PunishComboValidated == true && profile.PunishComboRequiresTarget == true &&
			 profile.ValidatedInputKinds.SequenceEqual(["Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory"]) &&
			 profile.ValidatedAttackModes.SequenceEqual(["Melee", "Fan"])) &&
			profile.SelectedTarget is { GlobalObject: "YPDrG8hKA8g", Kind: "iI1YjePn4ca", Index: "Al8Yj3jCFMN", PlayerKind: 1 };
		bool standaloneTrial = profile is { ExternalProfile: true, ExperimentalTrial: true, ActuationValidated: false } &&
			profile.Id.StartsWith("LOCAL TRIAL ", StringComparison.Ordinal) &&
			profile.Sha256 == TrialHash && profile.CommonSha256 == TrialCommonHash &&
			(profile.ValidatedInputKinds.SequenceEqual([nameof(LiveInputKind.TargetMemory)]) &&
			 profile.ValidatedAttackModes.Length == 0 && profile.PunishComboValidated == false ||
			 profile.ValidatedInputKinds.SequenceEqual(["Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory"]) &&
			 profile.ValidatedAttackModes.SequenceEqual(["Melee", "Fan"]) &&
			 profile.PunishComboValidated == true && profile.PunishComboRequiresTarget == true) &&
			profile.SelectedTarget is { GlobalObject: "F3miMmd6RE4", Kind: "dxFYxym9RQk", Index: "okyYx7CwjXs", PlayerKind: 1 };
		bool trial = steamTrial || standaloneTrial;
		bool steamOfficial = profile is { ExternalProfile: true, ExperimentalTrial: false, ActuationValidated: true,
			Id: SteamOfficialId, PunishComboValidated: false } &&
			profile.Sha256 == SteamTrialHash && profile.CommonSha256 == SteamTrialCommonHash &&
			profile.ValidatedInputKinds.SequenceEqual(["Turning", "Stamina", "TargetMemory"]) &&
			profile.ValidatedAttackModes.Length == 0 &&
			profile.SelectedTarget is { GlobalObject: "YPDrG8hKA8g", Kind: "iI1YjePn4ca", Index: "Al8Yj3jCFMN", PlayerKind: 1 };
		bool steamLayout = steamTrial || steamOfficial;
		if (profile is null || frame is null || candidate is null || gate is null ||
			!profile.ExternalProfile || !(profile.ActuationValidated && !profile.ExperimentalTrial || trial) ||
			!profile.ValidatedInputKinds.Contains(nameof(LiveInputKind.TargetMemory), StringComparer.Ordinal) ||
			!(profile.Sha256.Equals(TestedHash, StringComparison.OrdinalIgnoreCase) || trial || steamOfficial) ||
			profile.SelectedTarget is not { PlayerKind: 1 } fields || IntPtr.Size != 8 || !gate())
			return ActionResult.Rejected("Build, perfil ou autorização de escrita inválidos");
		if ((trial || steamOfficial) && (frame.Map != 190 || frame.PlayerName != "Raizen Kaen" ||
			frame.Snapshot.Player.Id.Split(':').LastOrDefault() != "Raizen Kaen"))
			return ActionResult.Rejected("Steam restrita a Raizen no mapa 190");
		if (trial && (_trialWriteCount >= 2 || _trialWriteCount == 1 && !_trialFirstConfirmed))
			return ActionResult.Rejected("Ensaio local aguardando confirmação ou limite de duas escritas atingido");
		if (trial && _trialWriteCount == 1 && frame.Snapshot.Target is not null)
			return ActionResult.Rejected("Ensaio exige alvo limpo por Esc após confirmação da primeira escrita");
		if (trial && _trialWriteCount == 1 && candidate.Id != _trialSelectedId)
			return ActionResult.Rejected("Segundo ensaio restrito ao mesmo jogador confirmado");
		if (frame.Map <= 0 || !frame.Snapshot.Player.IsAlive || frame.MapPlayers is not { UnreadableSlots: 0 } map ||
			map.Map != frame.Map || map.LocalId != frame.Snapshot.Player.Id ||
			frame.CapturedAtMs < map.CapturedAtMs || frame.CapturedAtMs - map.CapturedAtMs > 500)
			return ActionResult.Rejected("Mapa ou leitura dos jogadores não confiável");
		MapPlayer[] matches = map.Players.Where(p => p.IsAlive && !p.IsLocal && p.Map == frame.Map &&
			$"{p.Slot}:{p.Name}" == candidate.Id && p.X == candidate.PositionX && p.Y == candidate.PositionY).ToArray();
		if (matches.Length != 1 || matches[0].Slot <= 0 || matches[0].Slot > 500 || frame.Snapshot.Target?.Id == candidate.Id)
			return ActionResult.Rejected("Slot do alvo mudou ou já está selecionado");
		bool writeStarted = false;
		try
		{
			if (_process is null)
			{
				_process = Process.GetProcessById(pid);
				if (_process.HasExited || _process.ProcessName != "NinOnline")
					return ActionResult.Rejected("Processo do jogo mudou");
				string directory = Path.GetDirectoryName(_process.MainModule?.FileName ?? "") ?? "";
				if (!HashMatches(Path.Combine(directory, "NinOnline.dll"), profile.Sha256) ||
					!HashMatches(Path.Combine(directory, "Common.dll"), profile.CommonSha256))
					return ActionResult.Rejected("Hash da build mudou");
				_pid = pid;
				_gameHash = profile.Sha256;
				_commonHash = profile.CommonSha256;
				_target = DataTarget.AttachToProcess(pid, suspend: false);
				_runtime = _target.ClrVersions.Single().CreateRuntime();
				_handle = OpenProcess(VmOperation | VmRead | VmWrite, false, pid);
				if (_handle == 0) return ActionResult.Rejected("Acesso à memória recusado");
			}
			if (_process.HasExited || _pid != pid || _gameHash != profile.Sha256 ||
				_commonHash != profile.CommonSha256 || _handle == 0 || _runtime is null)
			{
				_faulted = true;
				return ActionResult.Rejected("Processo/perfil mudou; reconecte");
			}
			_runtime.FlushCachedData();
			ClrRuntime runtime = _runtime;
			var module = runtime.EnumerateModules().Single(m => Path.GetFileName(m.Name) == "NinOnline.dll");
			var globals = module.GetTypeByName(profile.Globals);
			var roots = module.GetTypeByName(profile.Roots);
			var domain = runtime.AppDomains.Single();
			if (globals is null || roots is null) return ActionResult.Rejected("Schema do jogo mudou");
			var localField = globals.GetStaticFieldByName(profile.LocalIndex);
			var playerField = roots.GetStaticFieldByName(profile.Players);
			var selectedField = globals.GetStaticFieldByName(fields.GlobalObject);
			if (localField is null || playerField is null || selectedField is null || !selectedField.IsInitialized(domain) ||
				!playerField.IsInitialized(domain) || !localField.IsInitialized(domain) || selectedField.Type?.IsValueType != false)
				return ActionResult.Rejected("Campos de seleção indisponíveis");
			int local = localField.Read<int>(domain);
			var players = playerField.ReadObject(domain);
			if (!players.IsArray || local <= 0 || local >= players.AsArray().Length || matches[0].Slot >= players.AsArray().Length)
				return ActionResult.Rejected("Índices dos jogadores inválidos");
			var own = players.AsArray().GetObjectValue(local);
			var wanted = players.AsArray().GetObjectValue(matches[0].Slot);
			if (own.IsNull || wanted.IsNull ||
				$"{local}:{own.ReadStringField(profile.Name, 64)}" != frame.Snapshot.Player.Id ||
				wanted.ReadStringField(profile.Name, 64) != matches[0].Name ||
				wanted.ReadField<int>(profile.Map) != frame.Map || own.ReadField<int>(profile.Map) != frame.Map)
				return ActionResult.Rejected("Identidade do mapa/alvo mudou");
			var health = wanted.ReadObjectField(profile.Vitals);
			if (health.IsNull || !health.IsArray || health.Type?.Name != "System.Int32[]" || health.AsArray().Length != 4)
				return ActionResult.Rejected("Vida do jogador indisponível");
			int[]? vitals = health.AsArray().ReadValues<int>(0, 4);
			if (vitals is null || vitals[1] <= 0 ||
				Math.Abs(wanted.ReadField<float>(profile.PixelX) / 32.0 - candidate.PositionX) > 0.25 ||
				Math.Abs(wanted.ReadField<float>(profile.PixelY) / 32.0 - candidate.PositionY) > 0.25)
				return ActionResult.Rejected("Jogador não está vivo");
			var selected = selectedField.ReadObject(domain);
			if (selected.IsNull || selected.Type?.Name != selectedField.Type?.Name ||
				selected.Type?.GetFieldByName(fields.Kind)?.Type?.Name != "System.Int32" ||
				selected.Type?.GetFieldByName(fields.Index)?.Type?.Name != "System.Int32")
				return ActionResult.Rejected("Layout do alvo mudou");
			int kindOffset = selected.Type!.GetFieldByName(fields.Kind)!.Offset;
			int indexOffset = selected.Type.GetFieldByName(fields.Index)!.Offset;
			if (kindOffset != 0 || indexOffset != sizeof(int))
				return ActionResult.Rejected("Campos do alvo não são contíguos");
			string historyKind = steamLayout ? "KXvYjpqD9OL" : "KZxYx3XP1Gd";
			string historyIndex = steamLayout ? "hPDYjEat2my" : "MCWYxSJiKSc";
			if ((trial || steamOfficial) && (selected.Type.GetFieldByName(historyKind)?.Offset != 8 ||
				selected.Type.GetFieldByName(historyIndex)?.Offset != 12 ||
				selected.Type.GetFieldByName(historyKind)?.Type?.Name != "System.Int32" ||
				selected.Type.GetFieldByName(historyIndex)?.Type?.Name != "System.Int32"))
				return ActionResult.Rejected("Layout do histórico do alvo mudou");
			// ClrMD's instance offsets start after the 8-byte method-table pointer on x64.
			ulong kindAddress = selected.Address + (ulong)IntPtr.Size;
			{
				int length = trial || steamOfficial ? 16 : 8;
				byte[] before = new byte[length];
				if (!ReadProcessMemory(_handle, (nint)kindAddress, before, (nuint)length, out nuint read) || read != (nuint)length ||
					BitConverter.ToInt32(before, 0) != selected.ReadField<int>(fields.Kind) ||
					BitConverter.ToInt32(before, 4) != selected.ReadField<int>(fields.Index) ||
					((trial || steamOfficial) && (BitConverter.ToInt32(before, 8) != selected.ReadField<int>(historyKind) ||
						BitConverter.ToInt32(before, 12) != selected.ReadField<int>(historyIndex))) ||
					selectedField.ReadObject(domain).Address != selected.Address || !gate())
					return ActionResult.Rejected("Alvo ou autorização mudou antes da escrita");
				if ((trial || steamOfficial) && (frame.Snapshot.Target?.Id is { } currentId && currentId !=
					$"{BitConverter.ToInt32(before, 4)}:{frame.TargetName}" ||
					BitConverter.ToInt32(before, 0) is not (0 or 1) ||
					(BitConverter.ToInt32(before, 0) == 0 && BitConverter.ToInt32(before, 4) != 0)))
					return ActionResult.Rejected("Estado do alvo não confiável para ensaio");
				byte[] desired = new byte[8];
				BitConverter.GetBytes(fields.PlayerKind).CopyTo(desired, 0);
				BitConverter.GetBytes(matches[0].Slot).CopyTo(desired, 4);
				if (trial)
				{
					_trialWriteCount++;
					_trialWriteFrameAt = frame.CapturedAtMs;
					_trialSelectedId = candidate.Id;
					_trialLocalId = frame.Snapshot.Player.Id;
				}
				writeStarted = true;
				if (!WriteProcessMemory(_handle, (nint)kindAddress, desired, 8, out nuint written) || written != 8)
				{
					_faulted = true;
					return ActionResult.Rejected("Escrita incompleta; desative e reconecte antes de tentar novamente");
				}
				byte[] after = new byte[8];
				if (!ReadProcessMemory(_handle, (nint)kindAddress, after, 8, out read) || read != 8 ||
					!after.SequenceEqual(desired) || selectedField.ReadObject(domain).Address != selected.Address)
				{
					_faulted = true;
					return ActionResult.Rejected("Confirmação imediata falhou; desative e reconecte");
				}
				if ((trial || steamOfficial) && (selected.ReadField<int>(historyKind) != BitConverter.ToInt32(before, 8) ||
					selected.ReadField<int>(historyIndex) != BitConverter.ToInt32(before, 12)))
				{
					_faulted = true;
					return ActionResult.Rejected("Histórico do alvo mudou; reconecte");
				}
				return ActionResult.Ok("Campos escritos; aguardando leitura posterior do alvo no cliente");
			}
		}
		catch (Exception ex)
		{
			if (writeStarted) _faulted = true;
			return ActionResult.Rejected("Escrita bloqueada: " + ex.GetType().Name + (_faulted ? "; reconecte" : ""));
		}
	}

	/// <summary>Only an independent later provider frame can arm the second local-trial write.</summary>
	public void ObserveTrialFrame(LiveFrame? frame)
	{
		if (_trialWriteCount != 1 || _trialFirstConfirmed || frame is null ||
			frame.CapturedAtMs <= _trialWriteFrameAt || frame.Map != 190 ||
			frame.PlayerName != "Raizen Kaen" || frame.Snapshot.Player.Id != _trialLocalId ||
			frame.MapPlayers is not { UnreadableSlots: 0 } players ||
			players.Map != frame.Map || players.LocalId != _trialLocalId ||
			frame.CapturedAtMs < players.CapturedAtMs || frame.ReadDurationMs > 500 ||
			frame.CapturedAtMs - players.CapturedAtMs > 500 ||
			frame.Snapshot.Target is not { IsAlive: true, IsTargeted: true } target || target.Id != _trialSelectedId)
			return;
		_trialFirstConfirmed = true;
	}

	public void Dispose()
	{
		_faulted = true;
		if (_handle != 0) { CloseHandle(_handle); _handle = 0; }
		_runtime?.Dispose();
		_target?.Dispose();
		_process?.Dispose();
		_runtime = null;
		_target = null;
		_process = null;
	}

	private static bool HashMatches(string path, string expected)
	{
		using var file = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase);
	}
}
