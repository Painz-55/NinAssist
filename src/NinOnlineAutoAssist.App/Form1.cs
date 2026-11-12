using System.Drawing;
using NinOnlineAutoAssist.Core;
using NinOnlineAutoAssist.Core.Abstractions;
using NinOnlineAutoAssist.Core.Config;
using NinOnlineAutoAssist.Core.Events;
using NinOnlineAutoAssist.Core.Logging;
using NinOnlineAutoAssist.Core.Models;
using NinOnlineAutoAssist.Live;
using NinOnlineAutoAssist.Core.Security;

namespace NinOnlineAutoAssist.App;

/// <summary>
/// Nin Online AutoAssist — main window.
/// Live-only UI. Reading is independent from input authorization; modules start OFF.
/// Live mode uses a verified read-only profile and explicit held-key authorization.
/// Unmapped cooldowns/threats are not synthesized.
/// </summary>
public sealed class Form1 : Form
{
    private readonly FirebaseSession _session;
    private int _permissionsMask;
    public bool ReturnToLogin { get; private set; }
    private AppFeature SubFeature => AppFeature.SubPunishLegacy;
    private bool Allowed(AppFeature feature) => _session.Access.Allows(feature);
    private int PermissionsMask => Enum.GetValues<AppFeature>().Aggregate(0, (mask, feature) => Allowed(feature) ? mask | (1 << (int)feature) : mask);
    private Button _inspectionButton = null!;
    private Label _account = null!;
    // ── palette ─────────────────────────────────────────────
    private static readonly Color Bg       = Color.FromArgb(0x14, 0x16, 0x1A);
    private static readonly Color Panel    = Color.FromArgb(0x1E, 0x21, 0x26);
    private static readonly Color PanelAlt = Color.FromArgb(0x26, 0x29, 0x2F);
    private static readonly Color Fg       = Color.FromArgb(0xE8, 0xE8, 0xE8);
    private static readonly Color Dim      = Color.FromArgb(0x9A, 0xA0, 0xA6);
    private static readonly Color Accent   = Color.FromArgb(0x61, 0xAF, 0xEF);
    private static readonly Color Good     = Color.FromArgb(0x98, 0xC3, 0x79);
    private static readonly Color Warn     = Color.FromArgb(0xE5, 0xC0, 0x7B);
    private static readonly Color Bad      = Color.FromArgb(0xE0, 0x6C, 0x75);
    private static readonly Color ReqBlue  = Color.FromArgb(0x61, 0xAF, 0xEF);
    private static readonly Color ExecOrg  = Color.FromArgb(0xD1, 0x9A, 0x66);

