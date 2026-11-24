using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Media;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Live;

namespace NinOnlineAutoAssist.App;

public sealed class Form1 : Form
{
	private readonly FirebaseSession _session;

	private int _permissionsMask;

	private Button _inspectionButton;

	private Label _account;

	private static readonly Color Bg = Color.FromArgb(13, 15, 19);

	private static readonly Color Panel = Color.FromArgb(22, 26, 33);

	private static readonly Color PanelAlt = Color.FromArgb(31, 36, 46);

	private static readonly Color Fg = Color.FromArgb(241, 243, 249);

	private static readonly Color Dim = Color.FromArgb(139, 148, 167);

	private static readonly Color Accent = Color.FromArgb(124, 92, 255);

	private static readonly Color Good = Color.FromArgb(34, 211, 166);

	private static readonly Color Warn = Color.FromArgb(255, 211, 107);

	private static readonly Color Bad = Color.FromArgb(255, 92, 108);

	private static readonly Color ReqBlue = Color.FromArgb(76, 154, 255);

	private static readonly Color ExecOrg = Color.FromArgb(245, 185, 74);

	private AutoAssistConfig _config;

	private LiveContext? _ctx;

	private readonly Timer _timer = new Timer
	{
		Interval = 15
	};

	private long _lastUiRefresh;

	private readonly SystemMonotonicClock _castObservationClock = new SystemMonotonicClock();

	private readonly ObservedCastIds _observedCastIds = new ObservedCastIds();

	private bool _emergencyKeyWasDown;

	private Label _lblMode;

	private Label _lblTarget;

	private Label _lblSubStatus;

	private Label _lblFlowStatus;

	private Label _lblCooldown;

	private Label _lblLastEvent;

	private Label _lblLastCounter;

	private Label _lblMetrics;

	private Label _lblPunishSafety;

	private Label _lblProvider;

	private Button _btnAutoTurning;

	private Button _btnSubPunish;

	private Button _btnCombat;

	private Button _btnTool;

	private Button _btnFollow;

	private Button _btnStamina;

	private Button _btnAutoTarget;

	private Label _lblCombat;

	private Label _lblFollow;

	private Label _lblStamina;

	private Label _lblAutoTarget;

	private TextBox _txtCharacter;

	private CheckBox _autoCharacter;

	private RichTextBox _rtbLog;

	private DataGridView _playersGrid;

	private Label _playersStatus;

	private MapPlayersFrame? _shownPlayers;

	private MapPlayersPlot _playersPlot;

	private Button _overlayButton;

	private PlayerPositionOverlay? _overlay;

	private readonly LastKnownPlayerTracker _lastKnownPlayers = new LastKnownPlayerTracker();

	private bool _overlayEnabled;

	private int? _selectedMapSlot;

	private bool _refreshingPlayers;

	private CheckBox _hideOwnVillage;

	private readonly Dictionary<PlayerVillage, CheckBox> _villageFilters = new Dictionary<PlayerVillage, CheckBox>();

	private int _shownVillageFilterMask = -1;

	private Button _btnCastTurn;

	private Label _lblCastTurn;

	private readonly List<(Label Label, Func<string> Summary)> _settingsSummaries = new();

	private string? _loadedCharacter;

	public bool ReturnToLogin { get; private set; }

	private AppFeature SubFeature => AppFeature.SubPunishLegacy;

	private int PermissionsMask => Enum.GetValues<AppFeature>().Aggregate(0, (int mask, AppFeature feature) => (!Allowed(feature)) ? mask : (mask | (1 << (int)feature)));

	private static string ConfigPath => AppPaths.Settings.ConfigPath;

	private string SettingsPath
	{
		get
		{
			if (_loadedCharacter != null)
			{
				return AppPaths.Settings.CharacterConfigPath(_loadedCharacter);
			}
			throw new InvalidOperationException("Aguarde a identidade local verificada antes de salvar.");
		}
	}

	private bool Allowed(AppFeature feature)
	{
		return _session.Access.Allows(feature);
	}

	public Form1(FirebaseSession session)
	{
		base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		_session = session;
		if (!session.Access.IsValid)
		{
			throw new AccessDeniedException("Login obrigatório.");
		}
		_permissionsMask = PermissionsMask;
		Text = "Nin AutoAssist — Central de automação";
		BackColor = Bg;
		ForeColor = Fg;
		Font = new Font("Segoe UI", 9f);
		MinimumSize = new Size(1180, 760);
		base.Size = new Size(1380, 900);
		base.KeyPreview = true;
		base.StartPosition = FormStartPosition.CenterScreen;
		BuildUi();
		LoadConfigAndContext();
		_timer.Tick += OnTick;
		_timer.Start();
		base.KeyDown += OnKeyDown;
		base.Shown += delegate
		{
			ConnectToGame();
		};
		base.FormClosed += delegate
		{
			StopOverlay();
			_overlay?.Dispose();
			_session.Revoke();
			_timer.Stop();
			_timer.Dispose();
			if (_ctx != null)
			{
				_ctx.MemoryLog.EntryLogged -= OnLogEntry;
				_ctx.Dispose();
			}
		};
	}

