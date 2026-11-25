using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NinOnlineAutoAssist.Live;

public static class SignedProfileCatalog
{
	public const string FeedUrl = "https://raw.githubusercontent.com/Painz-55/NinAssist/main/compatibility/profiles.json";

	public const string PublicKey = "-----BEGIN PUBLIC KEY-----\r\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqyxEIXbJaWxmyaldzpR94Rf4uURa\r\n11hsxWnZljnORHzLXlTGn4xaM1OtGFgCFC5Tim6+U0/y4T/cXTefRhuEiA==\r\n-----END PUBLIC KEY-----";

	private static readonly string[] RequiredNames = new string[23]
	{
		"Id", "Sha256", "CommonSha256", "Roots", "Globals", "Players", "LocalIndex", "Name", "Map", "PixelX",
		"PixelY", "Facing", "Vitals", "MaxVitals", "Running", "Buffs", "BuffActive", "BuffName", "BuffDuration", "MapRoot",
		"MapIdentity", "Tiles", "Projectiles"
	};

	private static readonly string[] CastNames = new string[5] { "GlobalSpell", "GlobalStart", "GameClock", "PlayerSpell", "PlayerEnd" };

	private static readonly string[] TargetNames = new string[3] { "GlobalObject", "Kind", "Index" };

	private static readonly Regex FieldName = new Regex("^[A-Za-z_][A-Za-z0-9_`.+]{0,200}$", RegexOptions.CultureInvariant);

