using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class KeySettingsForm : Form
{
	public AutoAssistConfig.LiveSection Settings { get; private set; }

	public KeySettingsForm(AutoAssistConfig.LiveSection settings)
	{
		KeySettingsForm keySettingsForm = this;
		base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		Settings = settings;
		Text = "Teclas e intervalos — aplicação desarma os módulos";
		Screen? primaryScreen = Screen.PrimaryScreen;
		base.ClientSize = new Size(720, Math.Max(540, Math.Min(820, (primaryScreen != null) ? (primaryScreen.WorkingArea.Height - 100) : 820)));
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = (base.MinimizeBox = false);
		base.StartPosition = FormStartPosition.CenterParent;
		FlowLayoutPanel panel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			Padding = new Padding(15),
			AutoScroll = true
		};
		base.Controls.Add(panel);
		Label("SubPunish Legado — duas ausências antecipadas. O modo Novo foi retirado da aplicação.");
		Label("SubPunish: três posições; null pula a posição sem enviar tecla");
		FlowLayoutPanel row = new FlowLayoutPanel
		{
			Width = 630,
			Height = 40
		};
		ComboBox[] combo = Enumerable.Range(0, 3).Select(delegate(int i)
		{
			ComboBox comboBox = new ComboBox
			{
				Width = 190,
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			comboBox.Items.Add("null");
			comboBox.Items.AddRange(LiveKeySettings.Keys.Where<KeyValuePair<string, ushort>>((KeyValuePair<string, ushort> k) => k.Value < 160 && k.Key != "F").Select((Func<KeyValuePair<string, ushort>, object>)((KeyValuePair<string, ushort> k) => k.Key)).ToArray());
			comboBox.SelectedItem = settings.PunishKeys.ElementAtOrDefault(i);
			row.Controls.Add(comboBox);
			return comboBox;
		}).ToArray();
		panel.Controls.Add(row);
		Label("Atraso após o buff sumir → primeira tecla (ms, DEBUG):");
		NumericUpDown triggerDelay = new NumericUpDown
		{
			Maximum = 10000m,
			Value = Math.Clamp(settings.PunishTriggerDelayMs, 0L, 10000L),
			Width = 130
		};
		panel.Controls.Add(triggerDelay);
		Label("Conta após a evidência do modo escolhido. 0 = sem atraso extra.");
		Label firstLabel = Label("Intervalo A → Q (ms após soltar A):");
		NumericUpDown first = new NumericUpDown
		{
			Maximum = 2000m,
			Value = Math.Clamp(settings.PunishFirstToSecondMs, 0L, 2000L),
			Width = 130
		};
		panel.Controls.Add(first);
		Label secondLabel = Label("Intervalo Q → S (ms após soltar Q):");
		NumericUpDown second = new NumericUpDown
		{
			Maximum = 2000m,
			Value = Math.Clamp(settings.PunishSecondToThirdMs, 0L, 2000L),
			Width = 130
		};
		panel.Controls.Add(second);
		ComboBox[] array = combo;
		for (int num = 0; num < array.Length; num++)
		{
			array[num].SelectedIndexChanged += delegate
			{
				RefreshIntervalLabels();
			};
		}
		RefreshIntervalLabels();
		Label("0 ms = sem pausa extra. Cada toque mantém cerca de 40 ms.");
		Label("Turn: segurar QUALQUER uma das teclas marcadas (não é combinação)");
		Label("Pode compartilhar teclas do combo; se estiver segurada, o combo aguarda soltá-la.");
		CheckedListBox activation = new CheckedListBox
		{
			Width = 630,
			Height = 230,
			CheckOnClick = true,
			MultiColumn = true,
			ColumnWidth = 150
		};
		foreach (string key in LiveKeySettings.Keys.Keys)
		{
			activation.Items.Add(key, settings.TurnActivationKeys.Contains<string>(key, StringComparer.OrdinalIgnoreCase));
		}
		Button button = new Button
		{
			Text = "Desmarcar Turn",
			Width = 140,
			Height = 25,
			Margin = new Padding(12, 6, 0, 6)
		};
		button.Click += delegate
		{
			for (int i = 0; i < activation.Items.Count; i++)
			{
				activation.SetItemChecked(i, value: false);
			}
		};
		new ToolTip().SetToolTip(button, "Desmarca as teclas do Auto Turn. A mudança só é aplicada ao salvar.");
		panel.Controls.Add(button);
		panel.Controls.Add(activation);
		Label("F12 e setas são reservados. F continua manual.\nSalvar cancela o combo e revoga a autorização ofensiva.");
		Button button2 = new Button
		{
			Text = "SALVAR E APLICAR",
			Width = 190,
			Height = 36
		};
		panel.Controls.Add(button2);
		button2.Click += delegate
		{
			try
			{
				string?[] combo2 = combo.Select((ComboBox c) => c.SelectedItem?.ToString() ?? "").ToArray();
				string[] activation2 = activation.CheckedItems.Cast<string>().ToArray();
				string[] source = LiveKeySettings.Validate(combo2, activation2);
				if (first.Value + second.Value >= (decimal)(settings.PunishSequenceTimeoutMs - 150))
				{
					throw new ArgumentException("Intervalos excedem o limite de tempo do combo.");
				}
				keySettingsForm.Settings = settings with
				{
					PunishKeys = source.Take(3).ToArray(),
					TurnActivationKeys = source.Skip(3).ToArray(),
					PunishMode = LivePunishMode.Legacy,
					PunishFirstToSecondMs = (long)first.Value,
					PunishSecondToThirdMs = (long)second.Value,
					PunishTriggerDelayMs = (long)triggerDelay.Value,
					AllowCounterAttackSequence = false
				};
				keySettingsForm.DialogResult = DialogResult.OK;
			}
			catch (ArgumentException ex)
			{
				MessageBox.Show(keySettingsForm, ex.Message, "Configuração inválida");
			}
		};
		Label Label(string text)
		{
			Label label = new Label
			{
				Text = text,
				AutoSize = true,
				MaximumSize = new Size(660, 0),
				Margin = new Padding(0, 8, 0, 4)
			};
			panel.Controls.Add(label);
			return label;
		}
		void RefreshIntervalLabels()
		{
			string[] array2 = combo.Select((ComboBox c) => c.SelectedItem?.ToString() ?? "?").ToArray();
			firstLabel.Text = $"Intervalo {array2[0]} → {array2[1]} (ms após soltar {array2[0]}):";
			secondLabel.Text = $"Intervalo {array2[1]} → {array2[2]} (ms após soltar {array2[1]}):";
		}
	}
}
