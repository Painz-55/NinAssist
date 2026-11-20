using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace NinOnlineAutoAssist.Live;

internal sealed class FullMapCache
{
	private const uint Magic = 0x50414D4E;
	private const int FormatVersion = 1;
	private readonly string _directory;
	private readonly GameMemoryProfile _profile;

	public FullMapCache(GameMemoryProfile profile)
	{
		_profile = profile;
		_directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NinOnlineAutoAssist", "MapCache");
	}

	public bool TryLoad(int map, int width, int height, ulong terrainSignature, long capturedAtMs, out FullMapFrame? frame)
	{
		frame = null;
		try
		{
			string path = CachePath(map);
			if (!File.Exists(path)) return false;
			byte[] file = File.ReadAllBytes(path);
			if (file.Length < 64 || file.Length > 2_000_000) return false;
			int payloadLength = file.Length - 32;
			byte[] expected = SHA256.HashData(file.AsSpan(0, payloadLength));
			if (!CryptographicOperations.FixedTimeEquals(expected, file.AsSpan(payloadLength, 32))) return false;
			using var stream = new MemoryStream(file, 0, payloadLength, writable: false);
			using var reader = new BinaryReader(stream);
			if (reader.ReadUInt32() != Magic || reader.ReadInt32() != FormatVersion) return false;
			if (reader.ReadInt32() != map || reader.ReadInt32() != width || reader.ReadInt32() != height) return false;
			if (reader.ReadUInt64() != terrainSignature) return false;
			if (!string.Equals(reader.ReadString(), _profile.Sha256, StringComparison.OrdinalIgnoreCase)) return false;
			if (!string.Equals(reader.ReadString(), _profile.CommonSha256, StringComparison.OrdinalIgnoreCase)) return false;
			int count = reader.ReadInt32();
			if (count != checked(width * height)) return false;
			byte[] cells = reader.ReadBytes(count);
			if (cells.Length != count || stream.Position != payloadLength) return false;
			var terrain = new MapTerrainCell[count];
			for (int i = 0; i < count; i++)
			{
				if (cells[i] > (byte)MapTerrainCell.Special) return false;
				terrain[i] = (MapTerrainCell)cells[i];
			}
			frame = new FullMapFrame(map, capturedAtMs, width, height, Array.AsReadOnly(terrain));
			return true;
		}
		catch
		{
			frame = null;
			return false;
		}
	}

	public void Save(FullMapFrame frame, ulong terrainSignature)
	{
		Directory.CreateDirectory(_directory);
		string path = CachePath(frame.Map);
		string temporary = path + ".tmp";
		using var payload = new MemoryStream();
		using (var writer = new BinaryWriter(payload, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write(Magic);
			writer.Write(FormatVersion);
			writer.Write(frame.Map);
			writer.Write(frame.Width);
			writer.Write(frame.Height);
			writer.Write(terrainSignature);
			writer.Write(_profile.Sha256);
			writer.Write(_profile.CommonSha256);
			writer.Write(frame.Cells.Count);
			foreach (MapTerrainCell cell in frame.Cells) writer.Write((byte)cell);
		}
		byte[] data = payload.ToArray();
		byte[] checksum = SHA256.HashData(data);
		using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
		{
			output.Write(data);
			output.Write(checksum);
			output.Flush(flushToDisk: true);
		}
		File.Move(temporary, path, overwrite: true);
	}

	private string CachePath(int map)
	{
		string build = new string(_profile.Sha256.Take(12).Where(char.IsLetterOrDigit).ToArray());
		return Path.Combine(_directory, $"map-{map}-{build}.nmap");
	}
}