	public static GameMemoryProfile? VerifyAndSelect(byte[] payload, byte[] signature, string publicKey, string gameHash, string commonHash)
	{
		int num = payload.Length;
		bool flag = ((num < 2 || num > 65536) ? true : false);
		bool flag2 = flag;
		if (!flag2)
		{
			int num2 = signature.Length;
			bool flag3 = ((num2 < 64 || num2 > 256) ? true : false);
			flag2 = flag3;
		}
		if (flag2)
		{
			throw new InvalidOperationException("Invalid profile feed size");
		}
		using ECDsa eCDsa = ECDsa.Create();
		eCDsa.ImportFromPem(publicKey);
		if (!eCDsa.VerifyData(payload, signature, HashAlgorithmName.SHA256))
		{
			throw new CryptographicException("Profile feed signature invalid");
		}
		using JsonDocument jsonDocument = JsonDocument.Parse((ReadOnlyMemory<byte>)payload, new JsonDocumentOptions
		{
			MaxDepth = 10
		});
		JsonElement rootElement = jsonDocument.RootElement;
		if (rootElement.ValueKind != JsonValueKind.Object || rootElement.GetProperty("schema").GetInt32() != 1)
		{
			throw new InvalidOperationException("Unsupported profile schema");
		}
		JsonElement property = rootElement.GetProperty("profiles");
		if (property.ValueKind != JsonValueKind.Array || property.GetArrayLength() > 32)
		{
			throw new InvalidOperationException("Invalid profile list");
		}
		GameMemoryProfile gameMemoryProfile = null;
		foreach (JsonElement item in property.EnumerateArray())
		{
			if (item.ValueKind != JsonValueKind.Object)
			{
				throw new InvalidOperationException("Invalid profile");
			}
			string? text = item.GetProperty("Sha256").GetString();
			string text2 = item.GetProperty("CommonSha256").GetString();
			if (!IsHash(text) || !IsHash(text2))
			{
				throw new InvalidOperationException("Invalid fingerprint");
			}
			if (!string.Equals(text, gameHash, StringComparison.OrdinalIgnoreCase) || !string.Equals(text2, commonHash, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			if ((object)gameMemoryProfile != null)
			{
				throw new InvalidOperationException("Ambiguous signed profile");
			}
			string[] requiredNames = RequiredNames;
			foreach (string text3 in requiredNames)
			{
				string text4 = item.GetProperty(text3).GetString();
				flag2 = string.IsNullOrWhiteSpace(text4) || text4.Length > 200;
				if (!flag2)
				{
					switch (text3)
					{
					case "Id":
					case "Sha256":
					case "CommonSha256":
						flag = true;
						break;
					default:
						flag = false;
						break;
					}
					flag2 = !flag && !FieldName.IsMatch(text4);
				}
				if (flag2)
				{
					throw new InvalidOperationException("Invalid profile field " + text3);
				}
			}
			JsonElement property2 = item.GetProperty("CastTiming");
			if (property2.ValueKind != JsonValueKind.Object)
			{
				throw new InvalidOperationException("Missing cast mapping");
			}
			requiredNames = CastNames;
			foreach (string name in requiredNames)
			{
				RequireField(property2, name);
			}
			JsonElement property3 = item.GetProperty("SelectedTarget");
			if (property3.ValueKind == JsonValueKind.Object)
			{
				requiredNames = TargetNames;
				foreach (string name2 in requiredNames)
				{
					RequireField(property3, name2);
				}
				num = property3.GetProperty("PlayerKind").GetInt32();
				if ((num < 0 || num > 16) ? true : false)
				{
					throw new InvalidOperationException("Invalid target kind");
				}
				if (item.GetProperty("SelectedIndex").ValueKind != JsonValueKind.Null)
				{
					RequireField(item, "SelectedIndex");
				}
			}
			else
			{
				if (property3.ValueKind != JsonValueKind.Null)
				{
					throw new InvalidOperationException("Invalid target schema");
				}
				RequireField(item, "SelectedIndex");
			}
			if (item.GetProperty("Village").ValueKind != JsonValueKind.Null)
			{
				RequireField(item, "Village");
			}
			item.GetProperty("ActuationValidated").GetBoolean();
			JsonElement value;
			bool flag4 = item.TryGetProperty("ExperimentalTrial", out value) && value.GetBoolean();
			if (flag4 && (item.GetProperty("ActuationValidated").GetBoolean() || !item.GetProperty("Id").GetString().StartsWith("LOCAL TRIAL ", StringComparison.Ordinal)))
			{
				throw new InvalidOperationException("Invalid local trial profile");
			}
			item.GetProperty("MapIdentityInRoots").GetBoolean();
			JsonElement property4 = item.GetProperty("ValidatedInputKinds");
			if (property4.ValueKind != JsonValueKind.Array || property4.GetArrayLength() > 8 || property4.EnumerateArray().Any(delegate(JsonElement a)
			{
				bool flag5 = a.ValueKind != JsonValueKind.String;
				if (!flag5)
				{
					bool flag6;
					switch (a.GetString())
					{
					case "Stamina":
					case "Turning":
					case "Attack":
					case "Follow":
					case "Punish":
					case "Tool":
					case "CastTurning":
					case "TargetMemory":
						flag6 = true;
						break;
					default:
						flag6 = false;
						break;
					}
					flag5 = !flag6;
				}
				return flag5;
			}))
			{
				throw new InvalidOperationException("Invalid approved input list");
			}
			if ((from a in property4.EnumerateArray()
				select a.GetString()).Distinct().Count() != property4.GetArrayLength())
			{
				throw new InvalidOperationException("Duplicate approved input kind");
			}
			bool targetTrial = flag4 && text == "8A163FC3D8FA13CCB3AA1FBABD9D1EBA1C829FBAE4ED14CC05AC688CAD328D50" &&
				text2 == "43B99E8E93F063DFA1239978C7B41C51520E765CAB8D21499160FCA14CD01F62" &&
				(property4.GetArrayLength() == 1 && property4[0].GetString() == "TargetMemory" &&
				 item.GetProperty("PunishComboValidated").GetBoolean() == false &&
				 item.GetProperty("ValidatedAttackModes").GetArrayLength() == 0 ||
				 property4.GetArrayLength() == 8 &&
				 property4.EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { "Turning", "Punish", "Attack", "Tool", "Follow", "Stamina", "CastTurning", "TargetMemory" }) &&
				 item.GetProperty("PunishComboValidated").GetBoolean() && item.GetProperty("PunishComboRequiresTarget").GetBoolean() &&
				 item.GetProperty("ValidatedAttackModes").EnumerateArray().Select(a => a.GetString()).SequenceEqual(new[] { "Melee", "Fan" })) &&
				property3.ValueKind == JsonValueKind.Object &&
				property3.GetProperty("GlobalObject").GetString() == "F3miMmd6RE4" &&
				property3.GetProperty("Kind").GetString() == "dxFYxym9RQk" &&
				property3.GetProperty("Index").GetString() == "okyYx7CwjXs";
			if (flag4 && text == "8A163FC3D8FA13CCB3AA1FBABD9D1EBA1C829FBAE4ED14CC05AC688CAD328D50" && !targetTrial)
				throw new InvalidOperationException("Local trial restricted to exact TargetMemory build and fields");
			if (property4.EnumerateArray().Any(a => a.GetString() == "TargetMemory") &&
				(text != "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634" && !targetTrial ||
				 property3.ValueKind != JsonValueKind.Object || property3.GetProperty("PlayerKind").GetInt32() != 1))
				throw new InvalidOperationException("TargetMemory requires the validated exact game build and player schema");
			if (!item.GetProperty("ActuationValidated").GetBoolean() && !flag4 && property4.GetArrayLength() != 0)
			{
				throw new InvalidOperationException("Read-only profile cannot approve input");
			}
			JsonElement value2;
			JsonElement jsonElement = (item.TryGetProperty("ValidatedAttackModes", out value2) ? value2 : default(JsonElement));
			if (jsonElement.ValueKind != JsonValueKind.Undefined && (jsonElement.ValueKind != JsonValueKind.Array || jsonElement.GetArrayLength() > 3 || jsonElement.EnumerateArray().Any(delegate(JsonElement m)
			{
				bool flag5 = m.ValueKind != JsonValueKind.String;
				if (!flag5)
				{
					bool flag6;
					switch (m.GetString())
					{
					case "Melee":
					case "Fan":
					case "HisuyFan":
						flag6 = true;
						break;
					default:
						flag6 = false;
						break;
					}
					flag5 = !flag6;
				}
				return flag5;
			}) || (from m in jsonElement.EnumerateArray()
				select m.GetString()).Distinct().Count() != jsonElement.GetArrayLength()))
			{
				throw new InvalidOperationException("Invalid approved attack modes");
			}
			if (jsonElement.ValueKind == JsonValueKind.Array && jsonElement.GetArrayLength() > 0 && ((!item.GetProperty("ActuationValidated").GetBoolean() && !flag4) || !property4.EnumerateArray().Any((JsonElement a) => a.GetString() == "Attack")))
			{
				throw new InvalidOperationException("Attack mode requires explicit Attack approval");
			}
			GameMemoryProfile? obj = item.Deserialize<GameMemoryProfile>() ?? throw new InvalidOperationException("Invalid signed profile");
			gameMemoryProfile = obj with
			{
				ExternalProfile = true
			};
		}
		return gameMemoryProfile;
	}

	public static GameMemoryProfile? Fetch(string gameHash, string commonHash)
	{
		string text = Path.Combine(AppContext.BaseDirectory, "compatibility", "profiles.json");
		if (File.Exists(text))
		{
			byte[] payload = File.ReadAllBytes(text);
			byte[] signature = Convert.FromBase64String(File.ReadAllText(text + ".sig").Trim());
			GameMemoryProfile gameMemoryProfile = VerifyAndSelect(payload, signature, "-----BEGIN PUBLIC KEY-----\r\nMFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEqyxEIXbJaWxmyaldzpR94Rf4uURa\r\n11hsxWnZljnORHzLXlTGn4xaM1OtGFgCFC5Tim6+U0/y4T/cXTefRhuEiA==\r\n-----END PUBLIC KEY-----", gameHash, commonHash);
			if ((object)gameMemoryProfile != null)
			{
				return gameMemoryProfile;
			}
		}
		return null;
	}

	private static byte[] GetLimited(HttpClient client, string url)
	{
		using HttpResponseMessage httpResponseMessage = client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
		if (httpResponseMessage.StatusCode != HttpStatusCode.OK || httpResponseMessage.Content.Headers.ContentLength > 65536)
		{
			throw new InvalidOperationException("Signed compatibility feed unavailable");
		}
		using Stream stream = httpResponseMessage.Content.ReadAsStream();
		using MemoryStream memoryStream = new MemoryStream();
		byte[] array = new byte[8192];
		int num;
		while ((num = stream.Read(array)) > 0)
		{
			if (memoryStream.Length + num > 65536)
			{
				throw new InvalidOperationException("Compatibility feed too large");
			}
			memoryStream.Write(array, 0, num);
		}
		return memoryStream.ToArray();
	}

	private static void RequireField(JsonElement element, string name)
	{
		string text = element.GetProperty(name).GetString();
		if (text == null || !FieldName.IsMatch(text))
		{
			throw new InvalidOperationException("Invalid field " + name);
		}
	}

	private static bool IsHash(string? hash)
	{
		if (hash != null && hash.Length == 64)
		{
			return hash.All(Uri.IsHexDigit);
		}
		return false;
	}
}
