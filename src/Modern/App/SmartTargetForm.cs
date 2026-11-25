using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class SmartTargetForm : Form
{
	public SmartTargetSettings Settings { get; private set; }

	public SmartTargetForm(SmartTargetSettings settings)
	{
		Settings = settings;
		Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		Text = "Smart Target — segurar tecla";
		ClientSize = new Size(460, 360);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = MinimizeBox = false;
		var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
			WrapContents = false, Padding = new Padding(14), AutoScroll = true };
		flow.Controls.Add(new Label { Width = 420, Height = 85, Text =
			"Com o módulo ON, seleciona pela memória o jogador vivo mais próximo de outra vila apenas enquanto a tecla estiver pressionada. Ao soltar, mantém o último alvo. Se a tecla também está vinculada a uma ação do jogo (por exemplo, Target=Tab), pode haver disputa; remapeie no jogo se necessário." });
		flow.Controls.Add(new Label { AutoSize = true, Text = "Tecla para segurar:" });
		var key = new ComboBox { Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
		key.Items.AddRange(SmartTargetSettings.HoldKeys.Keys.Cast<object>().ToArray());
		key.SelectedItem = key.Items.Cast<string>().FirstOrDefault(k => k.Equals(settings.HoldKey, StringComparison.OrdinalIgnoreCase)) ?? "Tab";
		flow.Controls.Add(key);
		flow.Controls.Add(new Label { AutoSize = true, Text = "Raio máximo em tiles:" });
		var radius = new NumericUpDown { Minimum = 1, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m,
			Value = (decimal)Math.Clamp(settings.RadiusTiles, 1, 40), Width = 180 };
		flow.Controls.Add(radius);
		flow.Controls.Add(new Label { AutoSize = true, Text = "Máximo de tentativas por alvo:" });
		var attempts = new NumericUpDown { Minimum = 1, Maximum = 10,
			Value = Math.Clamp(settings.MaxAttempts, 1, 10), Width = 180 };
		flow.Controls.Add(attempts);
		flow.Controls.Add(new Label { AutoSize = true, Text = "Intervalo entre tentativas (ms):" });
		var interval = new NumericUpDown { Minimum = 150, Maximum = 5000, Increment = 50,
			Value = Math.Clamp(settings.AttemptIntervalMs, 150, 5000), Width = 180 };
		flow.Controls.Add(interval);
		var save = new Button { Text = "Salvar (módulos permanecem OFF)", AutoSize = true };
		save.Click += (_, _) =>
		{
			try
			{
				var next = settings with { HoldKey = (string)key.SelectedItem!, RadiusTiles = (double)radius.Value,
					MaxAttempts = (int)attempts.Value, AttemptIntervalMs = (int)interval.Value };
				next.Validate();
				Settings = next;
				DialogResult = DialogResult.OK;
				Close();
			}
			catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Smart Target inválido"); }
		};
		flow.Controls.Add(save);
		Controls.Add(flow);
	}
}
