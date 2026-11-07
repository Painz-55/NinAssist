using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class AutoCastTurnForm : Form
{
    public AutoCastTurnSettings Settings { get; private set; }
    public AutoCastTurnForm(AutoCastTurnSettings settings, Func<IReadOnlyList<int>> observedIds)
    {
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Settings = settings;
        Text = "Auto Cast Turn — janela final";
        ClientSize = new Size(500, 540); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = MinimizeBox = false;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(14), AutoScroll = true };
        panel.Controls.Add(new Label { Width = 465, Height = 125,
            Text = "Por padrão, gira no fim de todo cast válido observado com alvo. Somente os IDs informados abaixo NÃO giram. Confira IDs no diagnóstico; não adivinhe.\nMantém/corrige a direção na janela final (40–250 ms, até o último quarto da barra). Não conjura magia. F12 e cancelamentos liberam a tecla; efeito/hit não garantidos." });
        panel.Controls.Add(new Label { Text = "IDs de jutsu a EXCLUIR (um por linha; vazio = nenhum):", Width = 465, Height = 24 });
        var exclusions = new TextBox { Multiline = true, ScrollBars = ScrollBars.Vertical, Width = 455, Height = 100,
            Text = string.Join(Environment.NewLine, settings.ExcludedSpellIds) };
        panel.Controls.Add(exclusions);
        panel.Controls.Add(new Label { Width = 465, Height = 37,
            Text = "IDs de casts observados neste personagem (somente leitura; não precisa ligar Auto Cast Turn). Lance o jutsu no jogo e volte aqui para selecionar:" });
        var observed = new ListBox { Width = 455, Height = 90 };
        panel.Controls.Add(observed);
        void RefreshObserved()
        {
            var ids = observedIds();
            if (ids.SequenceEqual(observed.Items.Cast<int>())) return;
            var selected = observed.SelectedItem is int id ? id : (int?)null;
            observed.Items.Clear();
            foreach (var value in ids) observed.Items.Add(value);
            if (selected is { } previous && observed.Items.Contains(previous)) observed.SelectedItem = previous;
        }
        var refresh = new System.Windows.Forms.Timer { Interval = 250 };
        refresh.Tick += (_, _) => RefreshObserved();
        RefreshObserved(); refresh.Start();
        Disposed += (_, _) => { refresh.Stop(); refresh.Dispose(); };
        var add = new Button { Text = "Adicionar ID selecionado à exclusão", AutoSize = true };
        add.Click += (_, _) =>
        {
            if (observed.SelectedItem is not int id) return;
            var existing = exclusions.Lines.Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (!existing.Contains(id.ToString(), StringComparer.Ordinal))
                exclusions.Text = string.Join(Environment.NewLine, existing.Append(id.ToString()));
        };
        panel.Controls.Add(add);
        panel.Controls.Add(new Label { AutoSize = true, Text = "Antecedência em milissegundos:" });
        var lead = new NumericUpDown { Minimum = 40, Maximum = 250, Increment = 10, Value = Math.Clamp(settings.LeadTimeMs, 40, 250), Width = 180 };
        panel.Controls.Add(lead);
        var save = new Button { Text = "Salvar (módulos permanecem OFF)", AutoSize = true };
        save.Click += (_, _) =>
        {
            try
            {
                var ids = new List<int>();
                foreach (var raw in exclusions.Lines.Where(line => !string.IsNullOrWhiteSpace(line)))
                {
                    if (!int.TryParse(raw.Trim(), out var id) || id <= 0 || ids.Contains(id))
                        throw new ArgumentException($"ID inválido ou repetido: {raw}. Informe IDs positivos únicos.");
                    ids.Add(id);
                }
                var updated = settings with { LeadTimeMs = (int)lead.Value, ExcludedSpellIds = ids.ToArray() };
                updated.Validate(); Settings = updated; DialogResult = DialogResult.OK; Close();
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Cast Turn inválido"); }
        };
        panel.Controls.Add(save); Controls.Add(panel);
    }
}
