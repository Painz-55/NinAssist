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
public sealed class TargetMemoryWriter
{
	private bool _faulted;
	private const uint VmOperation = 0x0008, VmRead = 0x0010, VmWrite = 0x0020;
	private const string TestedHash = "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634";

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
		if (profile is null || frame is null || candidate is null || gate is null ||
			!profile.ExternalProfile || !profile.ActuationValidated ||
			!profile.ValidatedInputKinds.Contains(nameof(LiveInputKind.TargetMemory), StringComparer.Ordinal) ||
			!profile.Sha256.Equals(TestedHash, StringComparison.OrdinalIgnoreCase) ||
			profile.SelectedTarget is not { PlayerKind: 1 } fields || IntPtr.Size != 8 || !gate())
			return ActionResult.Rejected("Build, perfil ou autorização de escrita inválidos");
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
			using Process process = Process.GetProcessById(pid);
			if (process.HasExited || process.ProcessName != "NinOnline")
				return ActionResult.Rejected("Processo do jogo mudou");
			string directory = Path.GetDirectoryName(process.MainModule?.FileName ?? "") ?? "";
			if (!HashMatches(Path.Combine(directory, "NinOnline.dll"), profile.Sha256) ||
				!HashMatches(Path.Combine(directory, "Common.dll"), profile.CommonSha256))
				return ActionResult.Rejected("Hash da build mudou");
			using DataTarget target = DataTarget.AttachToProcess(pid, suspend: false);
			using ClrRuntime runtime = target.ClrVersions.Single().CreateRuntime();
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
			// ClrMD's instance offsets start after the 8-byte method-table pointer on x64.
			ulong kindAddress = selected.Address + (ulong)IntPtr.Size;
			nint handle = OpenProcess(VmOperation | VmRead | VmWrite, false, pid);
			if (handle == 0) return ActionResult.Rejected("Acesso à memória recusado");
			try
			{
				byte[] before = new byte[8];
				if (!ReadProcessMemory(handle, (nint)kindAddress, before, 8, out nuint read) || read != 8 ||
					BitConverter.ToInt32(before, 0) != selected.ReadField<int>(fields.Kind) ||
					BitConverter.ToInt32(before, 4) != selected.ReadField<int>(fields.Index) ||
					selectedField.ReadObject(domain).Address != selected.Address || !gate())
					return ActionResult.Rejected("Alvo ou autorização mudou antes da escrita");
				byte[] desired = new byte[8];
				BitConverter.GetBytes(fields.PlayerKind).CopyTo(desired, 0);
				BitConverter.GetBytes(matches[0].Slot).CopyTo(desired, 4);
				writeStarted = true;
				if (!WriteProcessMemory(handle, (nint)kindAddress, desired, 8, out nuint written) || written != 8)
				{
					_faulted = true;
					return ActionResult.Rejected("Escrita incompleta; desative e reconecte antes de tentar novamente");
				}
				byte[] after = new byte[8];
				if (!ReadProcessMemory(handle, (nint)kindAddress, after, 8, out read) || read != 8 ||
					!after.SequenceEqual(desired) || selectedField.ReadObject(domain).Address != selected.Address)
				{
					_faulted = true;
					return ActionResult.Rejected("Confirmação imediata falhou; desative e reconecte");
				}
				return ActionResult.Ok("Campos escritos; aguardando leitura posterior do alvo no cliente");
			}
			finally { CloseHandle(handle); }
		}
		catch (Exception ex)
		{
			if (writeStarted) _faulted = true;
			return ActionResult.Rejected("Escrita bloqueada: " + ex.GetType().Name + (_faulted ? "; reconecte" : ""));
		}
	}

	private static bool HashMatches(string path, string expected)
	{
		using var file = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase);
	}
}
