using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class AutoCastTurnForm : Form
{
    public AutoCastTurnSettings Settings { get; private set; }
    public AutoCastTurnForm(AutoCastTurnSettings settings)
    {
        Settings = settings;
        Text = "Auto Cast Turn — janela final";
        ClientSize = new Size(500, 280); FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = MinimizeBox = false;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(14) };
        panel.Controls.Add(new Label { Width = 465, Height = 125,
            Text = "Mantém/corrige a direção do alvo até o fim do cast, inclusive andando. Não conjura magias nem bloqueia movimento.\nJanela final ajustável (40–250 ms), limitada ao último quarto da barra. Padrão: 100 ms.\nCancelamentos liberam a tecla. Cancelar após o giro não desfaz input já enviado. Efeito/hit não garantidos." });
        panel.Controls.Add(new Label { AutoSize = true, Text = "Antecedência em milissegundos:" });
        var lead = new NumericUpDown { Minimum = 40, Maximum = 250, Increment = 10, Value = Math.Clamp(settings.LeadTimeMs, 40, 250), Width = 180 };
        panel.Controls.Add(lead);
        var save = new Button { Text = "Salvar (módulos permanecem OFF)", AutoSize = true };
        save.Click += (_, _) => { Settings = settings with { LeadTimeMs = (int)lead.Value }; Settings.Validate(); DialogResult = DialogResult.OK; Close(); };
        panel.Controls.Add(save); Controls.Add(panel);
    }
}