    private AutoAssistConfig _config = null!;
    private LiveContext? _ctx;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 15 };
    private long _lastUiRefresh;
    private readonly SystemMonotonicClock _castObservationClock = new();
    private readonly ObservedCastIds _observedCastIds = new();

    private bool _emergencyKeyWasDown;

    // ── controls ────────────────────────────────────────────
    private Label _lblMode = null!, _lblTarget = null!, _lblSubStatus = null!, _lblFlowStatus = null!, _lblCooldown = null!;
    private Label _lblLastEvent = null!, _lblLastCounter = null!, _lblMetrics = null!, _lblPunishSafety = null!, _lblProvider = null!;
    private Button _btnAutoTurning = null!, _btnSubPunish = null!, _btnCombat = null!, _btnFollow = null!, _btnStamina = null!;
    private Label _lblCombat = null!, _lblFollow = null!, _lblStamina = null!;
    private TextBox _txtCharacter = null!;
    private CheckBox _autoCharacter = null!;
    private CheckBox _allowSequence = null!;
    private CheckBox _allowCombat = null!;
    private RichTextBox _rtbLog = null!;
    private DataGridView _playersGrid = null!;
    private Label _playersStatus = null!;
    private MapPlayersFrame? _shownPlayers;
    private MapPlayersPlot _playersPlot = null!;
    private int? _selectedMapSlot;
    private bool _refreshingPlayers;
    private CheckBox _hideOwnVillage = null!;
    private readonly Dictionary<PlayerVillage, CheckBox> _villageFilters = new();
    private int _shownVillageFilterMask = -1;
    private Button _btnCastTurn = null!;
    private Label _lblCastTurn = null!;

    public Form1(FirebaseSession session)
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _session = session;
        if (!session.Access.IsValid) throw new AccessDeniedException("Login obrigatório.");
        _permissionsMask = PermissionsMask;
        Text = "Nin Online AutoAssist — Turn / SubPunish / AA+Tool / Follow";
        BackColor = Bg;
        ForeColor = Fg;
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(1120, 780);
        Size = new Size(1280, 850);
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;

        BuildUi();
        LoadConfigAndContext();

        _timer.Tick += OnTick;
        _timer.Start();
        KeyDown += OnKeyDown;
        Shown += (_, _) => ConnectToGame();
        FormClosed += (_, _) =>
        {
            _session.Revoke();
            _timer.Stop(); _timer.Dispose();
            if (_ctx is not null) { _ctx.MemoryLog.EntryLogged -= OnLogEntry; _ctx.Dispose(); }
        };
    }

    // ═══════════════════════════════ UI construction ═══════════════════════════════

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = Bg, Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));   // header
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));   // module toggles
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 380));  // observed state + module safety
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));   // log
        Controls.Add(root);

        // ── header ──
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, BackColor = Bg };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        var title = new Label { Text = "NIN ONLINE AUTOASSIST", Font = new Font("Segoe UI Semibold", 17F, FontStyle.Bold), ForeColor = Fg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
        _lblMode = new Label { Text = "MODO: —", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold), BackColor = PanelAlt };
        var btnStop = new Button { Text = "PARADA DE EMERGÊNCIA (F12)", Dock = DockStyle.Fill, BackColor = Bad, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold), Margin = new Padding(6, 4, 0, 4) };
        btnStop.FlatAppearance.BorderSize = 0;
        btnStop.Click += (_, _) => EmergencyStop();
        header.Controls.Add(title, 0, 0); header.Controls.Add(_lblMode, 1, 0); header.Controls.Add(btnStop, 2, 0);
        root.Controls.Add(header, 0, 0);

        // ── module toggles ──
        var modules = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 8, BackColor = Bg, Padding = new Padding(0, 6, 0, 6) };
        for (var column = 0; column < 8; column++) modules.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 8));
        _btnAutoTurning = MakeModuleButton("AUTO TURNING", false);
        _btnAutoTurning.Click += (_, _) => ToggleAutoTurning();
        _btnCastTurn = MakeModuleButton("AUTO CAST TURN", false);
        _btnCastTurn.Click += (_, _) =>
        {
            var enabled = !(_btnCastTurn.Tag is true);
            _btnCastTurn.Tag = _ctx?.SetCastTurnEnabled(enabled) == true;
            RefreshModuleButtons();
        };
        var castMenu = new ContextMenuStrip();
        castMenu.Items.Add("Excluir jutsus do Cast Turn…", null, (_, _) => ConfigureCastTurn());
        _btnCastTurn.ContextMenuStrip = castMenu;
        _btnSubPunish = MakeModuleButton("SUBPUNISH LEGADO", false);
        _btnSubPunish.Click += (_, _) => ToggleSubPunish();
        _btnCombat = MakeModuleButton("AA + TOOL", false);
        _btnCombat.Click += (_, _) =>
        {
            var enabled = !(_btnCombat.Tag is true); _btnCombat.Tag = enabled;
            _ctx?.SetCombatEnabled(enabled); RefreshModuleButtons();
        };
        _btnFollow = MakeModuleButton("AUTO FOLLOW", false);
        _btnFollow.Click += (_, _) =>
        {
            var enabled = !(_btnFollow.Tag is true); _btnFollow.Tag = enabled;
            _ctx?.SetFollowEnabled(enabled); RefreshModuleButtons();
        };
        _btnStamina = MakeModuleButton("AUTO STAMINA", false);
        _btnStamina.Click += (_, _) =>
        {
            var enabled = !(_btnStamina.Tag is true); _btnStamina.Tag = enabled;
            _ctx?.SetStaminaEnabled(enabled); RefreshModuleButtons();
        };
        var hint = new Button { Text = "CONSULTAR RECURSOS DO JOGO\nSomente leitura · sem automação", ForeColor = Fg, BackColor = PanelAlt, Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat };
        _inspectionButton = hint;
        hint.Click += (_, _) => { if (Allowed(AppFeature.ResourceInspection)) new ResourceInspectionForm(_session.Access).Show(this); };
        _lblProvider = new Label { Text = "PROVEDOR: —", ForeColor = Dim, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(8, 0, 0, 0) };
        modules.Controls.Add(_btnAutoTurning, 0, 0); modules.Controls.Add(_btnCastTurn, 1, 0); modules.Controls.Add(_btnSubPunish, 2, 0);
        modules.Controls.Add(_btnCombat, 3, 0); modules.Controls.Add(_btnFollow, 4, 0);
        modules.Controls.Add(_btnStamina, 5, 0);
        modules.Controls.Add(hint, 6, 0); modules.Controls.Add(_lblProvider, 7, 0);
        root.Controls.Add(modules, 0, 1);

        // ── observed state + connection ──
        var mid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Bg };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.Controls.Add(mid, 0, 2);

        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, BackColor = Panel, Padding = new Padding(10) };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        _lblTarget      = AddStatusRow(status, 0, "STATUS DO ALVO");
        _lblSubStatus   = AddStatusRow(status, 1, "BUFF SUB — MEMÓRIA");
        _lblFlowStatus  = AddStatusRow(status, 2, "FLUXO SUBPUNISH");
        _lblCooldown    = AddStatusRow(status, 3, "AUTORIZAÇÃO DO COMBO");
        _lblLastEvent   = AddStatusRow(status, 4, "ÚLTIMO EVENTO");
        _lblLastCounter = AddStatusRow(status, 5, "ÚLTIMA AÇÃO");
        _lblMetrics     = AddStatusRow(status, 6, "TURN — TECLA / SUB");
        _lblPunishSafety = AddStatusRow(status, 7, "PUNISH — SEM TECLA");
        _lblCombat = AddStatusRow(status, 8, "AA MELEE / FAN + TOOL");
        _lblFollow = AddStatusRow(status, 9, "FOLLOW — TECLA SEGURADA");
        _lblStamina = AddStatusRow(status, 10, "STAMINA — LIMITES CONFIGURÁVEIS");
        _lblCastTurn = AddStatusRow(status, 11, "AUTO CAST TURN — FINAL DO CAST");
        mid.Controls.Add(status, 0, 0);

        var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7, BackColor = Panel, Padding = new Padding(10) };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _autoCharacter = new CheckBox { Text = "DETECTAR NICK AUTOMATICAMENTE — jogador local", Checked = true, Dock = DockStyle.Fill, ForeColor = Dim };
        rightPanel.Controls.Add(_autoCharacter, 0, 0);
        _txtCharacter = new TextBox { Dock = DockStyle.Fill, BackColor = PanelAlt, ForeColor = Fg, ReadOnly = true, PlaceholderText = "Aguardando personagem no mapa…" };
        _autoCharacter.CheckedChanged += (_, _) =>
        {
            _txtCharacter.ReadOnly = _autoCharacter.Checked;
            _txtCharacter.Text = _autoCharacter.Checked ? "" : _config?.Live.CharacterName ?? "";
        };
        rightPanel.Controls.Add(_txtCharacter, 0, 1);
        var connectionButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var connect = new Button { Text = "CONECTAR / RECONECTAR", AutoSize = true, BackColor = Accent, ForeColor = Color.Black, FlatStyle = FlatStyle.Flat };
        connect.Click += (_, _) => ConnectToGame();
        var disconnect = new Button { Text = "DESCONECTAR", AutoSize = true, BackColor = PanelAlt, ForeColor = Fg, FlatStyle = FlatStyle.Flat };
        disconnect.Click += (_, _) => { DisconnectFromGame(); RefreshStatus(); };
        var logout = new Button { Text = "SAIR DA CONTA", AutoSize = true };
        logout.Click += (_, _) => { _session.Revoke(); EmergencyStop(); ReturnToLogin = true; Close(); };
        _account = new Label { AutoSize = true, Text = _session.Email, ForeColor = Dim };
        connectionButtons.AutoScroll = true;
        connectionButtons.Controls.AddRange(new Control[] { connect, disconnect, logout, _account });
        rightPanel.Controls.Add(connectionButtons, 0, 2);
        _allowSequence = new CheckBox { Text = "AUTORIZAR A → Q → S por remoção antecipada (HEURÍSTICA)", Dock = DockStyle.Fill, ForeColor = Warn, Checked = false };
        _allowSequence.CheckedChanged += (_, _) =>
        {
            _ctx?.SetCounterAttackAuthorized(_allowSequence.Checked);
            RefreshStatus();
        };
        rightPanel.Controls.Add(_allowSequence, 0, 3);
        _allowCombat = new CheckBox { Text = "AUTORIZAR AA + TOOL automáticos (sem tecla segurada)", Dock = DockStyle.Fill, ForeColor = Warn };
        _allowCombat.CheckedChanged += (_, _) => { _ctx?.SetCombatAuthorized(_allowCombat.Checked); RefreshStatus(); };
        rightPanel.Controls.Add(_allowCombat, 0, 4);
        rightPanel.Controls.Add(new Label { Text = "SUB: sem giro durante buff; giro preparatório antes das teclas do combo HEURÍSTICO.\nAA: Melee radial ou Fan reto com largura própria e giro antes do AA; Hisuy indisponível.\nFOLLOW: ON + tecla segurada + caminho conhecido. F12 desliga tudo.\nFeche o chat; acertos/cooldowns/armadilhas invisíveis não são garantidos.", Dock = DockStyle.Fill, ForeColor = Dim }, 0, 5);
        var settings = new Button { Text = "TECLAS / TIMERS SUB", Width = 210, Height = 32, BackColor = PanelAlt, ForeColor = Fg };
        settings.Click += (_, _) => ConfigureKeys();
        var combatSettings = new Button { Text = "AA / TOOL / FOLLOW / STAMINA", Width = 240, Height = 32, BackColor = PanelAlt, ForeColor = Fg };
        combatSettings.Click += (_, _) => ConfigureCombat();
        var castSettings = new Button { Text = "EXCLUIR JUTSUS CAST TURN", Width = 210, Height = 32, BackColor = PanelAlt, ForeColor = Fg };
        castSettings.Click += (_, _) => ConfigureCastTurn();
        var settingsButtons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        settingsButtons.Controls.AddRange(new Control[] { settings, combatSettings, castSettings });
        rightPanel.Controls.Add(settingsButtons, 0, 6);
        mid.Controls.Add(rightPanel, 1, 0);

        // ── log ──
        _rtbLog = new RichTextBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(0x0D, 0x0F, 0x13), ForeColor = Fg, ReadOnly = true, Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.None, HideSelection = false };
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var playersTab = new TabPage("PLAYERS NO MAPA") { BackColor = Panel };
        var logTab = new TabPage("LOG") { BackColor = Panel };
        logTab.Controls.Add(_rtbLog);
        _playersGrid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            AllowUserToDeleteRows = false, RowHeadersVisible = false, MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            BackgroundColor = Panel, ForeColor = Fg, EnableHeadersVisualStyles = false };
        _playersGrid.DefaultCellStyle.BackColor = Panel;
        _playersGrid.DefaultCellStyle.ForeColor = Fg;
        _playersGrid.ColumnHeadersDefaultCellStyle.BackColor = PanelAlt;
        _playersGrid.ColumnHeadersDefaultCellStyle.ForeColor = Fg;
        _playersGrid.SelectionChanged += (_, _) =>
        {
            if (_refreshingPlayers) return;
            _selectedMapSlot = _playersGrid.SelectedRows.Count == 1 && _playersGrid.SelectedRows[0].Tag is int slot
                ? slot : null;
            _playersPlot.SelectedRowSlot = _selectedMapSlot;
            _playersPlot.Invalidate();
        };
        foreach (var caption in new[] { "#", "Slot", "Nome", "Vila", "Posição (tiles)", "Distância", "ΔX", "ΔY", "Você / Alvo", "Visibilidade", "Transformação" })
            _playersGrid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = caption, SortMode = DataGridViewColumnSortMode.NotSortable });
        var columnWidths = new[] { 40, 48, 150, 85, 105, 75, 65, 65, 90, 110, 110 };
        for (var i = 0; i < columnWidths.Length; i++) _playersGrid.Columns[i].Width = columnWidths[i];
        _playersStatus = new Label { Dock = DockStyle.Top, Height = 44, ForeColor = Warn,
            Text = "Aguardando lista local do mapa.\nVilas: Mist azul · Sand amarelo · Leaf verde · Renegado vermelho · Desconhecida neutro." };
        _hideOwnVillage = new CheckBox { Text = "Ocultar minha vila", AutoSize = true,
            ForeColor = Fg, Checked = false };
        _hideOwnVillage.CheckedChanged += (_, _) => RefreshPlayers();
        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 34, WrapContents = false, AutoScroll = true };
        filters.Controls.Add(_hideOwnVillage);
        foreach (var village in new[] { PlayerVillage.Leaf, PlayerVillage.Sand, PlayerVillage.Mist, PlayerVillage.Renegade })
        {
            var box = new CheckBox { Text = $"Ocultar {PlayerVillages.Label(village)}", AutoSize = true,
                ForeColor = Fg, Margin = new Padding(12, 3, 0, 3) };
            box.CheckedChanged += (_, _) => RefreshPlayers();
            _villageFilters.Add(village, box);
            filters.Controls.Add(box);
        }
        _playersPlot = new MapPlayersPlot();
        var mapBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        mapBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        mapBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        mapBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        mapBody.Controls.Add(_playersGrid, 0, 0);
        mapBody.Controls.Add(_playersPlot, 1, 0);
        playersTab.Controls.Add(mapBody); playersTab.Controls.Add(_playersStatus); playersTab.Controls.Add(filters);
        tabs.TabPages.Add(playersTab); tabs.TabPages.Add(logTab);
        root.Controls.Add(tabs, 0, 3);
    }

    private void RefreshPlayers()
    {
        // The UI timer blinks a table-selected row between 200 ms map-list reads.
        var highlightOn = _playersPlot.HasSelectedRow && (Environment.TickCount64 / 350) % 2 == 0;
        if (_playersPlot.SelectedHighlightOn != highlightOn)
        {
            _playersPlot.SelectedHighlightOn = highlightOn;
            _playersPlot.Invalidate();
        }
        var list = _ctx?.MapPlayers;
        var hideOwnVillage = _hideOwnVillage.Checked;
        var own = list?.LocalVillage ?? PlayerVillage.Unknown;
        foreach (var (village, box) in _villageFilters)
        {
            box.Visible = village != own;
            box.Enabled = village != own;
        }
        var hidden = _villageFilters.Where(entry => entry.Key != own && entry.Value.Checked)
            .Select(entry => entry.Key).ToArray();
        var filterMask = (hideOwnVillage ? 1 : 0) | hidden.Aggregate(0, (mask, village) => mask | (1 << (int)village));
        _playersStatus.Text = list is null
            ? "Lista indisponível/antiga — aguardando leitura válida. " + (_ctx?.MapPlayersStatus ?? "Desconectado")
            : hideOwnVillage && !PlayerVillages.IsKnown(list.LocalVillage)
                ? "Filtro ativo: sua vila ainda é desconhecida; aguardando leitura válida.\nNenhum personagem será classificado como de outra vila sem essa informação."
                : (_ctx?.MapPlayersStatus ?? "") + (hideOwnVillage || hidden.Length > 0
                    ? "\nFiltros apenas visuais; não alteram automações. Estados de invisibilidade/transformação não mapeados."
                    : "\nSem filtro de visibilidade/aparência. Estados de invisibilidade/transformação ainda não mapeados; não garante todos os jogadores do servidor.");
        if (ReferenceEquals(list, _shownPlayers) && filterMask == _shownVillageFilterMask) return;
        _shownPlayers = list;
        _shownVillageFilterMask = filterMask;
        if (list is null)
        {
            _refreshingPlayers = true;
            try { _playersGrid.Rows.Clear(); }
            finally { _refreshingPlayers = false; }
            _selectedMapSlot = _playersPlot.SelectedRowSlot = null;
            _playersPlot.Offsets = []; _playersPlot.Invalidate(); return;
        }
        var numberedPlayers = list.NumberedForDisplay(hideOwnVillage, hidden);
        var visiblePlayers = numberedPlayers.Select(entry => entry.Player).ToArray();
        var player = _ctx?.Frame?.Snapshot.Player;
        _playersPlot.Offsets = player is null ? [] : visiblePlayers.Where(p => p.X.HasValue && p.Y.HasValue)
            .Select(p => (p.Slot, p.X!.Value - player.PositionX, p.Y!.Value - player.PositionY, p.IsSelected)).ToArray();
        if (!visiblePlayers.Any(p => p.Slot == _selectedMapSlot)) _selectedMapSlot = null;
        _playersPlot.SelectedRowSlot = _selectedMapSlot;
        _playersPlot.Invalidate();
        var sameSlots = _playersGrid.Rows.Count == visiblePlayers.Length && visiblePlayers.Select((p, i) =>
            Equals(_playersGrid.Rows[i].Tag, p.Slot)).All(v => v);
        _refreshingPlayers = true;
        try
        {
            if (!sameSlots) _playersGrid.Rows.Clear();
            for (var i = 0; i < visiblePlayers.Length; i++)
            {
                var p = visiblePlayers[i];
                var position = p.X is { } x && p.Y is { } y ? $"{x:0.0}, {y:0.0}" : "Desconhecida";
                var distance = player is not null && p.X is { } px && p.Y is { } py
                    ? Math.Sqrt(Math.Pow(px - player.PositionX, 2) + Math.Pow(py - player.PositionY, 2)).ToString("0.0") : "?";
                var dx = player is not null && p.X is { } vx ? (vx - player.PositionX).ToString("+0.0;-0.0;0.0") : "?";
                var dy = player is not null && p.Y is { } vy ? (vy - player.PositionY).ToString("+0.0;-0.0;0.0") : "?";
                object[] values = { numberedPlayers[i].Number, p.Slot, p.Name, PlayerVillages.Label(p.Village), position, distance, dx, dy,
                    p.IsLocal ? "VOCÊ" : p.IsSelected ? "ALVO" : "—", "Não mapeada", "Não mapeada" };
                if (sameSlots) _playersGrid.Rows[i].SetValues(values);
                else { var row = _playersGrid.Rows.Add(values); _playersGrid.Rows[row].Tag = p.Slot; }
                var color = PlayerVillages.ColorArgb(p.Village) is { } argb ? Color.FromArgb(argb) : Fg;
                foreach (var column in new[] { 2, 3 })
                {
                    var style = _playersGrid.Rows[i].Cells[column].Style;
                    style.ForeColor = style.SelectionForeColor = color;
                    style.SelectionBackColor = PanelAlt;
                }
            }
            if (!sameSlots)
            {
                _playersGrid.ClearSelection();
                if (_selectedMapSlot is { } selectedSlot)
                    foreach (DataGridViewRow row in _playersGrid.Rows)
                        if (Equals(row.Tag, selectedSlot)) { row.Selected = true; break; }
            }
        }
        finally { _refreshingPlayers = false; }
    }

    private Control BuildLegend()
    {
        var legend = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(0, 4, 0, 0) };
        legend.Controls.Add(new Label { Text = "PROVENIÊNCIA DOS EVENTOS", ForeColor = Dim, AutoSize = true, Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 2) });
        legend.Controls.Add(LegendItem("● OBSERVADO  — lido, não confirmado", Warn));
        legend.Controls.Add(LegendItem("● CONFIRMADO — evidência suficiente", Good));
        legend.Controls.Add(LegendItem("● SOLICITADO — pedido enviado", ReqBlue));
        legend.Controls.Add(LegendItem("● EXECUTADO  — input disparado (acerto?)", ExecOrg));
        return legend;
    }

    private static Label LegendItem(string text, Color color) =>
        new() { Text = text, ForeColor = color, AutoSize = true, Font = new Font("Segoe UI", 8F), Margin = new Padding(0) };

    private static Button MakeModuleButton(string title, bool on) => new()
    {
        Text = $"{title}\nOFF",
        Dock = DockStyle.Fill,
        Margin = new Padding(0, 0, 8, 0),
        BackColor = PanelAlt,
        ForeColor = Fg,
        FlatStyle = FlatStyle.Flat,
        Font = new Font("Segoe UI Semibold", 12F, FontStyle.Bold),
        Tag = on
    };

    private Label AddStatusRow(TableLayoutPanel grid, int row, string caption)
    {
        if (grid.RowStyles.Count <= row) grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / 12f));
        var cap = new Label { Text = caption, ForeColor = Dim, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold) };
        var val = new Label { Text = "—", ForeColor = Fg, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };
        grid.Controls.Add(cap, 0, row);
        grid.Controls.Add(val, 1, row);
        return val;
    }

    // ═══════════════════════════════ config / context ═══════════════════════════════

    private static string ConfigPath => AppPaths.Settings.ConfigPath;
    private string? _loadedCharacter;
    private string SettingsPath => _loadedCharacter is null
        ? throw new InvalidOperationException("Aguarde a identidade local verificada antes de salvar.")
        : AppPaths.Settings.CharacterConfigPath(_loadedCharacter);

    private void LoadConfigAndContext()
    {
        AppPaths.Settings.EnsureConfig();
        _config = ConfigLoader.Load(ConfigPath);
        _txtCharacter.Text = _autoCharacter.Checked ? "" : _config.Live.CharacterName;
        _allowSequence.Text = $"AUTORIZAR {string.Join(" → ", _config.Live.PunishKeys)} + GIRO ANTES DO COMBO";
        _allowSequence.Checked = false; // Offensive permission is explicit for each app session.
        AppendLog(LogLevel.Info, "AO VIVO: módulos OFF. Sub não gira durante buff; prepara direção antes de cada tecla do combo. Turn comum exige tecla. Remoção antecipada é HEURÍSTICA.", null);
        RefreshModuleButtons();
        RefreshModeBadge();
        RefreshStatus();
    }

    private void RefreshModeBadge()
    {
        _lblMode.Text = _ctx?.Provider is null ? "AO VIVO · DESCONECTADO"
            : $"TURN: {string.Join(" / ", _config.Live.TurnActivationKeys)}";
        _lblMode.ForeColor = _ctx?.Frame is null ? Warn : Good;
        _lblProvider.Text = _ctx?.Status ?? "Desconectado — leitura indisponível";
        var connected = _ctx?.Provider is not null && _session.Access.IsValid;
        var profile = _ctx?.Provider?.Profile;
        bool Approved(LiveInputKind kind) => profile is { ActuationValidated: true } &&
            (!profile.ExternalProfile || profile.ValidatedInputKinds.Contains(kind.ToString(), StringComparer.Ordinal));
        var attack = Allowed(AppFeature.AutoAttack) && profile?.ApprovesAttackMode(_config.CombatAssist.AttackMode.ToString()) == true &&
            (_config.CombatAssist.AttackMode != AutoAttackMode.Fan ||
             profile.ValidatedInputKinds.Contains(nameof(LiveInputKind.Turning), StringComparer.Ordinal));
        var tool = Allowed(AppFeature.Tool) && Approved(LiveInputKind.Tool);
        _btnAutoTurning.Enabled = connected && Allowed(AppFeature.AutoTurning) && Approved(LiveInputKind.Turning);
        _btnSubPunish.Enabled = connected && Allowed(SubFeature) && Approved(LiveInputKind.Punish);
        _allowSequence.Enabled = _btnSubPunish.Enabled && profile?.PunishComboValidated != false;
        _btnCombat.Enabled = _allowCombat.Enabled = connected && (attack || tool);
        _btnFollow.Enabled = connected && Allowed(AppFeature.Follow) && Approved(LiveInputKind.Follow);
        _btnStamina.Enabled = connected && Allowed(AppFeature.Stamina) && Approved(LiveInputKind.Stamina);
        _btnCastTurn.Enabled = connected && Allowed(AppFeature.AutoCastTurn) && Approved(LiveInputKind.CastTurning) && profile?.CastTiming is not null;
        _inspectionButton.Enabled = Allowed(AppFeature.ResourceInspection);
        _account.Text = $"{_session.Email} · {_session.Status}";
    }

    private void DisconnectFromGame()
    {
        _observedCastIds.Reset();
        if (_ctx is not null)
        {
            _ctx.SetAutoTurningEnabled(false);
            _ctx.SetSubPunishEnabled(false);
            _ctx.SetCombatEnabled(false); _ctx.SetCombatAuthorized(false); _ctx.SetFollowEnabled(false);
            _ctx.SetStaminaEnabled(false);
            _ctx.SetCastTurnEnabled(false);
            _ctx.MemoryLog.EntryLogged -= OnLogEntry;
            _ctx.Dispose();
            _ctx = null;
        }
        _btnAutoTurning.Tag = _btnSubPunish.Tag = _btnCombat.Tag = _btnFollow.Tag = _btnStamina.Tag = false;
        _btnCastTurn.Tag = false;
        RefreshModuleButtons();
        RefreshModeBadge();
    }

    private void ConnectToGame()
    {
        if (!_session.Access.IsValid) return;
        if (!_autoCharacter.Checked && string.IsNullOrWhiteSpace(_txtCharacter.Text))
        {
            AppendLog(LogLevel.Warning, "Informe o nome exato do personagem para conectar.", null);
            return;
        }
        DisconnectFromGame();
        _config = _config with { Live = _config.Live with { CharacterName = _autoCharacter.Checked ? _config.Live.CharacterName : _txtCharacter.Text.Trim(),
            AllowCounterAttackSequence = _allowSequence.Checked } };
        _ctx = new LiveContext(_config, _session.Access, _autoCharacter.Checked);
        _ctx.SetCombatAuthorized(_allowCombat.Checked);
        _ctx.MemoryLog.EntryLogged += OnLogEntry;
        foreach (var entry in _ctx.MemoryLog.Snapshot()) OnLogEntry(entry);
        RefreshModeBadge();
        RefreshStatus();
    }

    private void ConfigureKeys()
    {
        // Editing always disarms first; closing/cancelling never resumes an old trigger.
        EmergencyStop();
        using var dialog = new KeySettingsForm(_config.Live);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var updated = _config with { Live = dialog.Settings };
        try { ConfigLoader.Save(SettingsPath, updated); }
        catch (Exception ex) { AppendLog(LogLevel.Error, $"Não foi possível salvar: {ex.Message}", null); return; }
        _config = updated;
        _allowSequence.Text = $"AUTORIZAR {string.Join(" → ", updated.Live.PunishKeys)} + GIRO ANTES DO COMBO";
        ConnectToGame();
        AppendLog(LogLevel.Info, "Teclas salvas/aplicadas; módulos OFF e autorização revogada.", EventOrigin.Requested);
    }

    private void ConfigureCombat()
    {
        EmergencyStop();
        using var dialog = new CombatAssistForm(_config.CombatAssist);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var updated = _config with { CombatAssist = dialog.Settings,
            Live = _config.Live with { AllowCounterAttackSequence = false } };
        try { ConfigLoader.Save(SettingsPath, updated); }
        catch (Exception ex) { AppendLog(LogLevel.Error, $"Não foi possível salvar: {ex.Message}", null); return; }
        _config = updated; ConnectToGame();
        AppendLog(LogLevel.Info, "AA/Tool/Follow salvos; módulos OFF e permissões revogadas.", EventOrigin.Requested);
    }

    private void ConfigureCastTurn()
    {
        EmergencyStop();
        using var dialog = new AutoCastTurnForm(_config.AutoCastTurn, () => _observedCastIds.Recent);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var updated = _config with { AutoCastTurn = dialog.Settings };
        try { ConfigLoader.Save(SettingsPath, updated); }
        catch (Exception ex) { AppendLog(LogLevel.Error, $"Não foi possível salvar: {ex.Message}", null); return; }
        _config = updated; ConnectToGame();
    }

    // ═══════════════════════════════ toggles & stop ═══════════════════════════════

    private void ToggleAutoTurning()
    {
        bool on = !(_btnAutoTurning.Tag is bool b && b);
        _btnAutoTurning.Tag = on;
        _ctx?.SetAutoTurningEnabled(on);
        AppendLog(LogLevel.Info, $"Auto Turning {(on ? "ATIVADO" : "DESATIVADO")} pelo usuário", EventOrigin.Requested);
        RefreshModuleButtons();
    }

    private void ToggleSubPunish()
    {
        bool on = !(_btnSubPunish.Tag is bool b && b);
        _btnSubPunish.Tag = on;
        _ctx?.SetSubPunishEnabled(on);
        AppendLog(LogLevel.Info, $"SubPunish {(on ? "ATIVADO" : "DESATIVADO")} pelo usuário", EventOrigin.Requested);
        RefreshModuleButtons();
    }

    private void RefreshModuleButtons()
    {
        RefreshModuleButton(_btnAutoTurning, "AUTO TURNING");
        RefreshModuleButton(_btnCastTurn, "AUTO CAST TURN");
        RefreshModuleButton(_btnSubPunish, "SUBPUNISH LEGADO");
        var profile = _ctx?.Provider?.Profile;
        var attackApproved = Allowed(AppFeature.AutoAttack) && profile is { ActuationValidated: true } &&
            profile.ApprovesAttackMode(_config.CombatAssist.AttackMode.ToString());
        var toolApproved = Allowed(AppFeature.Tool) && profile is { ActuationValidated: true } &&
            (!profile.ExternalProfile || profile.ValidatedInputKinds.Contains(nameof(LiveInputKind.Tool), StringComparer.Ordinal));
        RefreshModuleButton(_btnCombat, attackApproved && toolApproved ? "AA + TOOL"
            : attackApproved ? "AA (TOOL BLOQUEADO)" : toolApproved ? "TOOL (AA BLOQUEADO)" : "AA / TOOL BLOQUEADOS");
        RefreshModuleButton(_btnFollow, "AUTO FOLLOW");
        RefreshModuleButton(_btnStamina, "AUTO STAMINA");
    }

    private static void RefreshModuleButton(Button btn, string title)
    {
        bool on = btn.Tag is bool b && b;
        btn.Text = $"{title}\n{(on ? "ON" : "OFF")}";
        btn.BackColor = on ? Color.FromArgb(0x24, 0x3A, 0x2A) : PanelAlt;
        btn.ForeColor = on ? Good : Fg;
    }

    private void EmergencyStop()
    {
        _ctx?.SetAutoTurningEnabled(false);
        _ctx?.SetSubPunishEnabled(false);
        _ctx?.SetCombatEnabled(false); _ctx?.SetCombatAuthorized(false); _ctx?.SetFollowEnabled(false);
        _ctx?.SetStaminaEnabled(false);
        _ctx?.SetCastTurnEnabled(false);
        _btnAutoTurning.Tag = false;
        _btnSubPunish.Tag = false;
        _btnCombat.Tag = _btnFollow.Tag = _btnStamina.Tag = false;
        _btnCastTurn.Tag = false;
        _allowSequence.Checked = false;
        _allowCombat.Checked = false;
        LiveInputController.AcknowledgeEmergencyAfterDisarm();
        RefreshModuleButtons();
        AppendLog(LogLevel.Warning, "PARADA DE EMERGÊNCIA acionada — módulos desativados", EventOrigin.Requested);
        System.Media.SystemSounds.Exclamation.Play();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var key = _config.General.EmergencyStopKey;
        if (e.KeyCode.ToString().Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            e.Handled = true;
            EmergencyStop();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var down = LiveInputController.IsDown((int)Keys.F12);
        var pressed = LiveInputController.ConsumeEmergency();
        if ((pressed && !down) || (down && !_emergencyKeyWasDown)) EmergencyStop();
        _emergencyKeyWasDown = down;
        if (Environment.TickCount64 - _lastUiRefresh < 100) return;
        _lastUiRefresh = Environment.TickCount64;
        if (!_session.Access.IsValid)
        {
            _timer.Stop(); // MessageBox has its own message loop: do not re-enter this expiry branch.
            _session.Revoke(); EmergencyStop();
            foreach (var child in OwnedForms) child.Close();
            ReturnToLogin = true;
            MessageBox.Show(this, "A sessão perdeu a autorização ou a conexão. Entre novamente para continuar.", "Sessão encerrada");
            Close(); return;
        }
        var mask = PermissionsMask;
        if (mask != _permissionsMask)
        {
            _permissionsMask = mask; EmergencyStop();
            if (!Allowed(AppFeature.ResourceInspection)) foreach (var child in OwnedForms.OfType<ResourceInspectionForm>()) child.Close();
            AppendLog(LogLevel.Warning, "Permissões atualizadas pelo administrador; módulos desarmados.", null);
        }
        if (_ctx?.Frame is { } observed)
        {
            if (_loadedCharacter != observed.PlayerName && !string.IsNullOrWhiteSpace(observed.PlayerName))
            {
                _observedCastIds.Reset();
                EmergencyStop();
                try
                {
                    var path = AppPaths.Settings.EnsureCharacterConfig(observed.PlayerName);
                    var settings = ConfigLoader.Load(path);
                    _loadedCharacter = observed.PlayerName;
                    _txtCharacter.Text = observed.PlayerName;
                    _config = settings with { Live = settings.Live with {
                        CharacterName = observed.PlayerName, AllowCounterAttackSequence = false } };
                    _allowSequence.Checked = _allowCombat.Checked = false;
                    _allowSequence.Text = $"AUTORIZAR {string.Join(" → ", _config.Live.PunishKeys)} + GIRO ANTES DO COMBO";
                    AppendLog(LogLevel.Info, $"Configurações de {observed.PlayerName} carregadas; módulos e consentimento OFF.", null);
                    ConnectToGame(); // rebuild engines with the verified character's settings
                }
                catch (Exception ex)
                {
                    DisconnectFromGame();
                    AppendLog(LogLevel.Error, $"Configuração por personagem indisponível; conexão desarmada: {ex.Message}", null);
                }
                return;
            }
            if (_autoCharacter.Checked && _txtCharacter.Text != observed.PlayerName) _txtCharacter.Text = observed.PlayerName;
            if (_loadedCharacter == observed.PlayerName) _ctx.BindCharacterSettings(observed.PlayerName);
            if (_loadedCharacter == observed.PlayerName && _ctx.Provider?.Profile.CastTiming is not null)
                _observedCastIds.Observe(observed, _castObservationClock.NowMs);
            _session.ObserveCharacter(observed.PlayerName);
        }
        RefreshModeBadge();
        RefreshStatus();
    }

    // ═══════════════════════════════ status & log ═══════════════════════════════

    private void RefreshStatus()
    {
        var snap = _ctx?.Snapshot();
        _lblTarget.Text = snap?.Target is { } target
            ? $"{target.Id} @ ({target.PositionX:F1}, {target.PositionY:F1}) {(target.IsAlive ? "vivo" : "morto / HP indisponível")}"
            : snap is null ? "Sem amostra recente" : "Nenhum alvo jogador válido — leitura do buff continua";
        _lblTarget.ForeColor = snap?.Target is { IsAlive: true } ? Fg : Dim;

        var observation = _ctx?.BuffObservation ?? LiveBuffObservation.FromFrame(null, 0);
        _lblSubStatus.Text = observation.Presence switch
        {
            BuffPresence.Active => string.Join(" | ", observation.ActiveSubs.Select(b =>
                $"{SubstitutionBuffs.Label(b.Name)}: {b.RemainingSeconds?.ToString() ?? "?"}s")),
            BuffPresence.Inactive => "INATIVA — ausente no array de buffs desta amostra",
            _ => "DESCONHECIDA — sem leitura recente, não equivale a inativa"
        };
        _lblSubStatus.ForeColor = observation.Presence switch { BuffPresence.Active => Good, BuffPresence.Unknown => Warn, _ => Dim };
        if (_ctx?.Punish is { } punish)
        {
            _lblFlowStatus.Text = !(_btnSubPunish.Tag is true) ? "Módulo OFF — observação do buff permanece ativa"
                : punish.Status;
            _lblFlowStatus.ForeColor = punish.State switch
            {
                EarlyPunishState.WatchingBuff => Good, EarlyPunishState.Executing => ExecOrg,
                EarlyPunishState.RemovalPending or EarlyPunishState.Cancelled => Warn, _ => Dim
            };
        }
        else _lblFlowStatus.Text = "Desconectado";
        var sequence = string.Join(" → ", _config.Live.PunishKeys);
        _lblCooldown.Text = _ctx?.CounterAttackAuthorized == true ? $"{sequence} + GIRO SUB autorizados; combo sem alvo"
            : $"DESAUTORIZADO — {sequence}";
        _lblCooldown.ForeColor = _ctx?.CounterAttackAuthorized == true ? Good : Warn;
        var entries = _ctx?.MemoryLog.Snapshot();
        _lblLastEvent.Text = entries is { Count: > 0 } ? $"{entries[^1].At:HH:mm:ss} {Truncate(entries[^1].Message, 90)}" : "—";
        _lblLastCounter.Text = _ctx?.ActionStatus ?? "Nenhuma tecla ofensiva enviada";
        _lblMetrics.Text = _ctx?.TurningSafetyStatus ?? "Desconectado";
        _lblPunishSafety.Text = _ctx?.PunishSafetyStatus ?? "Desconectado";
        _lblCombat.Text = _ctx?.CombatStatus ?? "Desconectado";
        _lblFollow.Text = _ctx?.FollowStatus ?? "Desconectado";
        _lblStamina.Text = _ctx?.StaminaStatus ?? "Desconectado";
        _lblCastTurn.Text = !Allowed(AppFeature.AutoCastTurn) ? "Recurso não autorizado pelo Firebase (AutoCastTurn)"
            : _ctx?.Provider is { Profile.CastTiming: null } ? "Cast não calibrado para este perfil do jogo"
            : _ctx?.CastTurnStatus ?? "Desconectado";
        RefreshPlayers();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    private void OnLogEntry(LogEntry entry)
    {
        if (IsDisposed || Disposing) return;
        if (InvokeRequired)
        {
            try { BeginInvoke(() => OnLogEntry(entry)); }
            catch (InvalidOperationException) when (IsDisposed || Disposing || !IsHandleCreated) { }
            return;
        }
        AppendLog(entry.Level, entry.Message, entry.Origin);
    }

    private void AppendLog(LogLevel level, string message, EventOrigin? origin)
    {
        var color = origin switch
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
                _ => Fg
            }
        };
        var originTag = origin?.ToString().ToUpperInvariant() ?? level.ToString().ToUpperInvariant();
        _rtbLog.SelectionStart = _rtbLog.TextLength;
        _rtbLog.SelectionLength = 0;
        _rtbLog.SelectionColor = color;
        _rtbLog.AppendText($"{DateTime.Now:HH:mm:ss} [{originTag,-10}] {message}{Environment.NewLine}");
        _rtbLog.SelectionColor = Fg;
        _rtbLog.ScrollToCaret();
    }
}
