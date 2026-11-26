using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Microsoft.Diagnostics.Runtime;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Models;

namespace NinOnlineAutoAssist.Live;

public sealed class ReadOnlyGameStateProvider : IGameStateProvider, IDisposable
{
	private readonly Process _process;

	private readonly DataTarget _target;

	private readonly ClrRuntime _runtime;

	private readonly IMonotonicClock _clock;

	private readonly LocalCharacterIdentity _identity;

	private ulong _globalsMethodTable;

	private ulong _rootsMethodTable;

	private int _externalSampleCount;

	private int _externalSampleMap;

	private string? _externalSampleName;

	private MapTerrainCell[]? _fullMapCells;

	private int _fullMapCursor;

	private int _fullMapBuildingMap;

	private int _fullMapWidth;

	private int _fullMapHeight;

	private FullMapCache? _fullMapCache;

	private ulong _fullMapSignature;

	public string? DetectedCharacterName => _identity.Name;

	public int ProcessId => _process.Id;

	public string GameDirectory { get; }

	public GameMemoryProfile Profile { get; }

	public LiveFrame? Frame { get; private set; }

	public bool ReadNavigation { get; set; }

	public NavigationFrame? Navigation { get; private set; }

	public bool ReadMapPlayers { get; set; }

	public MapPlayersFrame? MapPlayers { get; private set; }

	public bool ReadFullMap { get; set; }

	public FullMapFrame? FullMap { get; private set; }

	public string FullMapStatus { get; private set; } = "Mapa completo não solicitado";

	public string MapPlayersStatus { get; private set; } = "Lista não solicitada";

	public string NavigationStatus { get; private set; } = "Navegação não solicitada";

	public string RenderViewStatus { get; private set; } = "View não solicitada";

	public string Status { get; private set; } = "Connecting";

	public bool IsLiveData
	{
		get
		{
			LiveFrame frame = Frame;
			if ((object)frame != null)
			{
				return _clock.NowMs - frame.CapturedAtMs <= 500;
			}
			return false;
		}
	}

