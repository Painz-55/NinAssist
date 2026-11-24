using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class AutoCastTurnForm : Form
{
	public AutoCastTurnSettings Settings { get; private set; }

	public AutoCastTurnForm(AutoCastTurnSettings settings, Func<IReadOnlyList<int>> observedIds)
	{
		AutoCastTurnForm autoCastTurnForm = this;
		base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		Settings = settings;
		Text = "Auto Cast Turn — janela final";
		base.ClientSize = new Size(500, 620);
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.StartPosition = FormStartPosition.CenterParent;
		base.MaximizeBox = (base.MinimizeBox = false);
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Padding = new Padding(14),
			AutoScroll = true
		};
		flowLayoutPanel.Controls.Add(new Label
		{
			Width = 465,
			Height = 125,
			Text = "Por padrão, gira no fim de todo cast válido observado com alvo selecionado. No modo Próximo, usa o jogador vivo mais próximo da leitura do mapa; não muda a seleção no jogo. Somente os IDs abaixo NÃO giram.\nMantém/corrige a direção na janela final (40–250 ms). Não conjura magia. F12 libera a tecla; efeito/hit não garantidos."
		});
		flowLayoutPanel.Controls.Add(new Label { AutoSize = true, Text = "Alvo para o giro:" });
		ComboBox mode = new ComboBox { Width = 250, DropDownStyle = ComboBoxStyle.DropDownList };
		mode.Items.AddRange(new object[] { "Selecionado no jogo", "Jogador mais próximo" });
		mode.SelectedIndex = settings.TargetMode == CastTurnTargetMode.Nearest ? 1 : 0;
		flowLayoutPanel.Controls.Add(mode);
		flowLayoutPanel.Controls.Add(new Label { AutoSize = true, Text = "Raio do modo Próximo (tiles):" });
		NumericUpDown radius = new NumericUpDown { Minimum = 1, Maximum = 40, DecimalPlaces = 1, Increment = 0.5m,
			Value = (decimal)Math.Clamp(settings.NearestRadiusTiles, 1, 40), Width = 180 };
		flowLayoutPanel.Controls.Add(radius);
		CheckBox ignoreVillage = new CheckBox { Text = "Próximo: não mirar membros da minha vila (vila desconhecida também)", Width = 455, Checked = settings.NearestIgnoreSameVillage };
		flowLayoutPanel.Controls.Add(ignoreVillage);
		flowLayoutPanel.Controls.Add(new Label
		{
			Text = "IDs de jutsu a EXCLUIR (um por linha; vazio = nenhum):",
			Width = 465,
			Height = 24
		});
		TextBox exclusions = new TextBox
		{
			Multiline = true,
			ScrollBars = ScrollBars.Vertical,
			Width = 455,
			Height = 100,
			Text = string.Join(Environment.NewLine, settings.ExcludedSpellIds)
		};
		flowLayoutPanel.Controls.Add(exclusions);
		flowLayoutPanel.Controls.Add(new Label
		{
			Width = 465,
			Height = 37,
			Text = "IDs de casts observados neste personagem (somente leitura; não precisa ligar Auto Cast Turn). Lance o jutsu no jogo e volte aqui para selecionar:"
		});
		ListBox observed = new ListBox
		{
			Width = 455,
			Height = 90
		};
		flowLayoutPanel.Controls.Add(observed);
		Timer refresh = new Timer
		{
			Interval = 250
		};
		refresh.Tick += delegate
		{
			RefreshObserved();
		};
		RefreshObserved();
		refresh.Start();
		base.Disposed += delegate
		{
			refresh.Stop();
			refresh.Dispose();
		};
		Button button = new Button
		{
			Text = "Adicionar ID selecionado à exclusão",
			AutoSize = true
		};
		button.Click += delegate
		{
			if (observed.SelectedItem is int num)
			{
				string[] source = (from s in exclusions.Lines
					select s.Trim() into s
					where s.Length > 0
					select s).ToArray();
				if (!source.Contains<string>(num.ToString(), StringComparer.Ordinal))
				{
					exclusions.Text = string.Join(Environment.NewLine, source.Append(num.ToString()));
				}
			}
		};
		flowLayoutPanel.Controls.Add(button);
		flowLayoutPanel.Controls.Add(new Label
		{
			AutoSize = true,
			Text = "Antecedência em milissegundos:"
		});
		NumericUpDown lead = new NumericUpDown
		{
			Minimum = 40m,
			Maximum = 250m,
			Increment = 10m,
			Value = Math.Clamp(settings.LeadTimeMs, 40, 250),
			Width = 180
		};
		flowLayoutPanel.Controls.Add(lead);
		Button button2 = new Button
		{
			Text = "Salvar (módulos permanecem OFF)",
			AutoSize = true
		};
		button2.Click += delegate
		{
			try
			{
				List<int> list = new List<int>();
				foreach (string item in exclusions.Lines.Where((string line) => !string.IsNullOrWhiteSpace(line)))
				{
					if (!int.TryParse(item.Trim(), out var result) || result <= 0 || list.Contains(result))
					{
						throw new ArgumentException("ID inválido ou repetido: " + item + ". Informe IDs positivos únicos.");
					}
					list.Add(result);
				}
				AutoCastTurnSettings autoCastTurnSettings = settings with
				{
					LeadTimeMs = (int)lead.Value,
					ExcludedSpellIds = list.ToArray(),
					TargetMode = mode.SelectedIndex == 1 ? CastTurnTargetMode.Nearest : CastTurnTargetMode.Selected,
					NearestRadiusTiles = (double)radius.Value,
					NearestIgnoreSameVillage = ignoreVillage.Checked
				};
				autoCastTurnSettings.Validate();
				autoCastTurnForm.Settings = autoCastTurnSettings;
				autoCastTurnForm.DialogResult = DialogResult.OK;
				autoCastTurnForm.Close();
			}
			catch (ArgumentException ex)
			{
				MessageBox.Show(autoCastTurnForm, ex.Message, "Cast Turn inválido");
			}
		};
		flowLayoutPanel.Controls.Add(button2);
		base.Controls.Add(flowLayoutPanel);
		void RefreshObserved()
		{
			IReadOnlyList<int> readOnlyList = observedIds();
			if (!readOnlyList.SequenceEqual(observed.Items.Cast<int>()))
			{
				int? num = ((observed.SelectedItem is int value) ? new int?(value) : ((int?)null));
				observed.Items.Clear();
				foreach (int item2 in readOnlyList)
				{
					observed.Items.Add(item2);
				}
				if (num.HasValue)
				{
					int valueOrDefault = num.GetValueOrDefault();
					if (observed.Items.Contains(valueOrDefault))
					{
						observed.SelectedItem = valueOrDefault;
					}
				}
			}
		}
	}
}
