using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class KeySettingsForm : Form
{
    public AutoAssistConfig.LiveSection Settings { get; private set; }
    public KeySettingsForm(AutoAssistConfig.LiveSection settings)
    {
        Settings = settings;
        Text = "Teclas e intervalos — aplicação desarma os módulos";
        ClientSize = new Size(720, Math.Max(540, Math.Min(820, Screen.PrimaryScreen?.WorkingArea.Height - 100 ?? 820)));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(15), AutoScroll = true };
        Controls.Add(panel);
        Label Label(string text)
        {
            var label = new Label { Text = text, AutoSize = true, MaximumSize = new Size(660, 0),
                Margin = new Padding(0, 8, 0, 4) };
            panel.Controls.Add(label);
            return label;
        }
        Label("SubPunish Legado — duas ausências antecipadas. O modo Novo foi retirado da aplicação.");
        Label("SubPunish: três posições; null pula a posição sem enviar tecla");
        var row = new FlowLayoutPanel { Width = 630, Height = 40 };
        var combo = Enumerable.Range(0, 3).Select(i =>
        {
            var box = new ComboBox { Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            box.Items.Add(LiveKeySettings.Disabled);
            box.Items.AddRange(LiveKeySettings.Keys.Where(k => k.Value < 0xA0 && k.Key != "F").Select(k => (object)k.Key).ToArray());
            box.SelectedItem = settings.PunishKeys.ElementAtOrDefault(i);
            row.Controls.Add(box);
            return box;
        }).ToArray();
        panel.Controls.Add(row);
        Label("Atraso após o buff sumir → primeira tecla (ms, DEBUG):");
        var triggerDelay = new NumericUpDown { Maximum = 10000,
            Value = Math.Clamp(settings.PunishTriggerDelayMs, 0, 10000), Width = 130 };
        panel.Controls.Add(triggerDelay);
        Label("Conta após a evidência do modo escolhido. 0 = sem atraso extra.");
        var firstLabel = Label("Intervalo A → Q (ms após soltar A):");
        var first = new NumericUpDown { Maximum = 2000, Value = Math.Clamp(settings.PunishFirstToSecondMs, 0, 2000), Width = 130 };
        panel.Controls.Add(first);
        var secondLabel = Label("Intervalo Q → S (ms após soltar Q):");
        var second = new NumericUpDown { Maximum = 2000, Value = Math.Clamp(settings.PunishSecondToThirdMs, 0, 2000), Width = 130 };
        panel.Controls.Add(second);
        void RefreshIntervalLabels()
        {
            var names = combo.Select(c => c.SelectedItem?.ToString() ?? "?").ToArray();
            firstLabel.Text = $"Intervalo {names[0]} → {names[1]} (ms após soltar {names[0]}):";
            secondLabel.Text = $"Intervalo {names[1]} → {names[2]} (ms após soltar {names[1]}):";
        }
        foreach (var box in combo) box.SelectedIndexChanged += (_, _) => RefreshIntervalLabels();
        RefreshIntervalLabels();
        Label("0 ms = sem pausa extra. Cada toque mantém cerca de 40 ms.");
        Label("Turn: segurar QUALQUER uma das teclas marcadas (não é combinação)");
        Label("Pode compartilhar teclas do combo; se estiver segurada, o combo aguarda soltá-la.");
        var activation = new CheckedListBox { Width = 630, Height = 230, CheckOnClick = true, MultiColumn = true, ColumnWidth = 150 };
        foreach (var key in LiveKeySettings.Keys.Keys)
            activation.Items.Add(key, settings.TurnActivationKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
        var clear = new Button { Text = "Desmarcar Turn", Width = 140, Height = 25, Margin = new Padding(12, 6, 0, 6) };
        clear.Click += (_, _) => { for (var i = 0; i < activation.Items.Count; i++) activation.SetItemChecked(i, false); };
        new ToolTip().SetToolTip(clear, "Desmarca as teclas do Auto Turn. A mudança só é aplicada ao salvar.");
        panel.Controls.Add(clear);
        panel.Controls.Add(activation);
        Label("F12 e setas são reservados. F continua manual.\nSalvar cancela o combo e revoga a autorização ofensiva.");
        var save = new Button { Text = "SALVAR E APLICAR", Width = 190, Height = 36 };
        panel.Controls.Add(save);
        save.Click += (_, _) =>
        {
            try
            {
                var keys = combo.Select(c => c.SelectedItem?.ToString() ?? "").ToArray();
                var holds = activation.CheckedItems.Cast<string>().ToArray();
                var canonical = LiveKeySettings.Validate(keys, holds);
                if (first.Value + second.Value >= settings.PunishSequenceTimeoutMs - 150)
                    throw new ArgumentException("Intervalos excedem o limite de tempo do combo.");
                Settings = settings with { PunishKeys = canonical.Take(3).ToArray(), TurnActivationKeys = canonical.Skip(3).ToArray(),
                    PunishMode = LivePunishMode.Legacy,
                    PunishFirstToSecondMs = (long)first.Value, PunishSecondToThirdMs = (long)second.Value,
                    PunishTriggerDelayMs = (long)triggerDelay.Value,
                    AllowCounterAttackSequence = false };
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Configuração inválida"); }
        };
    }
}