	public ReadOnlyGameStateProvider(int pid, string expectedName, IMonotonicClock clock, GameMemoryProfile? calibrationProfile = null)
	{
		_identity = new LocalCharacterIdentity(expectedName);
		_clock = clock;
		_process = Process.GetProcessById(pid);
		try
		{
			if (_process.ProcessName != "NinOnline")
			{
				throw new InvalidOperationException("Expected NinOnline.exe");
			}
			string path = _process.MainModule?.FileName ?? throw new InvalidOperationException("Process path unavailable");
			GameDirectory = Path.GetDirectoryName(path);
			string text;
			using (FileStream source = File.OpenRead(Path.Combine(Path.GetDirectoryName(path), "NinOnline.dll")))
			{
				text = Convert.ToHexString(SHA256.HashData(source));
			}
			string text2;
			using (FileStream source2 = File.OpenRead(Path.Combine(GameDirectory, "Common.dll")))
			{
				text2 = Convert.ToHexString(SHA256.HashData(source2));
			}
			if ((object)calibrationProfile != null)
			{
				if (calibrationProfile.ActuationValidated || !calibrationProfile.ExternalProfile || !calibrationProfile.Sha256.Equals(text, StringComparison.OrdinalIgnoreCase) || !calibrationProfile.CommonSha256.Equals(text2, StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidOperationException("Read-only calibration profile must match both DLL hashes and disable all input");
				}
				Profile = calibrationProfile;
			}
			else
			{
				try
				{
					Profile = GameMemoryProfile.Select(text, text2);
				}
				catch (InvalidOperationException)
				{
					try
					{
						Profile = SignedProfileCatalog.Fetch(text, text2) ?? throw new InvalidOperationException("No signed profile for this exact game build");
					}
					catch (Exception innerException)
					{
						throw new InvalidOperationException($"Unknown game build ({text.Substring(0, 12)}/{text2.Substring(0, 12)}). " + "No verified compatibility profile available; automation remains OFF.", innerException);
					}
				}
			}
			_fullMapCache = new FullMapCache(Profile);
			_target = DataTarget.AttachToProcess(pid, suspend: false);
			try
			{
				_runtime = _target.ClrVersions.Single().CreateRuntime();
			}
			catch
			{
				_target.Dispose();
				throw;
			}
		}
		catch
		{
			_process.Dispose();
			throw;
		}
	}

	public GameSnapshot? GetSnapshot()
	{
		Frame = null;
		Navigation = null;
		MapPlayers = null;
		long nowMs = _clock.NowMs;
		try
		{
			if (_process.HasExited)
			{
				throw new InvalidOperationException("Game exited; reconnect manually.");
			}
			_runtime.FlushCachedData();
			ClrType clrType = ((_globalsMethodTable == 0L) ? null : _runtime.GetTypeByMethodTable(_globalsMethodTable));
			ClrType clrType2 = ((_rootsMethodTable == 0L) ? null : _runtime.GetTypeByMethodTable(_rootsMethodTable));
			if ((object)clrType == null || (object)clrType2 == null)
			{
				ClrModule clrModule = _runtime.EnumerateModules().Single((ClrModule m) => Path.GetFileName(m.Name) == "NinOnline.dll");
				clrType = clrModule.GetTypeByName(Profile.Globals) ?? throw new InvalidOperationException("Globals unavailable");
				clrType2 = clrModule.GetTypeByName(Profile.Roots) ?? throw new InvalidOperationException("Player roots unavailable");
				_globalsMethodTable = clrType.MethodTable;
				_rootsMethodTable = clrType2.MethodTable;
			}
			if (clrType.Name != Profile.Globals || clrType2.Name != Profile.Roots)
			{
				throw new InvalidOperationException("Type identity changed; reconnect required");
			}
			ClrAppDomain clrAppDomain = _runtime.AppDomains.Single();
			int num = StaticInt(clrType, Profile.LocalIndex, clrAppDomain);
			(int, int, int, ulong) tuple = ReadSelectedTarget(clrType, clrAppDomain);
			int item = tuple.Item1;
			ClrStaticField clrStaticField = clrType2.GetStaticFieldByName(Profile.Players) ?? throw new InvalidOperationException("Player array unavailable");
			if (!clrStaticField.IsInitialized(clrAppDomain) || clrStaticField.GetAddress(clrAppDomain) == 0L)
			{
				throw new InvalidOperationException("Game state not initialized");
			}
			ClrObject clrObject = clrStaticField.ReadObject(clrAppDomain);
			if (clrObject.IsNull || !clrObject.IsArray)
			{
				throw new InvalidOperationException("Not in a map");
			}
			ClrArray clrArray = clrObject.AsArray();
			if (num <= 0 || num >= clrArray.Length || clrArray.Length > 100000)
			{
				throw new InvalidOperationException("Invalid local index");
			}
			ClrObject objectValue = clrArray.GetObjectValue(num);
			if (objectValue.IsNull)
			{
				throw new InvalidOperationException("Local player missing");
			}
			string text = objectValue.ReadStringField(Profile.Name, 64);
			if (_identity.IsInvalidated || !LocalCharacterIdentity.IsValidName(text))
			{
				throw new InvalidOperationException("Local character changed/unavailable; confirm identity and reconnect.");
			}
			string name = _identity.Name;
			if (name != null && text != name)
			{
				if (StaticInt(clrType, Profile.LocalIndex, clrAppDomain) == num && clrStaticField.ReadObject(clrAppDomain).Address == clrObject.Address)
				{
					_identity.ConfirmStableObservation(text, objectValue.ReadStringField(Profile.Name, 64));
				}
				throw new InvalidOperationException("Local character changed; reconnect required.");
			}
			int num2 = Field<int>(objectValue, Profile.Map);
			if (num2 <= 0)
			{
				throw new InvalidOperationException("Player outside a valid map");
			}
			double num3 = Coordinate(objectValue, Profile.PixelX);
			double num4 = Coordinate(objectValue, Profile.PixelY);
			int[] array = IntArray(objectValue, Profile.Vitals, 4);
			int[] array2 = IntArray(objectValue, Profile.MaxVitals, 4);
			CastObservation castObservation = null;
			CastMemoryFields castTiming = Profile.CastTiming;
			if ((object)castTiming != null)
			{
				try
				{
					int num5 = StaticInt(clrType, castTiming.GlobalSpell, clrAppDomain);
					int num6 = StaticInt(clrType, castTiming.GlobalStart, clrAppDomain);
					int num7 = Field<int>(objectValue, castTiming.PlayerSpell);
					int num8 = Field<int>(objectValue, castTiming.PlayerEnd);
					ClrStaticField staticFieldByName = clrType.GetStaticFieldByName(castTiming.GameClock);
					if (staticFieldByName?.Type?.Name == "System.Int64" && staticFieldByName.IsInitialized(clrAppDomain) && _target.DataReader.Read(staticFieldByName.GetAddress(clrAppDomain), out long value) && value > 0 && num5 == StaticInt(clrType, castTiming.GlobalSpell, clrAppDomain) && num6 == StaticInt(clrType, castTiming.GlobalStart, clrAppDomain) && num7 == Field<int>(objectValue, castTiming.PlayerSpell) && num8 == Field<int>(objectValue, castTiming.PlayerEnd))
					{
						CastObservation castObservation2 = CastObservation.Decode(num5, num6, num7, num8, value);
						if ((object)castObservation2 != null)
						{
							castObservation = castObservation2 with
							{
								ObservedAtMs = _clock.NowMs
							};
						}
					}
				}
				catch
				{
				}
			}
			StaminaObservation stamina = null;
			ClrStaticField staticFieldByName2 = clrType.GetStaticFieldByName(Profile.Running);
			if (array[3] >= 0 && array2[3] > 0 && array[3] <= array2[3] && staticFieldByName2?.Type?.Name == "System.Boolean" && staticFieldByName2.IsInitialized(clrAppDomain) && _target.DataReader.Read(staticFieldByName2.GetAddress(clrAppDomain), out byte value2) && value2 <= 1)
			{
				stamina = new StaminaObservation(array[3], array2[3], value2 == 1);
			}
			if (array[1] < 0 || array2[1] <= 0 || array[1] > array2[1] || array[2] < 0 || array[2] > array2[2])
			{
				throw new InvalidOperationException("Inconsistent vitals");
			}
			List<string> list = new List<string>();
			List<ObservedBuff> list2 = new List<ObservedBuff>();
			ClrObject clrObject2 = objectValue.ReadObjectField(Profile.Buffs);
			if (clrObject2.IsNull || !clrObject2.IsArray)
			{
				throw new InvalidOperationException("Buff state unavailable");
			}
			ClrArray clrArray2 = clrObject2.AsArray();
			if (clrArray2.Length > 256)
			{
				throw new InvalidOperationException("Invalid buff array");
			}
			for (int num9 = 0; num9 < clrArray2.Length; num9++)
			{
				ClrObject objectValue2 = clrArray2.GetObjectValue(num9);
				if (!objectValue2.IsNull && Field<int>(objectValue2, Profile.BuffActive) == 1)
				{
					string text2 = objectValue2.ReadStringField(Profile.BuffName, 128);
					if (!string.IsNullOrWhiteSpace(text2))
					{
						list.Add(text2);
						int value3 = Field<int>(objectValue2, Profile.BuffDuration);
						list2.Add(new ObservedBuff(text2, value3));
					}
				}
			}
			TargetInfo targetInfo = null;
			string text3 = null;
			if (item > 0 && item < clrArray.Length && item != num)
			{
				ClrObject objectValue3 = clrArray.GetObjectValue(item);
				if (!objectValue3.IsNull && Field<int>(objectValue3, Profile.Map) == num2)
				{
					text3 = objectValue3.ReadStringField(Profile.Name, 64);
					if (!string.IsNullOrWhiteSpace(text3))
					{
						int num10 = IntArray(objectValue3, Profile.Vitals, 4)[1];
						targetInfo = new TargetInfo
						{
							Id = $"{item}:{text3}",
							PositionX = Coordinate(objectValue3, Profile.PixelX),
							PositionY = Coordinate(objectValue3, Profile.PixelY),
							IsAlive = (num10 > 0),
							IsTargeted = true
						};
					}
				}
			}
			if (StaticInt(clrType, Profile.LocalIndex, clrAppDomain) == num)
			{
				(int, int, int, ulong) tuple2 = ReadSelectedTarget(clrType, clrAppDomain);
				(int, int, int, ulong) tuple3 = tuple;
				if (tuple2.Item1 == tuple3.Item1 && tuple2.Item2 == tuple3.Item2 && tuple2.Item3 == tuple3.Item3 && tuple2.Item4 == tuple3.Item4 && Field<int>(objectValue, Profile.Map) == num2 && clrStaticField.ReadObject(clrAppDomain).Address == clrObject.Address && _identity.ConfirmStableObservation(text, objectValue.ReadStringField(Profile.Name, 64)))
				{
					GameSnapshot gameSnapshot = new GameSnapshot
					{
						Player = new PlayerState
						{
							Id = $"{num}:{text}",
							PositionX = num3,
							PositionY = num4,
							Facing = DecodeFacing(Field<byte>(objectValue, Profile.Facing)),
							IsAlive = (array[1] > 0),
							Resources = array[2]
						},
						Target = targetInfo,
						KnownEntities = (((object)targetInfo == null) ? ((IReadOnlyList<TargetInfo>)Array.Empty<TargetInfo>()) : ((IReadOnlyList<TargetInfo>)new TargetInfo[1] { targetInfo })),
						ActiveBuffs = list
					};
					GameSnapshot gameSnapshot2 = gameSnapshot;
					if (ReadNavigation)
					{
						try
						{
							Navigation = ReadTerrain(clrType2, clrAppDomain, num, num2, num3, num4, nowMs);
							NavigationStatus = $"Terreno observado + {Navigation.VisibleHazards} perigos; {Navigation.IgnoredOwnShots} disparos próprios reconhecidos ignorados";
						}
						catch (Exception ex)
						{
							Navigation = null;
							NavigationStatus = "Navegação bloqueada: " + ex.Message;
						}
					}
					else
					{
						NavigationStatus = "Navegação não solicitada (Follow OFF/tecla solta)";
					}
					if (ReadMapPlayers)
					{
						try
						{
							List<MapPlayer> list3 = new List<MapPlayer>();
							int num11 = 0;
							for (int num12 = 1; num12 < clrArray.Length; num12++)
							{
								try
								{
									ClrObject objectValue4 = clrArray.GetObjectValue(num12);
									if (objectValue4.IsNull || Field<int>(objectValue4, Profile.Map) != num2)
									{
										continue;
									}
									string text4 = objectValue4.ReadStringField(Profile.Name, 64);
									if (string.IsNullOrWhiteSpace(text4))
									{
										continue;
									}
									double? x = null;
									double? y = null;
									try
									{
										x = Coordinate(objectValue4, Profile.PixelX);
										y = Coordinate(objectValue4, Profile.PixelY);
									}
									catch
									{
									}
									PlayerVillage village = PlayerVillage.Unknown;
									string village2 = Profile.Village;
									if (village2 != null)
									{
										try
										{
											byte b = Field<byte>(objectValue4, village2);
											if (b == Field<byte>(objectValue4, village2))
											{
												village = PlayerVillages.FromCode(b);
											}
										}
										catch
										{
										}
									}
									if (clrArray.GetObjectValue(num12).Address != objectValue4.Address || Field<int>(objectValue4, Profile.Map) != num2 || objectValue4.ReadStringField(Profile.Name, 64) != text4)
									{
										num11++;
										continue;
									}
									list3.Add(new MapPlayer(num12, text4, num2, x, y, num12 == num, num12 == item)
									{
										Village = village,
									IsAlive = SafeAlive(objectValue4)
									});
								}
								catch
								{
									num11++;
								}
							}
							if (clrStaticField.ReadObject(clrAppDomain).Address != clrObject.Address || StaticInt(clrType, Profile.LocalIndex, clrAppDomain) != num || Field<int>(objectValue, Profile.Map) != num2)
							{
								throw new InvalidOperationException("Mapa/lista mudou durante leitura");
							}
							int? gridWidth = null;
							int? gridHeight = null;
							try
							{
								ClrObject? clrObject3 = clrType2.GetStaticFieldByName(Profile.MapRoot)?.ReadObject(clrAppDomain);
								if (clrObject3.HasValue)
								{
									ClrObject valueOrDefault = clrObject3.GetValueOrDefault();
									if (!valueOrDefault.IsNull && ReadMapIdentity(clrType2, clrAppDomain, valueOrDefault) == num2)
									{
										ClrObject clrObject4 = valueOrDefault.ReadObjectField(Profile.Tiles);
										if (clrObject4.IsArray && clrObject4.Type?.Name == "Common.TileRec[,]" && clrObject4.AsArray().Rank == 2)
										{
											int length = clrObject4.AsArray().GetLength(0);
											if (length > 0 && length <= 512)
											{
												length = clrObject4.AsArray().GetLength(1);
												if (length > 0 && length <= 512)
												{
													gridWidth = clrObject4.AsArray().GetLength(0);
													gridHeight = clrObject4.AsArray().GetLength(1);
												}
											}
										}
									}
								}
							}
							catch
							{
							}
							MapPlayers = new MapPlayersFrame(num2, $"{num}:{text}", nowMs, MapPlayersFrame.Filter(list3, num2), num11)
							{
								GridWidth = gridWidth,
								GridHeight = gridHeight,
								RenderView = ReadRenderView(objectValue, num2, num3, num4, gridWidth, gridHeight)
							};
							MapPlayersStatus = $"{MapPlayers.Players.Count} registros locais no mapa {num2}";
						}
						catch (Exception ex2)
						{
							MapPlayers = null;
							MapPlayersStatus = "Lista indisponível: " + ex2.Message;
						}
					}
					if (ReadFullMap)
					{
						try
						{
							ReadFullTerrainChunk(clrType2, clrAppDomain, num2, nowMs);
						}
						catch (Exception ex)
						{
							_fullMapCells = null;
							_fullMapCursor = 0;
							FullMapStatus = "Mapa completo indisponível: " + ex.Message;
						}
					}
					if (StaticInt(clrType, Profile.LocalIndex, clrAppDomain) != num || Field<int>(objectValue, Profile.Map) != num2 || objectValue.ReadStringField(Profile.Name, 64) != text || clrStaticField.ReadObject(clrAppDomain).Address != clrObject.Address)
					{
						throw new InvalidOperationException("Local identity/map changed during read");
					}
					long num13 = _clock.NowMs - nowMs;
					if (num13 > 500)
					{
						throw new InvalidOperationException($"Read too slow ({num13} ms); no inputs allowed");
					}
					Frame = new LiveFrame(gameSnapshot2, nowMs, num2, text, text3, list2.Any((ObservedBuff observedBuff) => SubstitutionBuffs.IsKnown(observedBuff.Name)), (from observedBuff in list2
						where SubstitutionBuffs.IsKnown(observedBuff.Name)
						select observedBuff.RemainingSeconds).DefaultIfEmpty().Min(), num13)
					{
						ObservedActiveBuffs = list2,
						Stamina = stamina,
						Cast = castObservation,
						UnmappedSignals = (((object)castObservation != null) ? new string[5] { "ActionBlock", "SubstitutionCooldownRemaining", "Threat", "AttackerIdentity", "QSCostCooldownAndEffect" } : new string[6] { "Casting", "ActionBlock", "SubstitutionCooldownRemaining", "Threat", "AttackerIdentity", "QSCostCooldownAndEffect" })
					};
					if (Profile.ExternalProfile)
					{
						_externalSampleCount = ((_externalSampleMap != num2 || !(_externalSampleName == text)) ? 1 : Math.Min(_externalSampleCount + 1, 3));
						_externalSampleMap = num2;
						_externalSampleName = text;
						if (_externalSampleCount < 3)
						{
							Frame = null;
							Status = $"Perfil externo: validando leituras estáveis ({_externalSampleCount}/3); sem teclas";
							return null;
						}
					}
					Status = $"OBSERVED [{Profile.Id}] {text} → {text3 ?? "no target"} · {num13} ms";
					return gameSnapshot2;
				}
			}
			throw new InvalidOperationException("State changed during read; retry next tick");
		}
		catch (Exception ex3)
		{
			_externalSampleCount = 0;
			MapPlayers = null;
			Status = "UNAVAILABLE: " + ex3.Message;
			return null;
		}
	}

	private int StaticInt(ClrType type, string name, ClrAppDomain domain)
	{
		ClrStaticField clrStaticField = type.GetStaticFieldByName(name) ?? throw new InvalidOperationException("Missing " + name);
		if (clrStaticField.Type?.Name != "System.Int32" || !clrStaticField.IsInitialized(domain))
		{
			throw new InvalidOperationException("Invalid " + name);
		}
		ulong address = clrStaticField.GetAddress(domain);
		if (address == 0L || !_target.DataReader.Read(address, out int value))
		{
			throw new InvalidOperationException("Cannot read " + name);
		}
		return value;
	}

	private (int PlayerIndex, int RawIndex, int Kind, ulong ObjectAddress) ReadSelectedTarget(ClrType globals, ClrAppDomain domain)
	{
		SelectedTargetMemoryFields selectedTarget = Profile.SelectedTarget;
		if ((object)selectedTarget == null)
		{
			int num = StaticInt(globals, Profile.SelectedIndex ?? throw new InvalidOperationException("Legacy selected index unavailable"), domain);
			return (PlayerIndex: num, RawIndex: num, Kind: 0, ObjectAddress: 0uL);
		}
		ClrStaticField clrStaticField = globals.GetStaticFieldByName(selectedTarget.GlobalObject) ?? throw new InvalidOperationException("Target object unavailable");
		if (!clrStaticField.IsInitialized(domain) || clrStaticField.GetAddress(domain) == 0L)
		{
			throw new InvalidOperationException("Target state uninitialized");
		}
		if ((object)clrStaticField.Type == null || clrStaticField.Type.IsValueType)
		{
			throw new InvalidOperationException("Target schema changed");
		}
		ClrObject obj = clrStaticField.ReadObject(domain);
		if (obj.IsNull)
		{
			return (PlayerIndex: 0, RawIndex: 0, Kind: 0, ObjectAddress: 0uL);
		}
		if (obj.Type?.Name != clrStaticField.Type.Name)
		{
			throw new InvalidOperationException("Target identity changed during read");
		}
		int num2 = Field<int>(obj, selectedTarget.Kind);
		int num3 = Field<int>(obj, selectedTarget.Index);
		return (PlayerIndex: selectedTarget.PlayerIndex(num2, num3), RawIndex: num3, Kind: num2, ObjectAddress: obj.Address);
	}

	private int ReadMapIdentity(ClrType roots, ClrAppDomain domain, ClrObject mapObject)
	{
		if (!Profile.MapIdentityInRoots)
		{
			return Field<int>(mapObject, Profile.MapIdentity);
		}
		return StaticInt(roots, Profile.MapIdentity, domain);
	}

	private RenderViewObservation? ReadRenderView(ClrObject local, int map, double x, double y, int? gridWidth, int? gridHeight)
	{
		(string, string) tuple = Profile.Sha256 switch
		{
			"3B1AAF914AE0EA2268CEE6C6E95960C2A04CDAC383E0C540D98752B12EF96842" => ("wMWLcvW2ArKRj9nhY3bg.TyDWRPW2l05BDoekEyUt", "xgcWweIHSdJ"),
			"C3F7DF26A0C00D72291DEFC67F219BD54909AE9062160329DE4AD6956110CB72" => ("bfh79MUAUg04A0AkOIDe.XueWakUAoTIroqRVa3KM", "AwCUSQuJJUb"),
			"5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634" => ("Nvx4GuISaVJTgraaxmuY.ddoPbRISG5UMbcXPLWJC", "rU0I2D0I0yb"),
			"349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525" => ("qM9Z8LrxmrrBo1XcjmiL.ckkW2irxY8qGNKVIijDw", "NaHWqrTa5P1"),
			_ => ("", ""),
		};
		if (tuple.Item1.Length == 0)
		{
			RenderViewStatus = "Hash sem view calibrada";
			return null;
		}
		try
		{
			ClrStaticField clrStaticField = _runtime.EnumerateModules().Single((ClrModule m) => Path.GetFileName(m.Name) == "NinOnline.dll").GetTypeByName(tuple.Item1)?.GetStaticFieldByName(tuple.Item2);
			ClrAppDomain appDomain = _runtime.AppDomains.Single();
			if (clrStaticField?.Type?.Name != "NinEngine.Graphics.View" || !clrStaticField.IsInitialized(appDomain))
			{
				RenderViewStatus = "View global não inicializada";
				return null;
			}
			ClrObject clrObject = clrStaticField.ReadObject(appDomain);
			if (clrObject.IsNull || clrObject.Type?.Name != "NinEngine.Graphics.View")
			{
				RenderViewStatus = "View ausente";
				return null;
			}
			ClrValueType clrValueType = clrObject.ReadValueTypeField("myCenter");
			ClrValueType clrValueType2 = clrObject.ReadValueTypeField("mySize");
			ClrValueType clrValueType3 = clrObject.ReadValueTypeField("myViewport");
			float num = clrValueType.ReadField<float>("X");
			float num2 = clrValueType.ReadField<float>("Y");
			float num3 = clrValueType2.ReadField<float>("X");
			float num4 = clrValueType2.ReadField<float>("Y");
			bool flag = !float.IsFinite(num) || !float.IsFinite(num2);
			if (!flag)
			{
				bool flag2 = ((num3 < 320f || num3 > 8000f) ? true : false);
				flag = flag2;
			}
			bool flag3 = flag;
			if (!flag3)
			{
				bool flag2 = ((num4 < 240f || num4 > 8000f) ? true : false);
				flag3 = flag2;
			}
			if (flag3 || clrObject.ReadField<float>("myRotation") != 0f || clrValueType3.ReadField<float>("Left") != 0f || clrValueType3.ReadField<float>("Top") != 0f || clrValueType3.ReadField<float>("Width") != 1f || clrValueType3.ReadField<float>("Height") != 1f || (Profile.Sha256 != "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634" && (Math.Abs(x * 32.0 - (double)num) > (double)(num3 / 2f + 32f) || Math.Abs(y * 32.0 - (double)num2) > (double)(num4 / 2f + 32f))) || local.ReadStringField(Profile.Name, 64) != _identity.Name || Field<int>(local, Profile.Map) != map || clrStaticField.ReadObject(appDomain).Address != clrObject.Address)
			{
				RenderViewStatus = "View inconsistente com mapa/personagem";
				return null;
			}
			if (Profile.Sha256 == "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634")
			{
				RenderViewObservation renderViewObservation = ((map == 190) ? OverlayProjection.CalibratedMap190(x, y, gridWidth.GetValueOrDefault(), gridHeight.GetValueOrDefault(), num3, num4) : null);
				RenderViewStatus = (((object)renderViewObservation == null) ? "Câmera visual não calibrada neste mapa" : "Câmera visual calibrada no mapa 190");
				return renderViewObservation;
			}
			if (Profile.Sha256 == "349E3190B1D5CB27E10E8CB88AD2402857567989BB069115B80B264060DAB525")
			{
				RenderViewStatus = map == 190 ? "View Steam candidata; alinhamento visual pendente" : "View Steam não calibrada neste mapa";
				return map == 190 ? new RenderViewObservation(num, num2, num3, num4) : null;
			}
			RenderViewStatus = "View observada";
			return new RenderViewObservation(num, num2, num3, num4);
		}
		catch (Exception ex)
		{
			RenderViewStatus = "View indisponível: " + ex.Message;
			return null;
		}
	}

	private NavigationFrame ReadTerrain(ClrType roots, ClrAppDomain domain, int localIndex, int map, double x, double y, long captured)
	{
		ClrStaticField clrStaticField = roots.GetStaticFieldByName(Profile.MapRoot) ?? throw new InvalidOperationException("Map root unavailable");
		ClrObject mapObject = clrStaticField.ReadObject(domain);
		if (mapObject.IsNull || ReadMapIdentity(roots, domain, mapObject) != map)
		{
			throw new InvalidOperationException("Map identity mismatch");
		}
		ClrObject clrObject = mapObject.ReadObjectField(Profile.Tiles);
		if (!clrObject.IsArray || clrObject.Type?.Name != "Common.TileRec[,]")
		{
			throw new InvalidOperationException("Tile schema unavailable");
		}
		ClrArray clrArray = clrObject.AsArray();
		bool flag = clrArray.Rank != 2 || clrArray.GetLowerBound(0) != 0 || clrArray.GetLowerBound(1) != 0;
		int length;
		if (!flag)
		{
			length = clrArray.GetLength(0);
			bool flag2 = ((length < 1 || length > 512) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		if (!flag3)
		{
			length = clrArray.GetLength(1);
			bool flag2 = ((length < 1 || length > 512) ? true : false);
			flag3 = flag2;
		}
		if (flag3)
		{
			throw new InvalidOperationException("Tile dimensions invalid");
		}
		int num = (int)Math.Floor(x);
		int num2 = (int)Math.Floor(y);
		if (num < 0 || num2 < 0 || num >= clrArray.GetLength(0) || num2 >= clrArray.GetLength(1))
		{
			throw new InvalidOperationException("Player outside grid");
		}
		int num3 = Math.Max(0, num - 12);
		int num4 = Math.Max(0, num2 - 12);
		int num5 = Math.Min(clrArray.GetLength(0), num + 13) - num3;
		int num6 = Math.Min(clrArray.GetLength(1), num2 + 13) - num4;
		NavigationCell[] array = new NavigationCell[num5 * num6];
		ClrObject objectValue = clrArray.GetObjectValue(num, num2);
		if (objectValue.IsNull)
		{
			throw new InvalidOperationException("Local tile unknown");
		}
		int num7 = Field<int>(objectValue, "Height");
		for (int i = num3; i < num3 + num5; i++)
		{
			for (int j = num4; j < num4 + num6; j++)
			{
				ClrObject objectValue2 = clrArray.GetObjectValue(i, j);
				if (objectValue2.IsNull)
				{
					continue;
				}
				byte b = Field<byte>(objectValue2, "Type");
				bool num8 = Field<byte>(objectValue2, "DirBlock") != 0 || Field<int>(objectValue2, "Height") != num7;
				NavigationCell[] array2 = array;
				length = (j - num4) * num5 + i - num3;
				NavigationCell navigationCell;
				if (num8)
				{
					navigationCell = NavigationCell.Blocked;
				}
				else
				{
					NavigationCell navigationCell2;
					switch (b)
					{
					case 0:
					case 4:
					case 18:
					case 19:
					case 21:
					case 22:
						navigationCell2 = NavigationCell.Open;
						break;
					case 13:
					case 14:
					case 17:
					case 20:
					case 25:
						navigationCell2 = NavigationCell.Hazard;
						break;
					default:
						navigationCell2 = NavigationCell.Blocked;
						break;
					}
					navigationCell = navigationCell2;
				}
				array2[length] = navigationCell;
			}
		}
		ClrStaticField clrStaticField2 = roots.GetStaticFieldByName(Profile.Projectiles) ?? throw new InvalidOperationException("Projectile root unavailable");
		ClrObject clrObject2 = clrStaticField2.ReadObject(domain);
		if (!clrObject2.IsArray || clrObject2.Type?.Name != "client.ProjectileRec[]")
		{
			throw new InvalidOperationException("Projectile schema unavailable");
		}
		ClrArray clrArray2 = clrObject2.AsArray();
		length = clrArray2.Length;
		if ((length < 1 || length > 8192) ? true : false)
		{
			throw new InvalidOperationException("Projectile count invalid");
		}
		int num9 = 0;
		int num10 = 0;
		Dictionary<int, ProjectileSourceDefinition> dictionary = new Dictionary<int, ProjectileSourceDefinition>();
		ClrObject? clrObject3 = roots.StaticFields.SingleOrDefault((ClrStaticField f) => f.Type?.Name == "client.SpellRec[]")?.ReadObject(domain);
		for (int num11 = 0; num11 < clrArray2.Length; num11++)
		{
			ClrObject objectValue3 = clrArray2.GetObjectValue(num11);
			if (objectValue3.IsNull)
			{
				continue;
			}
			int num12 = Field<int>(objectValue3, "Owner");
			if (num12 == 0)
			{
				continue;
			}
			double num13 = Field<double>(objectValue3, "X");
			double num14 = Field<double>(objectValue3, "Y");
			if (!double.IsFinite(num13) || !double.IsFinite(num14) || Math.Abs(num13) > 100000.0 || Math.Abs(num14) > 100000.0)
			{
				throw new InvalidOperationException("Projectile position invalid");
			}
			int num15 = Math.Max(Field<int>(objectValue3, "HitboxWidth"), Field<int>(objectValue3, "HitboxLength"));
			if ((num15 < 0 || num15 > 2048) ? true : false)
			{
				throw new InvalidOperationException("Projectile hitbox unknown");
			}
			if (num12 == localIndex && Field<int>(objectValue3, "OwnerType") == 1)
			{
				int num16 = Field<int>(objectValue3, "Spell");
				if (!dictionary.TryGetValue(num16, out var value))
				{
					value = null;
					try
					{
						if (clrObject3.HasValue)
						{
							ClrObject valueOrDefault = clrObject3.GetValueOrDefault();
							if (valueOrDefault.IsArray)
							{
								ClrArray clrArray3 = valueOrDefault.AsArray();
								if (num16 > 0 && num16 < clrArray3.Length)
								{
									ClrObject objectValue4 = clrArray3.GetObjectValue(num16);
									if (!objectValue4.IsNull && objectValue4.Type?.Name == "client.SpellRec")
									{
										ClrObject obj = objectValue4.ReadObjectField("Projectile");
										string text = objectValue4.ReadStringField("Name", 128);
										if (!obj.IsNull && text != null)
										{
											value = new ProjectileSourceDefinition(text, Field<byte>(objectValue4, "Type"), Field<bool>(objectValue4, "WalkCast"), Field<int>(obj, "pic"));
										}
									}
								}
							}
						}
					}
					catch
					{
						value = null;
					}
					dictionary[num16] = value;
				}
				if (ProjectileHazardPolicy.IgnoreOwnAttack(localIndex, num12, 1, Field<int>(objectValue3, "Range"), Field<int>(objectValue3, "Speed"), Field<int>(objectValue3, "pic"), value))
				{
					num10++;
					continue;
				}
			}
			int num17 = Math.Clamp((int)Math.Ceiling((double)num15 / 64.0), 1, 16);
			num9++;
			for (int num18 = (int)Math.Floor(num13) - num17; num18 <= (int)Math.Floor(num13) + num17; num18++)
			{
				for (int num19 = (int)Math.Floor(num14) - num17; num19 <= (int)Math.Floor(num14) + num17; num19++)
				{
					if (num18 >= num3 && num19 >= num4 && num18 < num3 + num5 && num19 < num4 + num6)
					{
						array[(num19 - num4) * num5 + num18 - num3] = NavigationCell.Hazard;
					}
				}
			}
		}
		if (clrStaticField.ReadObject(domain).Address != mapObject.Address || mapObject.ReadObjectField(Profile.Tiles).Address != clrObject.Address || ReadMapIdentity(roots, domain, mapObject) != map || clrStaticField2.ReadObject(domain).Address != clrObject2.Address)
		{
			throw new InvalidOperationException("Terrain changed during read");
		}
		return new NavigationFrame(map, captured, num3, num4, num5, num6, array, DynamicHazardsKnown: true, num9)
		{
			IgnoredOwnShots = num10
		};
	}

	private void ReadFullTerrainChunk(ClrType roots, ClrAppDomain domain, int map, long captured)
	{
		if (FullMap?.Map == map)
		{
			FullMapStatus = $"Mapa {map} completo · {FullMap.Width} × {FullMap.Height} tiles";
			return;
		}
		ClrStaticField mapField = roots.GetStaticFieldByName(Profile.MapRoot) ?? throw new InvalidOperationException("Map root unavailable");
		ClrObject mapObject = mapField.ReadObject(domain);
		if (mapObject.IsNull || ReadMapIdentity(roots, domain, mapObject) != map)
			throw new InvalidOperationException("Map identity mismatch");
		ClrObject tileObject = mapObject.ReadObjectField(Profile.Tiles);
		if (!tileObject.IsArray || tileObject.Type?.Name != "Common.TileRec[,]")
			throw new InvalidOperationException("Tile schema unavailable");
		ClrArray tiles = tileObject.AsArray();
		if (tiles.Rank != 2 || tiles.GetLowerBound(0) != 0 || tiles.GetLowerBound(1) != 0)
			throw new InvalidOperationException("Tile dimensions invalid");
		int width = tiles.GetLength(0);
		int height = tiles.GetLength(1);
		if (width < 1 || height < 1 || width > 512 || height > 512)
			throw new InvalidOperationException("Tile dimensions invalid");
		ulong signature = ReadTerrainSignature(tiles, map, width, height);
		if (_fullMapCells == null || _fullMapBuildingMap != map || _fullMapWidth != width || _fullMapHeight != height || _fullMapSignature != signature)
		{
			_fullMapBuildingMap = map;
			_fullMapWidth = width;
			_fullMapHeight = height;
			_fullMapSignature = signature;
			_fullMapCursor = 0;
			if (_fullMapCache != null && _fullMapCache.TryLoad(map, width, height, signature, captured, out FullMapFrame? cached) && cached != null)
			{
				FullMap = cached;
				_fullMapCells = null;
				FullMapStatus = $"Mapa {map} carregado do cache local · {width} × {height} tiles";
				return;
			}
			_fullMapCells = new MapTerrainCell[width * height];
			FullMap = null;
		}
		int end = Math.Min(_fullMapCells.Length, _fullMapCursor + 2048);
		for (int index = _fullMapCursor; index < end; index++)
		{
			int x = index % width;
			int y = index / width;
			ClrObject tile = tiles.GetObjectValue(x, y);
			if (tile.IsNull) continue;
			byte type = Field<byte>(tile, "Type");
			byte dirBlock = Field<byte>(tile, "DirBlock");
			int data1 = 0;
			try { data1 = Field<int>(tile, "Data1"); } catch { }
			_fullMapCells[index] = ClassifyMapTile(type, dirBlock, data1);
		}
		_fullMapCursor = end;
		if (mapField.ReadObject(domain).Address != mapObject.Address || mapObject.ReadObjectField(Profile.Tiles).Address != tileObject.Address || ReadMapIdentity(roots, domain, mapObject) != map)
			throw new InvalidOperationException("Terrain changed during read");
		if (_fullMapCursor >= _fullMapCells.Length)
		{
			FullMap = new FullMapFrame(map, captured, width, height, Array.AsReadOnly(_fullMapCells));
			_fullMapCells = null;
			_fullMapCursor = 0;
			try
			{
				_fullMapCache?.Save(FullMap, signature);
				FullMapStatus = $"Mapa {map} lido e salvo no cache local · {width} × {height} tiles";
			}
			catch (Exception ex)
			{
				FullMapStatus = $"Mapa {map} completo; cache local não salvo: {ex.Message}";
			}
		}
		else
		{
			int percent = (int)(100L * _fullMapCursor / _fullMapCells.Length);
			FullMapStatus = $"Lendo mapa {map} completo… {percent}%";
		}
	}

	private static ulong ReadTerrainSignature(ClrArray tiles, int map, int width, int height)
	{
		const ulong offset = 14695981039346656037UL;
		const ulong prime = 1099511628211UL;
		ulong hash = offset;
		void Add(int value)
		{
			unchecked
			{
				hash ^= (uint)value;
				hash *= prime;
			}
		}
		Add(map); Add(width); Add(height);
		int total = checked(width * height);
		int samples = Math.Min(97, total);
		for (int i = 0; i < samples; i++)
		{
			int index = samples == 1 ? 0 : (int)((long)i * (total - 1) / (samples - 1));
			int x = index % width;
			int y = index / width;
			ClrObject tile = tiles.GetObjectValue(x, y);
			if (tile.IsNull) { Add(-1); continue; }
			Add(Field<byte>(tile, "Type"));
			Add(Field<byte>(tile, "DirBlock"));
			try { Add(Field<int>(tile, "Data1")); } catch { Add(0); }
			try { Add(Field<int>(tile, "Height")); } catch { Add(0); }
		}
		return hash;
	}

	private static MapTerrainCell ClassifyMapTile(byte type, byte dirBlock, int data1)
	{
		if (dirBlock != 0 || type == 1) return MapTerrainCell.Wall;
		return type switch
		{
			0 or 4 or 18 or 19 or 21 or 22 => MapTerrainCell.Ground,
			2 when data1 > 0 => MapTerrainCell.Portal,
			7 or 26 => MapTerrainCell.Capture,
			13 or 14 or 17 or 20 or 23 or 25 or 27 => MapTerrainCell.Water,
			_ => MapTerrainCell.Special
		};
	}

	private static T Field<T>(ClrObject obj, string name) where T : unmanaged
	{
		if (obj.Type?.GetFieldByName(name)?.Type?.Name != typeof(T).FullName || !obj.TryReadField<T>(name, out var result))
		{
			throw new InvalidOperationException("Field schema/read mismatch: " + name);
		}
		return result;
	}

	private bool SafeAlive(ClrObject obj)
	{
		try
		{
			return IntArray(obj, Profile.Vitals, 4)[1] > 0;
		}
		catch
		{
			return false;
		}
	}

	private static int[] IntArray(ClrObject obj, string name, int expectedLength)
	{
		ClrObject clrObject = obj.ReadObjectField(name);
		if (clrObject.IsNull || !clrObject.IsArray || clrObject.Type?.Name != "System.Int32[]")
		{
			throw new InvalidOperationException("Missing " + name);
		}
		ClrArray clrArray = clrObject.AsArray();
		if (clrArray.Length != expectedLength)
		{
			throw new InvalidOperationException("Unexpected " + name + " length");
		}
		return clrArray.ReadValues<int>(0, expectedLength) ?? throw new InvalidOperationException("Cannot read " + name);
	}

	private static double Coordinate(ClrObject obj, string name)
	{
		float num = Field<float>(obj, name);
		if (!float.IsFinite(num) || num < -32f || num > 1000000f)
		{
			throw new InvalidOperationException("Invalid coordinates");
		}
		return (double)num / 32.0;
	}

	public static Direction DecodeFacing(byte value)
	{
		return value switch
		{
			0 => Direction.Up,
			1 => Direction.Down,
			2 => Direction.Left,
			3 => Direction.Right,
			_ => Direction.None,
		};
	}

	public void Dispose()
	{
		Frame = null;
		_runtime.Dispose();
		_target.Dispose();
		_process.Dispose();
	}
}
