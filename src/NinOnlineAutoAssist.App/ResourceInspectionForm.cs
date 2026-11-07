using System.Diagnostics;
using System.Text.Json;
using NinOnlineAutoAssist.Diagnostics;
using NinOnlineAutoAssist.Core.Security;

namespace NinOnlineAutoAssist.App;

/// <summary>Independent diagnostic view; never supplies guessed state to combat engines.</summary>
public sealed class ResourceInspectionForm : Form
{
    private readonly AccessLease _access;
    private readonly System.Windows.Forms.Timer _permissionTimer = new() { Interval = 100 };
    private readonly ComboBox _processes = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _read = new() { Text = "Ler memória", AutoSize = true };
    private readonly Button _save = new() { Text = "Exportar JSON", AutoSize = true, Enabled = false };
    private readonly TextBox _filter = new() { Width = 220, PlaceholderText = "Filtrar nome do jutsu" };
    private readonly Label _status = new() { Dock = DockStyle.Fill, AutoSize = false };
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true,
        AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
    };
    private ResourceInspection? _report;

    public ResourceInspectionForm(AccessLease access)
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        _access = access;
        if (!access.Allows(AppFeature.ResourceInspection)) throw new AccessDeniedException("Inspeção não autorizada.");
        _permissionTimer.Tick += (_, _) => { if (!_access.Allows(AppFeature.ResourceInspection)) { _report = null; _grid.DataSource = null; Close(); } };
        _permissionTimer.Start();
        FormClosed += (_, _) => { _permissionTimer.Stop(); _permissionTimer.Dispose(); _report = null; };
        Text = "Recursos observados — Nin Online (somente leitura)";
        Size = new Size(1060, 650);
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill };
        tools.Controls.AddRange(new Control[] { _processes, _read, _save, _filter });
        root.Controls.Add(tools, 0, 0);
        root.Controls.Add(_status, 0, 1);
        root.Controls.Add(_grid, 0, 2);
        Controls.Add(root);
        foreach (var process in Process.GetProcessesByName("NinOnline"))
        {
            using (process) _processes.Items.Add(process.Id);
        }
        if (_processes.Items.Count > 0) _processes.SelectedIndex = 0;
        _status.Text = "Selecione o processo e clique em Ler memória (pode levar até alguns segundos).\n" +
            "São definições carregadas, não habilidades equipadas. CDTime/CastTime/Range são valores brutos; unidades não validadas.\n" +
            "Não lê chat, credenciais ou rede. Não escreve memória, não injeta código e não envia teclas.";
        _read.Click += async (_, _) => await ReadAsync();
        _filter.TextChanged += (_, _) => RefreshGrid();
        _save.Click += (_, _) => SaveReport();
    }

    private async Task ReadAsync()
    {
        if (!_access.Allows(AppFeature.ResourceInspection)) return;
        if (_processes.SelectedItem is not int pid)
        {
            _status.Text = "NinOnline.exe não encontrado. Abra o jogo e reabra esta janela.";
            return;
        }
        _read.Enabled = _save.Enabled = false;
        _report = null;
        RefreshGrid();
        _status.Text = "Lendo catálogo em modo passivo; nenhum estado de combate será inferido…";
        try
        {
            var report = await Task.Run(() => _access.Allows(AppFeature.ResourceInspection)
                ? LiveInspection.Inspect(pid, () => _access.Allows(AppFeature.ResourceInspection)) : null);
            if (IsDisposed || !_access.Allows(AppFeature.ResourceInspection) || report is null) return;
            _report = report;
            RefreshGrid();
            _status.Text = $"OBSERVADO em {report.ObservedAtUtc.ToLocalTime():HH:mm:ss} · PID {pid} · {report.Spells.Count} definições · {report.ElapsedMs} ms.\n" +
                $"Varredura {(report.Truncated ? "PARCIAL (limite de tempo/objetos)" : "concluída")} · falhas de leitura de jutsus: {report.FailedSpellReads}. GC pode alterar objetos durante a leitura.\n" +
                "CDTime/CastTime/Range brutos (unidades não validadas). CurrentLevel não indica nível equipado. Chakra atual/alvo/cooldown restante desconhecidos.";
            _save.Enabled = true;
        }
        catch (Exception ex)
        {
            if (!IsDisposed) _status.Text = $"Dados indisponíveis: {ex.Message}\nNenhum bypass ou fallback inventado foi aplicado.";
        }
        finally { if (!IsDisposed) _read.Enabled = true; }
    }

    private void RefreshGrid() => _grid.DataSource = _report?.Spells
        .Where(s => s.Name.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).ToList();

    private void SaveReport()
    {
        if (_report is null || !_access.Allows(AppFeature.ResourceInspection)) return;
        using var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "ninonline-resources.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!_access.Allows(AppFeature.ResourceInspection)) return;
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_report, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _status.Text = $"Não foi possível exportar: {ex.Message}"; }
    }
}
