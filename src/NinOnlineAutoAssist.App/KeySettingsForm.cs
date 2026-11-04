using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class KeySettingsForm : Form
{
    public AutoAssistConfig.LiveSection Settings { get; private set; }
    public KeySettingsForm(AutoAssistConfig.LiveSection settings)
    {
        Settings = settings;
        Text = "Teclas e intervalos — aplicação desarma os módulos";
        ClientSize = new Size(590, 670);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, Padding = new Padding(15), AutoScroll = true };
        Controls.Add(panel);
        Label Label(string text)
        {
            var label = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 8, 0, 4) };
            panel.Controls.Add(label);
            return label;
        }
        Label("Versão do SubPunish (somente uma executa por vez):");
        var mode = new ComboBox { Width = 520, DropDownStyle = ComboBoxStyle.DropDownList };
        mode.Items.AddRange(new object[] { "SubPunish Legado — duas ausências antecipadas (funcional)",
            "SubPunish Novo — remoção + salto local (EXPERIMENTAL)" });
        mode.SelectedIndex = settings.PunishMode == LivePunishMode.AnchoredExperimental ? 1 : 0;
        panel.Controls.Add(mode);
        Label("Novo não usa o gatilho legado como fallback. Sem deslocamento correlacionado, não dispara.\nLegado preserva o funcionamento anterior. Salvar desarma ambos.");
        Label("SubPunish: três teclas, na ordem (vinculadas a Attack/Hotbar no jogo)");
        var row = new FlowLayoutPanel { Width = 490, Height = 35 };
        var combo = Enumerable.Range(0, 3).Select(i =>
        {
            var box = new ComboBox { Width = 140, DropDownStyle = ComboBoxStyle.DropDownList };
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
        var activation = new CheckedListBox { Width = 490, Height = 165, CheckOnClick = true, MultiColumn = true, ColumnWidth = 120 };
        foreach (var key in LiveKeySettings.Keys.Keys)
            activation.Items.Add(key, settings.TurnActivationKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
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
                    PunishMode = mode.SelectedIndex == 1 ? LivePunishMode.AnchoredExperimental : LivePunishMode.Legacy,
                    PunishFirstToSecondMs = (long)first.Value, PunishSecondToThirdMs = (long)second.Value,
                    PunishTriggerDelayMs = (long)triggerDelay.Value,
                    AllowCounterAttackSequence = false };
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Configuração inválida"); }
        };
    }
}