	private void BuildUi()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = Bg,
			Padding = new Padding(10)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 380f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		base.Controls.Add(tableLayoutPanel);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			BackColor = Bg
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20f));
		Label control = new Label
		{
			Text = "NIN ONLINE AUTOASSIST",
			Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
			ForeColor = Fg,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft
		};
		_lblMode = new Label
		{
			Text = "MODO: —",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
			BackColor = PanelAlt
		};
		Button button = new Button
		{
			Text = "PARADA DE EMERGÊNCIA (F12)",
			Dock = DockStyle.Fill,
			BackColor = Bad,
			ForeColor = Color.Black,
			FlatStyle = FlatStyle.Flat,
			Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
			Margin = new Padding(6, 4, 0, 4)
		};
		button.FlatAppearance.BorderSize = 0;
		button.Click += delegate
		{
			EmergencyStop();
		};
		tableLayoutPanel2.Controls.Add(control, 0, 0);
		tableLayoutPanel2.Controls.Add(_lblMode, 1, 0);
		tableLayoutPanel2.Controls.Add(button, 2, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 0);
		TableLayoutPanel tableLayoutPanel3 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 9,
			RowCount = 2,
			BackColor = Bg,
			Padding = new Padding(0, 4, 0, 2)
		};
		for (int num = 0; num < 9; num++)
		{
			tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 9f));
		}
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
		tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_btnAutoTurning = MakeModuleButton("AUTO TURNING", on: false);
		_btnAutoTurning.Click += delegate
		{
			ToggleAutoTurning();
		};
		_btnCastTurn = MakeModuleButton("AUTO CAST TURN", on: false);
		_btnCastTurn.Click += delegate
		{
			object tag = _btnCastTurn.Tag;
			bool castTurnEnabled = !(tag is bool) || !(bool)tag;
			_btnCastTurn.Tag = _ctx?.SetCastTurnEnabled(castTurnEnabled) ?? false;
			RefreshModuleButtons();
		};
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
		contextMenuStrip.Items.Add("Configurar modo e jutsus do Cast Turn…", null, delegate
		{
			ConfigureCastTurn();
		});
		_btnCastTurn.ContextMenuStrip = contextMenuStrip;
		_btnAutoTarget = MakeModuleButton("AUTO TARGET", on: false);
		_btnAutoTarget.Click += delegate
		{
			object tag = _btnAutoTarget.Tag;
			bool flag = !(tag is bool) || !(bool)tag;
			_btnAutoTarget.Tag = _ctx?.SetAutoTargetEnabled(flag) ?? false;
			RefreshModuleButtons();
		};
		_btnSubPunish = MakeModuleButton("SUBPUNISH TAI/ÁGUA", on: false);
		_btnSubPunish.Click += delegate
		{
			ToggleSubPunish();
		};
		_btnCombat = MakeModuleButton("AUTO AA", on: false);
		_btnCombat.Click += delegate
		{
			object tag = _btnCombat.Tag;
			bool flag = !(tag is bool) || !(bool)tag;
			_btnCombat.Tag = _ctx?.SetAttackEnabled(flag) ?? false;
			RefreshModuleButtons();
		};
		_btnTool = MakeModuleButton("AUTO TOOL", on: false);
		_btnTool.Click += delegate
		{
			object tag = _btnTool.Tag;
			bool flag = !(tag is bool) || !(bool)tag;
			_btnTool.Tag = _ctx?.SetToolEnabled(flag) ?? false;
			RefreshModuleButtons();
		};
		_btnFollow = MakeModuleButton("AUTO FOLLOW", on: false);
		_btnFollow.Click += delegate
		{
			object tag = _btnFollow.Tag;
			bool flag = !(tag is bool) || !(bool)tag;
			_btnFollow.Tag = flag;
			_ctx?.SetFollowEnabled(flag);
			RefreshModuleButtons();
		};
		_btnStamina = MakeModuleButton("AUTO STAMINA", on: false);
		_btnStamina.Click += delegate
		{
			object tag = _btnStamina.Tag;
			bool flag = !(tag is bool) || !(bool)tag;
			if (flag && _ctx?.StaminaBindingValid != true)
			{
				AppendLog(LogLevel.Warning, "Auto Stamina não pode ser ligado: " + (_ctx?.StaminaBindingProblem ?? "desconectado") + ". Ajuste a tecla de ativação do Follow nas configurações e reconecte.", EventOrigin.Requested);
				return;
			}
			_btnStamina.Tag = flag;
			_ctx?.SetStaminaEnabled(flag);
			RefreshModuleButtons();
		};
		Button button2 = (_inspectionButton = new Button
		{
			Text = "RECURSOS DO JOGO\nConsulta somente leitura",
			ForeColor = Fg,
			BackColor = PanelAlt,
			Dock = DockStyle.Fill,
			FlatStyle = FlatStyle.Flat,
			Margin = new Padding(5, 3, 5, 3),
			Font = new Font("Segoe UI Semibold", 9f),
			Cursor = Cursors.Hand
		});
		button2.FlatAppearance.BorderColor = Color.FromArgb(55, 66, 82);
		button2.Click += delegate
		{
			if (Allowed(AppFeature.ResourceInspection))
			{
				new ResourceInspectionForm(_session.Access).Show(this);
			}
		};
		_lblProvider = new Label
		{
			Text = "PROVEDOR: —",
			ForeColor = Dim,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Padding = new Padding(7, 0, 0, 0),
			Font = new Font("Segoe UI", 8.5f)
		};
		tableLayoutPanel3.Controls.Add(_btnAutoTurning, 0, 0);
		tableLayoutPanel3.Controls.Add(_btnCastTurn, 1, 0);
		tableLayoutPanel3.Controls.Add(_btnSubPunish, 2, 0);
		tableLayoutPanel3.Controls.Add(_btnCombat, 3, 0);
		tableLayoutPanel3.Controls.Add(_btnTool, 4, 0);
		tableLayoutPanel3.Controls.Add(_btnFollow, 5, 0);
		tableLayoutPanel3.Controls.Add(_btnStamina, 6, 0);
		tableLayoutPanel3.Controls.Add(_btnAutoTarget, 7, 0);
		tableLayoutPanel3.Controls.Add(button2, 8, 0);
		tableLayoutPanel3.Controls.Add(_lblProvider, 0, 1);
		tableLayoutPanel3.SetColumnSpan(_lblProvider, 9);
		tableLayoutPanel.Controls.Add(tableLayoutPanel3, 0, 1);
		TableLayoutPanel tableLayoutPanel4 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = Bg
		};
		tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 190f));
		tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(tableLayoutPanel4, 0, 2);
		TableLayoutPanel tableLayoutPanel5 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			BackColor = Panel,
			Padding = new Padding(18, 14, 18, 14),
			Margin = new Padding(0, 0, 6, 0)
		};
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34f));
		tableLayoutPanel5.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66f));
		_lblTarget = AddStatusRow(tableLayoutPanel5, 0, "STATUS DO ALVO");
		_lblSubStatus = AddStatusRow(tableLayoutPanel5, 1, "BUFF SUB — MEMÓRIA");
		_lblFlowStatus = AddStatusRow(tableLayoutPanel5, 2, "FLUXO SUBPUNISH");
		_lblCooldown = AddStatusRow(tableLayoutPanel5, 3, "AUTORIZAÇÃO DO COMBO");
		_lblLastEvent = AddStatusRow(tableLayoutPanel5, 4, "ÚLTIMO EVENTO");
		_lblLastCounter = AddStatusRow(tableLayoutPanel5, 5, "ÚLTIMA AÇÃO");
		_lblMetrics = AddStatusRow(tableLayoutPanel5, 6, "TURN — TECLA / SUB");
		_lblPunishSafety = AddStatusRow(tableLayoutPanel5, 7, "PUNISH — SEM TECLA");
		_lblCombat = AddStatusRow(tableLayoutPanel5, 8, "AA MELEE / FAN + TOOL");
		_lblFollow = AddStatusRow(tableLayoutPanel5, 9, "FOLLOW — TECLA SEGURADA");
		_lblStamina = AddStatusRow(tableLayoutPanel5, 10, "STAMINA — LIMITES CONFIGURÁVEIS");
		_lblCastTurn = AddStatusRow(tableLayoutPanel5, 11, "AUTO CAST TURN — FINAL DO CAST");
		_lblAutoTarget = AddStatusRow(tableLayoutPanel5, 12, "AUTO TARGET — ALVO MAIS PRÓXIMO");
		tableLayoutPanel5.RowCount = 13;
		tableLayoutPanel5.Height = 520;
		TableLayoutPanel tableLayoutPanel6 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4,
			BackColor = Panel,
			Padding = new Padding(18, 14, 18, 14),
			Margin = new Padding(0, 0, 0, 8)
		};
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
		tableLayoutPanel6.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		_autoCharacter = new CheckBox
		{
			Text = "DETECTAR NICK AUTOMATICAMENTE — jogador local",
			Checked = true,
			Dock = DockStyle.Fill,
			ForeColor = Dim
		};
		tableLayoutPanel6.Controls.Add(_autoCharacter, 0, 0);
		_txtCharacter = new TextBox
		{
			Dock = DockStyle.Fill,
			BackColor = PanelAlt,
			ForeColor = Fg,
			ReadOnly = true,
			PlaceholderText = "Aguardando personagem no mapa…"
		};
		_autoCharacter.CheckedChanged += delegate
		{
			_txtCharacter.ReadOnly = _autoCharacter.Checked;
			_txtCharacter.Text = (_autoCharacter.Checked ? "" : (_config?.Live.CharacterName ?? ""));
		};
		tableLayoutPanel6.Controls.Add(_txtCharacter, 0, 1);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill
		};
		Button button3 = new Button
		{
			Text = "CONECTAR / RECONECTAR",
			AutoSize = true,
			BackColor = Accent,
			ForeColor = Color.Black,
			FlatStyle = FlatStyle.Flat
		};
		button3.Click += delegate
		{
			ConnectToGame();
		};
		Button button4 = new Button
		{
			Text = "DESCONECTAR",
			AutoSize = true,
			BackColor = PanelAlt,
			ForeColor = Fg,
			FlatStyle = FlatStyle.Flat
		};
		button4.Click += delegate
		{
			DisconnectFromGame();
			RefreshStatus();
		};
		Button button5 = new Button
		{
			Text = "SAIR DA CONTA",
			AutoSize = true
		};
		button5.Click += delegate
		{
			_session.Revoke();
			EmergencyStop();
			ReturnToLogin = true;
			Close();
		};
		_account = new Label
		{
			AutoSize = true,
			Text = _session.Email,
			ForeColor = Dim
		};
		flowLayoutPanel.AutoScroll = true;
		flowLayoutPanel.Controls.AddRange(new Control[4] { button3, button4, button5, _account });
		tableLayoutPanel6.Controls.Add(flowLayoutPanel, 0, 2);
		tableLayoutPanel6.Controls.Add(new Label
		{
			Text = "SUB: sem giro durante buff; giro preparatório antes das teclas do combo HEURÍSTICO.\nAA: Melee radial ou Fan reto com largura própria e giro antes do AA; Hisuy indisponível.\nFOLLOW: ON + tecla segurada + caminho conhecido. F12 desliga tudo.\nFeche o chat; acertos/cooldowns/armadilhas invisíveis não são garantidos.",
			Dock = DockStyle.Fill,
			ForeColor = Dim
		}, 0, 3);
		tableLayoutPanel4.Controls.Add(tableLayoutPanel6, 0, 0);
		tableLayoutPanel4.Controls.Add(BuildFeatureSettings(), 0, 1);
		_rtbLog = new RichTextBox
		{
			Dock = DockStyle.Fill,
			BackColor = Color.FromArgb(13, 15, 19),
			ForeColor = Fg,
			ReadOnly = true,
			Font = new Font("Consolas", 9f),
			BorderStyle = BorderStyle.None,
			HideSelection = false
		};
		TabControl tabControl = new TabControl
		{
			Dock = DockStyle.Fill
		};
		TabPage tabPage = new TabPage("PLAYERS NO MAPA")
		{
			BackColor = Panel
		};
		TabPage tabPage2 = new TabPage("LOG")
		{
			BackColor = Panel
		};
		tabPage2.Controls.Add(_rtbLog);
		_playersGrid = new SmoothPlayersGrid
		{
			Dock = DockStyle.Fill,
			ReadOnly = true,
			AllowUserToAddRows = false,
			AllowUserToDeleteRows = false,
			RowHeadersVisible = false,
			MultiSelect = false,
			SelectionMode = DataGridViewSelectionMode.FullRowSelect,
			AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
			BackgroundColor = Panel,
			ForeColor = Fg,
			EnableHeadersVisualStyles = false,
			BorderStyle = BorderStyle.None,
			CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
			GridColor = Color.FromArgb(39, 47, 58),
			ColumnHeadersHeight = 36,
			ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
			RowTemplate =
			{
				Height = 31
			}
		};
		_playersGrid.DefaultCellStyle.BackColor = Panel;
		_playersGrid.DefaultCellStyle.ForeColor = Fg;
		_playersGrid.ColumnHeadersDefaultCellStyle.BackColor = PanelAlt;
		_playersGrid.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
		_playersGrid.SelectionChanged += delegate
		{
			if (!_refreshingPlayers)
			{
				_selectedMapSlot = ((_playersGrid.SelectedRows.Count == 1 && _playersGrid.SelectedRows[0].Tag is int value) ? new int?(value) : ((int?)null));
				_playersPlot.SelectedRowSlot = _selectedMapSlot;
				_playersPlot.Invalidate();
			}
		};
		string[] array = new string[11]
		{
			"#", "Slot", "Nome", "Vila", "Posição (tiles)", "Distância", "ΔX", "ΔY", "Você / Alvo", "Visibilidade",
			"Transformação"
		};
		foreach (string headerText in array)
		{
			_playersGrid.Columns.Add(new DataGridViewTextBoxColumn
			{
				HeaderText = headerText,
				SortMode = DataGridViewColumnSortMode.NotSortable
			});
		}
		int[] array2 = new int[11]
		{
			40, 48, 150, 85, 105, 75, 65, 65, 90, 110,
			110
		};
		for (int num3 = 0; num3 < array2.Length; num3++)
		{
			_playersGrid.Columns[num3].Width = array2[num3];
		}
		_playersStatus = new Label
		{
			Dock = DockStyle.Top,
			Height = 44,
			ForeColor = Warn,
			Text = "Aguardando lista local do mapa.\nVilas: Mist azul · Sand amarelo · Leaf verde · Renegado vermelho · Desconhecida neutro."
		};
		_hideOwnVillage = new CheckBox
		{
			Text = "Ocultar minha vila (mapa + overlay)",
			AutoSize = true,
			ForeColor = Fg,
			Checked = false
		};
		_hideOwnVillage.CheckedChanged += delegate
		{
			RefreshPlayers();
			RefreshOverlay();
		};
		FlowLayoutPanel flowLayoutPanel3 = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 34,
			WrapContents = false,
			AutoScroll = true
		};
		flowLayoutPanel3.Controls.Add(_hideOwnVillage);
		_overlayButton = new Button
		{
			Text = "OVERLAY OFF",
			AutoSize = true,
			ForeColor = Fg,
			BackColor = PanelAlt
		};
		_overlayButton.Click += delegate
		{
			if (_overlayEnabled)
			{
				StopOverlay();
			}
			else if (_ctx?.Provider != null && Allowed(AppFeature.MapPlayers))
			{
				_overlayEnabled = true;
				_ctx.SetOverlayEnabled(enabled: true);
				_overlayButton.Text = "OVERLAY ON · clique para desligar";
				AppendLog(LogLevel.Info, "Overlay ON: última posição conhecida de registros no mapa, projetada pela view observada; não revela trajeto invisível. F12 desliga.", EventOrigin.Requested);
			}
		};
		flowLayoutPanel3.Controls.Add(_overlayButton);
		PlayerVillage[] array3 = new PlayerVillage[4]
		{
			PlayerVillage.Leaf,
			PlayerVillage.Sand,
			PlayerVillage.Mist,
			PlayerVillage.Renegade
		};
		foreach (PlayerVillage playerVillage in array3)
		{
			CheckBox checkBox = new CheckBox
			{
				Text = "Ocultar " + PlayerVillages.Label(playerVillage),
				AutoSize = true,
				ForeColor = Fg,
				Margin = new Padding(12, 3, 0, 3)
			};
			checkBox.CheckedChanged += delegate
			{
				RefreshPlayers();
				RefreshOverlay();
			};
			_villageFilters.Add(playerVillage, checkBox);
			flowLayoutPanel3.Controls.Add(checkBox);
		}
		_playersPlot = new MapPlayersPlot();
		TableLayoutPanel tableLayoutPanel7 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 2,
			Margin = Padding.Empty,
			Padding = new Padding(0, 8, 0, 0),
			BackColor = Bg
		};
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel7.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 238f));
		tableLayoutPanel7.RowStyles.Add(new RowStyle(SizeType.Percent, 72f));
		tableLayoutPanel7.RowStyles.Add(new RowStyle(SizeType.Percent, 28f));
		_playersPlot.Margin = new Padding(0, 0, 10, 10);
		_playersGrid.Margin = new Padding(0, 0, 10, 0);
		tableLayoutPanel7.Controls.Add(_playersPlot, 0, 0);
		tableLayoutPanel7.Controls.Add(_playersGrid, 0, 1);
		var mapHelp = new Panel { Dock = DockStyle.Fill, BackColor = Panel, Padding = new Padding(18), Margin = Padding.Empty };
		var mapHelpText = new Label
		{
			Dock = DockStyle.Fill,
			AutoSize = false,
			ForeColor = Dim,
			Font = new Font("Segoe UI", 9.5f),
			Text = "LEGENDA\n\n●  Seu personagem\n\n●  Outro jogador\n\n●  Alvo atual\n\n■  Terreno / água\n■  Parede\n■  Portal\n\n\nCOMO USAR\n\nRoda do mouse: zoom\nArrastar: mover o mapa\nDuplo clique: mostrar tudo\n\nO mapa usa coordenadas absolutas e não fica preso ao seu personagem."
		};
		mapHelp.Controls.Add(mapHelpText);
		tableLayoutPanel7.Controls.Add(mapHelp, 1, 0);
		tableLayoutPanel7.SetRowSpan(mapHelp, 2);
		tabPage.Controls.Add(tableLayoutPanel7);
		tabPage.Controls.Add(_playersStatus);
		tabPage.Controls.Add(flowLayoutPanel3);
		tabControl.TabPages.Add(tabPage);
		tabControl.TabPages.Add(tabPage2);
		tableLayoutPanel.Controls.Add(tabControl, 0, 3);
		InstallModernShell(tableLayoutPanel, tableLayoutPanel2, tableLayoutPanel3, tableLayoutPanel4, tableLayoutPanel5, tabControl, tabPage, tabPage2);
	}

	private Control BuildFeatureSettings()
	{
		var grid = new TableLayoutPanel
		{
			Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3,
			BackColor = Bg, Margin = Padding.Empty, AutoScroll = true
		};
		for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3333f));
		for (int i = 0; i < 3; i++) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3333f));
		AddCard(0, 0, "AUTO TURNING", () => $"Ativação: {string.Join(" / ", _config.Live.TurnActivationKeys)}\nTeclas de giro", () => ConfigureModule(ModuleSettingsFeature.AutoTurning));
		AddCard(1, 0, "AUTO CAST TURN", () => $"{_config.AutoCastTurn.TargetMode} · {_config.AutoCastTurn.LeadTimeMs} ms\nExcluídos: {_config.AutoCastTurn.ExcludedSpellIds.Length}", ConfigureCastTurn);
		AddCard(2, 0, "SUBPUNISH", () => $"Combo: {string.Join(" → ", _config.Live.PunishKeys)}\nModo: {_config.Live.PunishMode}", () => ConfigureModule(ModuleSettingsFeature.SubPunish));
		AddCard(0, 1, "AUTO AA", () => $"{_config.CombatAssist.AttackMode} · {_config.CombatAssist.AttackKey}\nRaio: {_config.CombatAssist.WeaponRadius:0.#} tiles", () => ConfigureModule(ModuleSettingsFeature.AutoAttack));
		AddCard(1, 1, "AUTO TOOL", () => $"{_config.CombatAssist.ToolKey} · {_config.CombatAssist.ToolRange:0.#} tiles\nIntervalo: {_config.CombatAssist.ToolIntervalMs} ms", () => ConfigureModule(ModuleSettingsFeature.Tool));
		AddCard(2, 1, "AUTO FOLLOW", () => $"Segurar: {_config.CombatAssist.FollowHoldKey}\nParar: {_config.CombatAssist.FollowStopDistance:0.#} tiles", () => ConfigureModule(ModuleSettingsFeature.Follow));
		AddCard(0, 2, "AUTO STAMINA", () => $"Desligar <{_config.CombatAssist.StaminaDisableBelowPercent}% · religar >{_config.CombatAssist.StaminaEnableAbovePercent}%\nRun: {(_ctx?.StaminaBindingValid == true ? "validado" : "verifique tecla / modo")}", () => ConfigureModule(ModuleSettingsFeature.Stamina));
		AddCard(1, 2, "AUTO TARGET", () => $"Memória experimental · raio: {_config.AutoTarget.RadiusTiles:0.#} tiles\nIgnorar própria vila: {(_config.AutoTarget.IgnoreSameVillage ? "sim" : "não")}", ConfigureAutoTarget);
		return grid;

		void AddCard(int column, int row, string title, Func<string> summary, Action configure)
		{
			var card = new TableLayoutPanel
			{
				Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
				BackColor = Panel, Padding = new Padding(14, 10, 14, 10), Margin = new Padding(0, 0, 10, 10)
			};
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
			card.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
			card.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
			card.Controls.Add(new Label
			{
				Text = title, Dock = DockStyle.Fill, ForeColor = Accent,
				Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold)
			}, 0, 0);
			var detail = new Label
			{
				Dock = DockStyle.Fill, ForeColor = Fg,
				Font = new Font("Segoe UI", 9f), AutoEllipsis = true
			};
			_settingsSummaries.Add((detail, summary));
			card.Controls.Add(detail, 0, 1);
			var edit = new Button
			{
				Text = "CONFIGURAR  ›", Dock = DockStyle.Fill,
				BackColor = PanelAlt, ForeColor = Fg, FlatStyle = FlatStyle.Flat,
				Cursor = Cursors.Hand
			};
			edit.FlatAppearance.BorderSize = 0;
			edit.Click += (_, _) => configure();
			card.Controls.Add(edit, 0, 2);
			grid.Controls.Add(card, column, row);
		}
	}

	private void RefreshSettingsSummaries()
	{
		if (_config == null) return;
		foreach (var (label, summary) in _settingsSummaries) label.Text = summary();
	}

	private void InstallModernShell(TableLayoutPanel legacyRoot, Control header, Control modules, Control dashboard,
		TableLayoutPanel statusCard,
		TabControl legacyTabs, TabPage mapPage, TabPage logPage)
	{
		legacyTabs.TabPages.Remove(mapPage);
		legacyTabs.TabPages.Remove(logPage);
		legacyRoot.Controls.Clear();
		Controls.Remove(legacyRoot);

		var shell = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			BackColor = Bg,
			Padding = Padding.Empty
		};
		shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 226f));
		shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

		var sidebar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(16, 19, 25), Padding = new Padding(14) };
		var brand = new Label
		{
			Dock = DockStyle.Top,
			Height = 74,
			Text = "  N  NIN AUTOASSIST\n      controle inteligente",
			ForeColor = Fg,
			Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
			TextAlign = ContentAlignment.MiddleLeft
		};
		brand.Paint += delegate(object? sender, PaintEventArgs e)
		{
			using var brush = new SolidBrush(Accent);
			e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
			e.Graphics.FillEllipse(brush, 0, 23, 34, 34);
			TextRenderer.DrawText(e.Graphics, "N", new Font("Segoe UI", 12f, FontStyle.Bold), new Rectangle(0, 23, 34, 34), Color.White,
				TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		};
		sidebar.Controls.Add(brand);

		var nav = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Top = 80,
			Height = 245,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Padding = new Padding(0, 12, 0, 0)
		};
		sidebar.Controls.Add(nav);
		nav.BringToFront();

		var footer = new Label
		{
			Dock = DockStyle.Bottom,
			Height = 66,
			Text = $"VERSÃO {Application.ProductVersion.Split('+')[0]}\nF12 · parada de emergência",
			ForeColor = Dim,
			Font = new Font("Segoe UI", 8.5f),
			TextAlign = ContentAlignment.MiddleLeft
		};
		sidebar.Controls.Add(footer);

		var host = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(14, 12, 16, 16) };
		var dashboardPage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Bg };
		dashboardPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 64f));
		dashboardPage.RowStyles.Add(new RowStyle(SizeType.Absolute, 122f));
		dashboardPage.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		header.Margin = new Padding(0, 0, 0, 10);
		modules.Margin = new Padding(0, 0, 0, 10);
		dashboard.Margin = Padding.Empty;
		dashboardPage.Controls.Add(header, 0, 0);
		dashboardPage.Controls.Add(modules, 0, 1);
		dashboardPage.Controls.Add(dashboard, 0, 2);

		var mapSurface = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = Padding.Empty };
		while (mapPage.Controls.Count > 0)
		{
			Control child = mapPage.Controls[0];
			mapPage.Controls.Remove(child);
			mapSurface.Controls.Add(child);
		}
		var logSurface = new Panel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(0, 8, 0, 0) };
		while (logPage.Controls.Count > 0)
		{
			Control child = logPage.Controls[0];
			logPage.Controls.Remove(child);
			logSurface.Controls.Add(child);
		}
		logSurface.Controls.Remove(_rtbLog);
		var logLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
		logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 62f));
		logLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 38f));
		var statusScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Panel, Margin = new Padding(0, 0, 0, 8) };
		statusCard.Dock = DockStyle.Top;
		statusCard.Margin = Padding.Empty;
		statusScroll.Controls.Add(statusCard);
		logLayout.Controls.Add(statusScroll, 0, 0);
		_rtbLog.Margin = Padding.Empty;
		logLayout.Controls.Add(_rtbLog, 0, 1);
		logSurface.Controls.Add(logLayout);
		Control[] pages = { dashboardPage, mapSurface, logSurface };
		foreach (Control page in pages)
		{
			page.Visible = false;
			host.Controls.Add(page);
		}

		var buttons = new List<Button>();
		Button AddNav(string icon, string text, Control page)
		{
			var button = new Button
			{
				Text = $"{icon}   {text}",
				Width = 194,
				Height = 48,
				Margin = new Padding(0, 0, 0, 7),
				TextAlign = ContentAlignment.MiddleLeft,
				Padding = new Padding(14, 0, 0, 0),
				FlatStyle = FlatStyle.Flat,
				BackColor = Color.Transparent,
				ForeColor = Dim,
				Font = new Font("Segoe UI Semibold", 10f),
				Cursor = Cursors.Hand
			};
			button.FlatAppearance.BorderSize = 0;
			button.FlatAppearance.MouseOverBackColor = PanelAlt;
			button.Click += delegate
			{
				foreach (Control candidate in pages) candidate.Visible = candidate == page;
				foreach (Button item in buttons)
				{
					item.BackColor = item == button ? Color.FromArgb(42, 35, 79) : Color.Transparent;
					item.ForeColor = item == button ? Color.White : Dim;
				}
				page.BringToFront();
			};
			buttons.Add(button);
			nav.Controls.Add(button);
			return button;
		}

		var dashboardButton = AddNav("▦", "Painel", dashboardPage);
		AddNav("◎", "Mapa ao vivo", mapSurface);
		AddNav("≡", "Logs", logSurface);

		shell.Controls.Add(sidebar, 0, 0);
		shell.Controls.Add(host, 1, 0);
		Controls.Add(shell);
		dashboardPage.Visible = true;
		dashboardPage.BringToFront();
		dashboardButton.BackColor = Color.FromArgb(42, 35, 79);
		dashboardButton.ForeColor = Color.White;
	}

	private void RefreshPlayers()
	{
		MapPlayersFrame mapPlayersFrame = _ctx?.MapPlayers;
		bool flag = _hideOwnVillage.Checked;
		PlayerVillage own = mapPlayersFrame?.LocalVillage ?? PlayerVillage.Unknown;
		foreach (KeyValuePair<PlayerVillage, CheckBox> villageFilter in _villageFilters)
		{
			villageFilter.Deconstruct(out var key, out var value);
			PlayerVillage playerVillage = key;
			CheckBox checkBox = value;
			checkBox.Visible = playerVillage != own;
			checkBox.Enabled = playerVillage != own;
		}
		PlayerVillage[] array = (from entry in _villageFilters
			where entry.Key != own && entry.Value.Checked
			select entry.Key).ToArray();
		int num = (flag ? 1 : 0) | array.Aggregate(0, (int mask, PlayerVillage village) => mask | (1 << (int)village));
		string text = (((object)mapPlayersFrame == null) ? ("Lista indisponível/antiga — aguardando leitura válida. " + (_ctx?.MapPlayersStatus ?? "Desconectado")) : ((flag && !PlayerVillages.IsKnown(mapPlayersFrame.LocalVillage)) ? "Filtro ativo: sua vila ainda é desconhecida; aguardando leitura válida.\nNenhum personagem será classificado como de outra vila sem essa informação." : (_ctx?.MapPlayersStatus + "  ·  " + (_ctx?.FullMapStatus ?? "Mapa indisponível") + ((flag || array.Length != 0) ? "\nFiltros apenas visuais; não alteram automações." : "\nMapa completo em coordenadas absolutas; roda para zoom, arraste para mover."))));
		if (_playersStatus.Text != text)
		{
			_playersStatus.Text = text;
		}
		_playersPlot.Terrain = _ctx?.FullMap;
		_playersPlot.LoadingStatus = _ctx?.FullMapStatus ?? "Desconectado / mapa indisponível";
		_playersPlot.GridWidth = mapPlayersFrame?.GridWidth;
		_playersPlot.GridHeight = mapPlayersFrame?.GridHeight;
		if ((object)mapPlayersFrame == _shownPlayers && num == _shownVillageFilterMask)
		{
			return;
		}
		MapPlayersFrame shownPlayers = _shownPlayers;
		_shownPlayers = mapPlayersFrame;
		_shownVillageFilterMask = num;
		if ((object)mapPlayersFrame == null)
		{
			return;
		}
		if ((object)shownPlayers != null)
		{
			MapPlayersFrame mapPlayersFrame2 = shownPlayers;
			if (mapPlayersFrame2.Map != mapPlayersFrame.Map || mapPlayersFrame2.LocalId != mapPlayersFrame.LocalId)
			{
				_selectedMapSlot = null;
				_playersPlot.ResetScale();
			}
		}
		IReadOnlyList<NumberedMapPlayer> readOnlyList = mapPlayersFrame.NumberedForDisplay(flag, (IReadOnlyCollection<PlayerVillage>)(object)array);
		MapPlayer[] array2 = readOnlyList.Select((NumberedMapPlayer entry) => entry.Player).ToArray();
		PlayerState player = _ctx?.Frame?.Snapshot.Player;
		_playersPlot.MapNumber = mapPlayersFrame.Map;
		_playersPlot.PlayerX = player?.PositionX ?? 0;
		_playersPlot.PlayerY = player?.PositionY ?? 0;
		_playersPlot.Players = (((object)player == null) ? ((IReadOnlyList<(int Slot, string Name, double X, double Y, bool Targeted)>)Array.Empty<(int, string, double, double, bool)>()) : ((IReadOnlyList<(int Slot, string Name, double X, double Y, bool Targeted)>)(from p in array2
			where p.X.HasValue && p.Y.HasValue
			select (Slot: p.Slot, Name: p.Name, X: p.X.Value, Y: p.Y.Value, Targeted: p.IsSelected)).ToArray()));
		if (!array2.Any((MapPlayer p) => p.Slot == _selectedMapSlot))
		{
			_selectedMapSlot = null;
		}
		_playersPlot.SelectedRowSlot = _selectedMapSlot;
		_playersPlot.Invalidate();
		_refreshingPlayers = true;
		try
		{
			_playersGrid.SuspendLayout();
			HashSet<int> hashSet = array2.Select((MapPlayer p) => p.Slot).ToHashSet();
			for (int num2 = _playersGrid.Rows.Count - 1; num2 >= 0; num2--)
			{
				if (!(_playersGrid.Rows[num2].Tag is int item) || !hashSet.Contains(item))
				{
					_playersGrid.Rows.RemoveAt(num2);
				}
			}
			for (int num3 = 0; num3 < array2.Length; num3++)
			{
				MapPlayer mapPlayer = array2[num3];
				int num4 = -1;
				for (int num5 = num3; num5 < _playersGrid.Rows.Count; num5++)
				{
					if (object.Equals(_playersGrid.Rows[num5].Tag, mapPlayer.Slot))
					{
						num4 = num5;
						break;
					}
				}
				if (num4 < 0)
				{
					_playersGrid.Rows.Insert(num3, 1);
					_playersGrid.Rows[num3].Tag = mapPlayer.Slot;
				}
				else if (num4 != num3)
				{
					DataGridViewRow dataGridViewRow = _playersGrid.Rows[num4];
					_playersGrid.Rows.RemoveAt(num4);
					_playersGrid.Rows.Insert(num3, dataGridViewRow);
				}
				double? x = mapPlayer.X;
				object obj;
				if (x.HasValue)
				{
					double valueOrDefault = x.GetValueOrDefault();
					x = mapPlayer.Y;
					if (x.HasValue)
					{
						double valueOrDefault2 = x.GetValueOrDefault();
						obj = $"{valueOrDefault:0.0}, {valueOrDefault2:0.0}";
						goto IL_0503;
					}
				}
				obj = "Desconhecida";
				goto IL_0503;
				IL_0598:
				object obj2;
				string text2 = (string)obj2;
				object obj3;
				if ((object)player != null)
				{
					x = mapPlayer.X;
					if (x.HasValue)
					{
						double valueOrDefault3 = x.GetValueOrDefault();
						obj3 = (valueOrDefault3 - player.PositionX).ToString("+0.0;-0.0;0.0");
						goto IL_05e2;
					}
				}
				obj3 = "?";
				goto IL_05e2;
				IL_0503:
				string text3 = (string)obj;
				if ((object)player != null)
				{
					x = mapPlayer.X;
					if (x.HasValue)
					{
						double valueOrDefault4 = x.GetValueOrDefault();
						x = mapPlayer.Y;
						if (x.HasValue)
						{
							double valueOrDefault5 = x.GetValueOrDefault();
							obj2 = Math.Sqrt(Math.Pow(valueOrDefault4 - player.PositionX, 2.0) + Math.Pow(valueOrDefault5 - player.PositionY, 2.0)).ToString("0.0");
							goto IL_0598;
						}
					}
				}
				obj2 = "?";
				goto IL_0598;
				IL_05e2:
				string text4 = (string)obj3;
				object obj4;
				if ((object)player != null)
				{
					x = mapPlayer.Y;
					if (x.HasValue)
					{
						double valueOrDefault6 = x.GetValueOrDefault();
						obj4 = (valueOrDefault6 - player.PositionY).ToString("+0.0;-0.0;0.0");
						goto IL_062c;
					}
				}
				obj4 = "?";
				goto IL_062c;
				IL_062c:
				string text5 = (string)obj4;
				object[] array3 = new object[11]
				{
					readOnlyList[num3].Number,
					mapPlayer.Slot,
					mapPlayer.Name,
					PlayerVillages.Label(mapPlayer.Village),
					text3,
					text2,
					text4,
					text5,
					mapPlayer.IsLocal ? "VOCÊ" : (mapPlayer.IsSelected ? "ALVO" : "—"),
					"Não mapeada",
					"Não mapeada"
				};
				for (int num6 = 0; num6 < array3.Length; num6++)
				{
					if (!object.Equals(_playersGrid.Rows[num3].Cells[num6].Value, array3[num6]))
					{
						_playersGrid.Rows[num3].Cells[num6].Value = array3[num6];
					}
				}
				int? num7 = PlayerVillages.ColorArgb(mapPlayer.Village);
				Color color;
				if (num7.HasValue)
				{
					int valueOrDefault7 = num7.GetValueOrDefault();
					color = Color.FromArgb(valueOrDefault7);
				}
				else
				{
					color = Fg;
				}
				Color color2 = color;
				int[] array4 = new int[2] { 2, 3 };
				foreach (int index in array4)
				{
					DataGridViewCellStyle style = _playersGrid.Rows[num3].Cells[index].Style;
					Color foreColor = (style.SelectionForeColor = color2);
					style.ForeColor = foreColor;
					style.SelectionBackColor = PanelAlt;
				}
			}
			foreach (DataGridViewRow item2 in (IEnumerable)_playersGrid.Rows)
			{
				if (item2.Selected != object.Equals(item2.Tag, _selectedMapSlot))
				{
					item2.Selected = object.Equals(item2.Tag, _selectedMapSlot);
				}
			}
		}
		finally
		{
			_playersGrid.ResumeLayout();
			_refreshingPlayers = false;
		}
	}

	private Control BuildLegend()
	{
		return new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Padding = new Padding(0, 4, 0, 0),
			Controls =
			{
				(Control?)new Label
				{
					Text = "PROVENIÊNCIA DOS EVENTOS",
					ForeColor = Dim,
					AutoSize = true,
					Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold),
					Margin = new Padding(0, 0, 0, 2)
				},
				(Control?)LegendItem("● OBSERVADO  — lido, não confirmado", Warn),
				(Control?)LegendItem("● CONFIRMADO — evidência suficiente", Good),
				(Control?)LegendItem("● SOLICITADO — pedido enviado", ReqBlue),
				(Control?)LegendItem("● EXECUTADO  — input disparado (acerto?)", ExecOrg)
			}
		};
	}

	private static Label LegendItem(string text, Color color)
	{
		return new Label
		{
			Text = text,
			ForeColor = color,
			AutoSize = true,
			Font = new Font("Segoe UI", 8f),
			Margin = new Padding(0)
		};
	}

	private static Button MakeModuleButton(string title, bool on)
	{
		return new ModuleCardButton
		{
			Text = title + "\nINATIVO",
			Dock = DockStyle.Fill,
			Margin = new Padding(5, 3, 5, 3),
			BackColor = PanelAlt,
			ForeColor = Fg,
			Tag = on
		};
	}

	private Label AddStatusRow(TableLayoutPanel grid, int row, string caption)
	{
		if (grid.RowStyles.Count <= row)
		{
			grid.RowStyles.Add(new RowStyle(SizeType.Percent, 8.333333f));
		}
		Label control = new Label
		{
			Text = caption,
			ForeColor = Dim,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			Font = new Font("Segoe UI Semibold", 8f, FontStyle.Bold)
		};
		Label label = new Label
		{
			Text = "—",
			ForeColor = Fg,
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleLeft,
			AutoEllipsis = true
		};
		grid.Controls.Add(control, 0, row);
		grid.Controls.Add(label, 1, row);
		return label;
	}

	private void LoadConfigAndContext()
	{
		AppPaths.Settings.EnsureConfig();
		_config = ConfigLoader.Load(ConfigPath);
		RefreshSettingsSummaries();
		_txtCharacter.Text = (_autoCharacter.Checked ? "" : _config.Live.CharacterName);
		AppendLog(LogLevel.Info, "AO VIVO: módulos OFF. SubPunish somente Tai/Água; Sub normal cria decoy e não dispara combo. Remoção antecipada é HEURÍSTICA.", null);
		RefreshModuleButtons();
		RefreshModeBadge();
		RefreshStatus();
	}

	private void RefreshModeBadge()
	{
		_lblMode.Text = ((_ctx?.Provider == null) ? "AO VIVO · DESCONECTADO" : ("TURN: " + string.Join(" / ", _config.Live.TurnActivationKeys)));
		_lblMode.ForeColor = (((object)_ctx?.Frame == null) ? Warn : Good);
		_lblProvider.Text = _ctx?.Status ?? "Desconectado — leitura indisponível";
		bool flag = _ctx?.Provider != null && _session.Access.IsValid;
		GameMemoryProfile profile = _ctx?.Provider?.Profile;
		int num;
		if (Allowed(AppFeature.AutoAttack))
		{
			GameMemoryProfile gameMemoryProfile = profile;
			if ((object)gameMemoryProfile != null && gameMemoryProfile.ApprovesAttackMode(_config.CombatAssist.AttackMode.ToString()))
			{
				num = ((_config.CombatAssist.AttackMode != AutoAttackMode.Fan || profile.ValidatedInputKinds.Contains<string>("Turning", StringComparer.Ordinal)) ? 1 : 0);
				goto IL_0157;
			}
		}
		num = 0;
		goto IL_0157;
		IL_0157:
		bool flag2 = (byte)num != 0;
		bool flag3 = Allowed(AppFeature.Tool) && Approved(LiveInputKind.Tool);
		_btnAutoTurning.Enabled = flag && Allowed(AppFeature.AutoTurning) && Approved(LiveInputKind.Turning);
		_btnSubPunish.Enabled = flag && Allowed(SubFeature) && Approved(LiveInputKind.Punish);
		_btnCombat.Enabled = flag && flag2 && Approved(LiveInputKind.Attack);
		_btnTool.Enabled = flag && flag3;
		_btnFollow.Enabled = flag && Allowed(AppFeature.Follow) && Approved(LiveInputKind.Follow);
		_btnStamina.Enabled = flag && Allowed(AppFeature.Stamina) && Approved(LiveInputKind.Stamina);
		_btnCastTurn.Enabled = flag && Allowed(AppFeature.AutoCastTurn) && Approved(LiveInputKind.CastTurning) && (object)profile?.CastTiming != null;
		_btnCastTurn.Enabled &= _config.AutoCastTurn.TargetMode != CastTurnTargetMode.Nearest || Allowed(AppFeature.MapPlayers);
		_btnAutoTarget.Enabled = flag && Allowed(AppFeature.AutoTarget) && Allowed(AppFeature.MapPlayers) && Approved(LiveInputKind.TargetMemory);
		_inspectionButton.Enabled = Allowed(AppFeature.ResourceInspection);
		_account.Text = _session.Email + " · " + _session.Status;
		bool Approved(LiveInputKind kind)
		{
			if (LocalTrialPolicy.CanActuate(profile))
			{
				if (profile.ExternalProfile)
				{
					return profile.ValidatedInputKinds.Contains<string>(kind.ToString(), StringComparer.Ordinal);
				}
				return true;
			}
			return false;
		}
	}

	private void DisconnectFromGame()
	{
		StopOverlay();
		_shownPlayers = null;
		int? selectedMapSlot = (_playersPlot.SelectedRowSlot = null);
		_selectedMapSlot = selectedMapSlot;
		_playersPlot.ResetScale();
		_playersGrid.Rows.Clear();
		_observedCastIds.Reset();
		if (_ctx != null)
		{
			_ctx.SetAutoTurningEnabled(enabled: false);
			_ctx.SetSubPunishEnabled(enabled: false);
			_ctx.SetAttackEnabled(false);
			_ctx.SetToolEnabled(false);
			_ctx.SetFollowEnabled(enabled: false);
			_ctx.SetStaminaEnabled(enabled: false);
			_ctx.SetCastTurnEnabled(enabled: false);
			_ctx.SetAutoTargetEnabled(enabled: false);
			_ctx.MemoryLog.EntryLogged -= OnLogEntry;
			_ctx.Dispose();
			_ctx = null;
		}
		Button btnAutoTurning = _btnAutoTurning;
		Button btnSubPunish = _btnSubPunish;
		Button btnCombat = _btnCombat;
		Button btnTool = _btnTool;
		Button btnFollow = _btnFollow;
		object obj = (_btnStamina.Tag = false);
		object obj3 = (btnFollow.Tag = obj);
		object obj5 = (btnCombat.Tag = (btnTool.Tag = obj3));
		object tag = (btnSubPunish.Tag = obj5);
		btnAutoTurning.Tag = tag;
		_btnCastTurn.Tag = false;
		_btnAutoTarget.Tag = false;
		RefreshModuleButtons();
		RefreshModeBadge();
		RefreshSettingsSummaries();
	}

	private void ConnectToGame()
	{
		if (!_session.Access.IsValid)
		{
			return;
		}
		if (!_autoCharacter.Checked && string.IsNullOrWhiteSpace(_txtCharacter.Text))
		{
			AppendLog(LogLevel.Warning, "Informe o nome exato do personagem para conectar.", null);
			return;
		}
		DisconnectFromGame();
		_config = _config with
		{
			Live = _config.Live with
			{
				CharacterName = (_autoCharacter.Checked ? _config.Live.CharacterName : _txtCharacter.Text.Trim()),
				AllowCounterAttackSequence = false
			}
		};
		_ctx = new LiveContext(_config, _session.Access, _autoCharacter.Checked);
		RefreshSettingsSummaries();
		_ctx.MemoryLog.EntryLogged += OnLogEntry;
		foreach (LogEntry item in _ctx.MemoryLog.Snapshot())
		{
			OnLogEntry(item);
		}
		RefreshModeBadge();
		RefreshStatus();
	}

	private void ConfigureModule(ModuleSettingsFeature feature)
	{
		EmergencyStop();
		using ModuleSettingsForm form = new ModuleSettingsForm(feature, _config.Live, _config.CombatAssist);
		if (form.ShowDialog(this) == DialogResult.OK)
		{
			AutoAssistConfig config = _config with
			{
				CombatAssist = form.CombatSettings,
				Live = form.LiveSettings with
				{
					AllowCounterAttackSequence = false
				}
			};
			try
			{
				ConfigLoader.Save(SettingsPath, config);
			}
			catch (Exception ex)
			{
				AppendLog(LogLevel.Error, "Não foi possível salvar: " + ex.Message, null);
				return;
			}
			_config = config;
			ConnectToGame();
			AppendLog(LogLevel.Info, $"{feature} salvo; módulos OFF e autorizações revogadas.", EventOrigin.Requested);
		}
	}

	private void ConfigureCastTurn()
	{
		EmergencyStop();
		using AutoCastTurnForm autoCastTurnForm = new AutoCastTurnForm(_config.AutoCastTurn, () => _observedCastIds.Recent);
		if (autoCastTurnForm.ShowDialog(this) == DialogResult.OK)
		{
			AutoAssistConfig config = _config with
			{
				AutoCastTurn = autoCastTurnForm.Settings
			};
			try
			{
				ConfigLoader.Save(SettingsPath, config);
			}
			catch (Exception ex)
			{
				AppendLog(LogLevel.Error, "Não foi possível salvar: " + ex.Message, null);
				return;
			}
			_config = config;
			ConnectToGame();
		}
	}

	private void ConfigureAutoTarget()
	{
		EmergencyStop();
		using AutoTargetForm form = new AutoTargetForm(_config.AutoTarget);
		if (form.ShowDialog(this) != DialogResult.OK) return;
		AutoAssistConfig next = _config with { AutoTarget = form.Settings };
		try { ConfigLoader.Save(SettingsPath, next); }
		catch (Exception ex)
		{
			AppendLog(LogLevel.Error, "Não foi possível salvar: " + ex.Message, null);
			return;
		}
		_config = next;
		ConnectToGame();
	}

	private void ToggleAutoTurning()
	{
		object tag = _btnAutoTurning.Tag;
		bool flag = default(bool);
		int num;
		if (tag is bool)
		{
			flag = (bool)tag;
			num = 1;
		}
		else
		{
			num = 0;
		}
		bool flag2 = ((uint)num & (flag ? 1u : 0u)) == 0;
		_btnAutoTurning.Tag = flag2;
		_ctx?.SetAutoTurningEnabled(flag2);
		AppendLog(LogLevel.Info, "Auto Turning " + (flag2 ? "ATIVADO" : "DESATIVADO") + " pelo usuário", EventOrigin.Requested);
		RefreshModuleButtons();
	}

	private void ToggleSubPunish()
	{
		object tag = _btnSubPunish.Tag;
		bool flag = default(bool);
		int num;
		if (tag is bool)
		{
			flag = (bool)tag;
			num = 1;
		}
		else
		{
			num = 0;
		}
		bool flag2 = ((uint)num & (flag ? 1u : 0u)) == 0;
		_btnSubPunish.Tag = flag2;
		_ctx?.SetSubPunishEnabled(flag2);
		AppendLog(LogLevel.Info, "SubPunish " + (flag2 ? "ATIVADO" : "DESATIVADO") + " pelo usuário", EventOrigin.Requested);
		RefreshModuleButtons();
	}

	private void RefreshModuleButtons()
	{
		RefreshModuleButton(_btnAutoTurning, "AUTO TURNING");
		RefreshModuleButton(_btnCastTurn, "AUTO CAST TURN");
		RefreshModuleButton(_btnAutoTarget, "AUTO TARGET");
		RefreshModuleButton(_btnSubPunish, "SUBPUNISH TAI/ÁGUA");
		RefreshModuleButton(_btnCombat, "AUTO AA");
		RefreshModuleButton(_btnTool, "AUTO TOOL");
		RefreshModuleButton(_btnFollow, "AUTO FOLLOW");
		RefreshModuleButton(_btnStamina, "AUTO STAMINA");
	}

	private static void RefreshModuleButton(Button btn, string title)
	{
		object tag = btn.Tag;
		bool flag = default(bool);
		int num;
		if (tag is bool)
		{
			flag = (bool)tag;
			num = 1;
		}
		else
		{
			num = 0;
		}
		bool flag2 = (byte)((uint)num & (flag ? 1u : 0u)) != 0;
		btn.Text = title + "\n" + (flag2 ? "ATIVO" : "INATIVO");
		btn.Invalidate();
	}

	private void EmergencyStop()
	{
		StopOverlay();
		_ctx?.SetAutoTurningEnabled(enabled: false);
		_ctx?.SetSubPunishEnabled(enabled: false);
		_ctx?.SetAttackEnabled(false);
		_ctx?.SetToolEnabled(false);
		_ctx?.SetFollowEnabled(enabled: false);
		_ctx?.SetStaminaEnabled(enabled: false);
		_ctx?.SetCastTurnEnabled(enabled: false);
		_ctx?.SetAutoTargetEnabled(enabled: false);
		_btnAutoTurning.Tag = false;
		_btnSubPunish.Tag = false;
		Button btnCombat = _btnCombat;
		Button btnTool = _btnTool;
		Button btnFollow = _btnFollow;
		object obj = (_btnStamina.Tag = false);
		object tag = (btnFollow.Tag = obj);
		btnCombat.Tag = (btnTool.Tag = tag);
		_btnCastTurn.Tag = false;
		_btnAutoTarget.Tag = false;
		LiveInputController.AcknowledgeEmergencyAfterDisarm();
		RefreshModuleButtons();
		AppendLog(LogLevel.Warning, "PARADA DE EMERGÊNCIA acionada — módulos desativados", EventOrigin.Requested);
		SystemSounds.Exclamation.Play();
	}

	private void OnKeyDown(object? sender, KeyEventArgs e)
	{
		string emergencyStopKey = _config.General.EmergencyStopKey;
		if (e.KeyCode.ToString().Equals(emergencyStopKey, StringComparison.OrdinalIgnoreCase))
		{
			e.Handled = true;
			EmergencyStop();
		}
	}

	private void OnTick(object? sender, EventArgs e)
	{
		bool flag = LiveInputController.IsDown(123);
		if ((LiveInputController.ConsumeEmergency() && !flag) || (flag && !_emergencyKeyWasDown))
		{
			EmergencyStop();
		}
		_emergencyKeyWasDown = flag;
		if (Environment.TickCount64 - _lastUiRefresh < 100)
		{
			return;
		}
		_lastUiRefresh = Environment.TickCount64;
		if (!_session.Access.IsValid)
		{
			_timer.Stop();
			_session.Revoke();
			EmergencyStop();
			Form[] ownedForms = base.OwnedForms;
			for (int i = 0; i < ownedForms.Length; i++)
			{
				ownedForms[i].Close();
			}
			ReturnToLogin = true;
			MessageBox.Show(this, "A sessão perdeu a autorização ou a conexão. Entre novamente para continuar.", "Sessão encerrada");
			Close();
			return;
		}
		int permissionsMask = PermissionsMask;
		if (permissionsMask != _permissionsMask)
		{
			_permissionsMask = permissionsMask;
			EmergencyStop();
			if (!Allowed(AppFeature.ResourceInspection))
			{
				foreach (ResourceInspectionForm item in base.OwnedForms.OfType<ResourceInspectionForm>())
				{
					item.Close();
				}
			}
			AppendLog(LogLevel.Warning, "Permissões atualizadas pelo administrador; módulos desarmados.", null);
		}
		LiveFrame liveFrame = _ctx?.Frame;
		if ((object)liveFrame != null)
		{
			if (_loadedCharacter != liveFrame.PlayerName && !string.IsNullOrWhiteSpace(liveFrame.PlayerName))
			{
				_observedCastIds.Reset();
				EmergencyStop();
				try
				{
					AutoAssistConfig autoAssistConfig = ConfigLoader.Load(AppPaths.Settings.EnsureCharacterConfig(liveFrame.PlayerName));
					_loadedCharacter = liveFrame.PlayerName;
					_txtCharacter.Text = liveFrame.PlayerName;
					_config = autoAssistConfig with
					{
						Live = autoAssistConfig.Live with
						{
							CharacterName = liveFrame.PlayerName,
							AllowCounterAttackSequence = false
						}
					};
					RefreshSettingsSummaries();
					AppendLog(LogLevel.Info, "Configurações de " + liveFrame.PlayerName + " carregadas; módulos OFF.", null);
					ConnectToGame();
					return;
				}
				catch (Exception ex)
				{
					DisconnectFromGame();
					AppendLog(LogLevel.Error, "Configuração por personagem indisponível; conexão desarmada: " + ex.Message, null);
					return;
				}
			}
			if (_autoCharacter.Checked && _txtCharacter.Text != liveFrame.PlayerName)
			{
				_txtCharacter.Text = liveFrame.PlayerName;
			}
			if (_loadedCharacter == liveFrame.PlayerName)
			{
				_ctx.BindCharacterSettings(liveFrame.PlayerName);
			}
			if (_loadedCharacter == liveFrame.PlayerName && (object)_ctx.Provider?.Profile.CastTiming != null)
			{
				_observedCastIds.Observe(liveFrame, _castObservationClock.NowMs);
			}
			_session.ObserveCharacter(liveFrame.PlayerName);
		}
		RefreshModeBadge();
		RefreshStatus();
		RefreshOverlay();
	}

	private void StopOverlay()
	{
		_overlayEnabled = false;
		_lastKnownPlayers.Reset();
		_ctx?.SetOverlayEnabled(enabled: false);
		_overlay?.Hide();
		if (_overlayButton != null)
		{
			_overlayButton.Text = "OVERLAY OFF";
		}
	}

	private void RefreshOverlay()
	{
		Rectangle bounds;
		LiveFrame frame;
		long observationNowMs;
		PlayerVillage[] hiddenVillages;
		int num;
		object obj;
		if (_overlayEnabled && Allowed(AppFeature.MapPlayers))
		{
			ReadOnlyGameStateProvider readOnlyGameStateProvider = _ctx?.Provider;
			if (readOnlyGameStateProvider != null && LiveInputController.IsGameForeground(readOnlyGameStateProvider.ProcessId))
			{
				Rectangle? rectangle = PlayerPositionOverlay.GameClientBounds(readOnlyGameStateProvider.ProcessId);
				if (rectangle.HasValue)
				{
					bounds = rectangle.GetValueOrDefault();
					frame = _ctx.Frame;
					if ((object)frame != null)
					{
						MapPlayersFrame mapPlayers = _ctx.MapPlayers;
						if ((object)mapPlayers != null)
						{
							if (mapPlayers.Map != frame.Map || mapPlayers.LocalId != frame.Snapshot.Player.Id)
							{
								_overlay?.ClearDots();
								return;
							}
							observationNowMs = _ctx.ObservationNowMs;
							PlayerVillage ownVillage = mapPlayers.LocalVillage;
							hiddenVillages = (from entry in _villageFilters
								where entry.Key != ownVillage && entry.Value.Checked
								select entry.Key).ToArray();
							object tag = _btnCastTurn.Tag;
							num = ((tag is bool && (bool)tag) ? 650 : 300);
							if (readOnlyGameStateProvider.Profile.Sha256 == "5F1A25276A4FF5AC42C58E55B491191ECD0240F1C8202190A57E199CA4F59634" && frame.Map == 190)
							{
								RenderViewObservation renderView = mapPlayers.RenderView;
								if ((object)renderView != null)
								{
									obj = mapPlayers with
									{
										RenderView = OverlayProjection.CalibratedMap190(frame.Snapshot.Player.PositionX, frame.Snapshot.Player.PositionY, mapPlayers.GridWidth.GetValueOrDefault(), mapPlayers.GridHeight.GetValueOrDefault(), renderView.SizeX, renderView.SizeY)
									};
									goto IL_025e;
								}
							}
							obj = mapPlayers;
							goto IL_025e;
						}
					}
					PlayerPositionOverlay? overlay = _overlay;
					if (overlay != null)
					{
						LiveFrame? frame2 = _ctx.Frame;
						overlay.MarkUnavailable(((object)frame2 != null && frame2.Cast?.Active == true) ? 2500 : 350);
					}
					return;
				}
			}
		}
		_overlay?.Hide();
		return;
		IL_025e:
		MapPlayersFrame displayList = (MapPlayersFrame)obj;
		if (!displayList.IsCurrent(frame, observationNowMs) || observationNowMs - displayList.CapturedAtMs > num || (object)displayList.RenderView == null)
		{
			PlayerPositionOverlay? overlay2 = _overlay;
			if (overlay2 != null)
			{
				CastObservation? cast = frame.Cast;
				overlay2.MarkUnavailable(((object)cast != null && cast.Active) ? 2500 : 350);
			}
			return;
		}
		(NearbyPlayerMarkers.Marker, PointF)[] array = (from p in _lastKnownPlayers.Update(displayList, frame, observationNowMs, _hideOwnVillage.Checked, (IReadOnlyCollection<PlayerVillage>?)(object)hiddenVillages, num)
			select (Marker: p, Point: OverlayProjection.Project(p, frame, displayList, bounds.Width, bounds.Height)) into p
			where p.Point.HasValue
			select (Marker: p.Marker, new PointF((float)p.Point.Value.X, (float)p.Point.Value.Y))).ToArray();
		if (array.Length == 0)
		{
			_overlay?.ClearDots();
			return;
		}
		if (_overlay == null)
		{
			_overlay = new PlayerPositionOverlay();
		}
		_overlay.UpdateDots(bounds, frame.Map, frame.Snapshot.Player.Id, array);
	}

	private void RefreshStatus()
	{
		GameSnapshot gameSnapshot = _ctx?.Snapshot();
		Label lblTarget = _lblTarget;
		TargetInfo targetInfo = gameSnapshot?.Target;
		lblTarget.Text = (((object)targetInfo != null) ? $"{targetInfo.Id} @ ({targetInfo.PositionX:F1}, {targetInfo.PositionY:F1}) {(targetInfo.IsAlive ? "vivo" : "morto / HP indisponível")}" : (((object)gameSnapshot == null) ? "Sem amostra recente" : "Nenhum alvo jogador válido — leitura do buff continua"));
		Label lblTarget2 = _lblTarget;
		TargetInfo targetInfo2 = gameSnapshot?.Target;
		lblTarget2.ForeColor = (((object)targetInfo2 != null && targetInfo2.IsAlive) ? Fg : Dim);
		LiveBuffObservation liveBuffObservation = _ctx?.BuffObservation ?? LiveBuffObservation.FromFrame(null, 0L);
		Label lblSubStatus = _lblSubStatus;
		lblSubStatus.Text = liveBuffObservation.Presence switch
		{
			BuffPresence.Active => string.Join(" | ", liveBuffObservation.ActiveSubs.Select((ObservedBuff b) => SubstitutionBuffs.Label(b.Name) + ": " + (b.RemainingSeconds?.ToString() ?? "?") + "s")),
			BuffPresence.Inactive => "INATIVA — ausente no array de buffs desta amostra",
			_ => "DESCONHECIDA — sem leitura recente, não equivale a inativa",
		};
		lblSubStatus = _lblSubStatus;
		lblSubStatus.ForeColor = liveBuffObservation.Presence switch
		{
			BuffPresence.Active => Good,
			BuffPresence.Unknown => Warn,
			_ => Dim,
		};
		ILivePunishEngine livePunishEngine = _ctx?.Punish;
		if (livePunishEngine != null)
		{
			Label lblFlowStatus = _lblFlowStatus;
			object tag = _btnSubPunish.Tag;
			lblFlowStatus.Text = ((!(tag is bool) || !(bool)tag) ? "Módulo OFF — observação do buff permanece ativa" : livePunishEngine.Status);
			lblSubStatus = _lblFlowStatus;
			Color foreColor;
			switch (livePunishEngine.State)
			{
			case EarlyPunishState.WatchingBuff:
				foreColor = Good;
				break;
			case EarlyPunishState.Executing:
				foreColor = ExecOrg;
				break;
			case EarlyPunishState.RemovalPending:
			case EarlyPunishState.Cancelled:
				foreColor = Warn;
				break;
			default:
				foreColor = Dim;
				break;
			}
			lblSubStatus.ForeColor = foreColor;
		}
		else
		{
			_lblFlowStatus.Text = "Desconectado";
		}
		string text = string.Join(" → ", _config.Live.PunishKeys);
		Label lblCooldown = _lblCooldown;
		LiveContext? ctx = _ctx;
		lblCooldown.Text = ((ctx != null && ctx.CounterAttackAuthorized) ? (text + " + GIRO SUB autorizados; somente Tai/Água; alvo conforme perfil") : ("DESAUTORIZADO — " + text));
		Label lblCooldown2 = _lblCooldown;
		LiveContext? ctx2 = _ctx;
		lblCooldown2.ForeColor = ((ctx2 != null && ctx2.CounterAttackAuthorized) ? Good : Warn);
		IReadOnlyList<LogEntry> readOnlyList = _ctx?.MemoryLog.Snapshot();
		Label lblLastEvent = _lblLastEvent;
		object text2;
		if (readOnlyList == null || readOnlyList.Count <= 0)
		{
			text2 = "—";
		}
		else
		{
			DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(1, 2);
			defaultInterpolatedStringHandler.AppendFormatted(readOnlyList[readOnlyList.Count - 1].At, "HH:mm:ss");
			defaultInterpolatedStringHandler.AppendLiteral(" ");
			defaultInterpolatedStringHandler.AppendFormatted(Truncate(readOnlyList[readOnlyList.Count - 1].Message, 90));
			text2 = defaultInterpolatedStringHandler.ToStringAndClear();
		}
		lblLastEvent.Text = (string)text2;
		_lblLastCounter.Text = _ctx?.ActionStatus ?? "Nenhuma tecla ofensiva enviada";
		_lblMetrics.Text = _ctx?.TurningSafetyStatus ?? "Desconectado";
		_lblPunishSafety.Text = _ctx?.PunishSafetyStatus ?? "Desconectado";
		_lblCombat.Text = _ctx?.CombatStatus ?? "Desconectado";
		_lblFollow.Text = _ctx?.FollowStatus ?? "Desconectado";
		_lblStamina.Text = _ctx?.StaminaStatus ?? "Desconectado";
		Label lblCastTurn = _lblCastTurn;
		object text3;
		if (Allowed(AppFeature.AutoCastTurn))
		{
			ReadOnlyGameStateProvider readOnlyGameStateProvider = _ctx?.Provider;
			if (readOnlyGameStateProvider != null)
			{
				GameMemoryProfile profile = readOnlyGameStateProvider.Profile;
				if ((object)profile != null && (object)profile.CastTiming == null)
				{
					text3 = "Cast não calibrado para este perfil do jogo";
					goto IL_04de;
				}
			}
			text3 = _ctx?.CastTurnStatus ?? "Desconectado";
		}
		else
		{
			text3 = "Recurso não autorizado pelo Firebase (AutoCastTurn)";
		}
		goto IL_04de;
		IL_04de:
		lblCastTurn.Text = (string)text3;
		_lblAutoTarget.Text = !Allowed(AppFeature.MapPlayers)
			? "Lista de jogadores não autorizada pelo Firebase (MapPlayers)"
			: !Allowed(AppFeature.AutoTarget) || _ctx?.Provider?.Profile is { ExternalProfile: true } targetProfile && !targetProfile.ValidatedInputKinds.Contains("TargetMemory", StringComparer.Ordinal)
				? "Somente leitura: " + (NearestTargetPolicy.Resolve(_ctx?.Frame, _config.AutoTarget.RadiusTiles, _config.AutoTarget.IgnoreSameVillage)?.Id ?? "nenhum candidato seguro") + " · input não autorizado"
				: _ctx?.AutoTargetStatus ?? "Desconectado";
		RefreshPlayers();
	}

	private static string Truncate(string s, int max)
	{
		if (s.Length > max)
		{
			return s.Substring(0, max) + "…";
		}
		return s;
	}

	private void OnLogEntry(LogEntry entry)
	{
		if (base.IsDisposed || base.Disposing)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			try
			{
				BeginInvoke(delegate
				{
					OnLogEntry(entry);
				});
				return;
			}
			catch (InvalidOperationException) when (base.IsDisposed || base.Disposing || !base.IsHandleCreated)
			{
				return;
			}
		}
		AppendLog(entry.Level, entry.Message, entry.Origin);
	}

	private void AppendLog(LogLevel level, string message, EventOrigin? origin)
	{
		Color selectionColor = origin switch
		{
			EventOrigin.Simulated => Dim,
			EventOrigin.Observed => Warn,
			EventOrigin.Confirmed => Good,
			EventOrigin.Requested => ReqBlue,
			EventOrigin.Executed => ExecOrg,
			_ => level switch
			{
				LogLevel.Error => Bad,
				LogLevel.Warning => Warn,
				LogLevel.Debug => Dim,
				_ => Fg,
			},
		};
		string value = origin?.ToString().ToUpperInvariant() ?? level.ToString().ToUpperInvariant();
		_rtbLog.SelectionStart = _rtbLog.TextLength;
		_rtbLog.SelectionLength = 0;
		_rtbLog.SelectionColor = selectionColor;
		_rtbLog.AppendText($"{DateTime.Now:HH:mm:ss} [{value,-10}] {message}{Environment.NewLine}");
		_rtbLog.SelectionColor = Fg;
		_rtbLog.ScrollToCaret();
	}
}
