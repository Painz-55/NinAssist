using System;
using System.Drawing;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class AutoTargetForm : Form
{
	public AutoTargetSettings Settings { get; private set; }

	public AutoTargetForm(AutoTargetSettings settings)
	{
		Settings = settings;
		Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		Text = "Auto Target — alvo mais próximo";
		ClientSize = new Size(460, 340);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = MinimizeBox = false;
		FlowLayoutPanel flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
			WrapContents = false, Padding = new Padding(14), AutoScroll = true };
		flow.Controls.Add(new Label { Width = 420, Height = 80, Text = "Seleciona o jogador vivo mais próximo por estado do cliente. Escrita experimental na memória, apenas para build e perfil assinados e após validação no jogo. Confirma o resultado por nova leitura. Firebase e botão ON devem autorizar." });
		flow.Controls.Add(new Label { AutoSize = true, Text = "Raio máximo em tiles:" });
		NumericUpDown radius = new NumericUpDown { Minimum = 1, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m,
			Value = (decimal)Math.Clamp(settings.RadiusTiles, 1, 40), Width = 180 };
		flow.Controls.Add(radius);
		flow.Controls.Add(new Label { AutoSize = true, Text = "Máximo de tentativas por alvo:" });
		NumericUpDown attempts = new NumericUpDown { Minimum = 1, Maximum = 10, Value = Math.Clamp(settings.MaxAttempts, 1, 10), Width = 180 };
		flow.Controls.Add(attempts);
		flow.Controls.Add(new Label { AutoSize = true, Text = "Intervalo entre tentativas (ms):" });
		NumericUpDown interval = new NumericUpDown { Minimum = 150, Maximum = 5000, Increment = 50,
			Value = Math.Clamp(settings.AttemptIntervalMs, 150, 5000), Width = 180 };
		flow.Controls.Add(interval);
		CheckBox ignoreVillage = new CheckBox { Width = 400, Text = "Não selecionar membros da minha vila (desconhecidos também)", Checked = settings.IgnoreSameVillage };
		flow.Controls.Add(ignoreVillage);
		Button save = new Button { Text = "Salvar (módulos permanecem OFF)", AutoSize = true };
		save.Click += (_, _) =>
		{
			try
			{
				AutoTargetSettings next = settings with { RadiusTiles = (double)radius.Value, MaxAttempts = (int)attempts.Value,
					AttemptIntervalMs = (int)interval.Value, IgnoreSameVillage = ignoreVillage.Checked };
				next.Validate();
				Settings = next;
				DialogResult = DialogResult.OK;
				Close();
			}
			catch (ArgumentException ex)
			{
				MessageBox.Show(this, ex.Message, "Auto Target inválido");
			}
		};
		flow.Controls.Add(save);
		Controls.Add(flow);
	}
}
