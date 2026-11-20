using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class CombatAssistForm : Form
{
	public CombatAssistSettings Settings { get; private set; }

	public CombatAssistForm(CombatAssistSettings settings)
	{
		CombatAssistForm combatAssistForm = this;
		base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		Settings = settings;
		Text = "AA / Tool / Follow / Stamina";
		base.ClientSize = new Size(640, 660);
		base.StartPosition = FormStartPosition.CenterParent;
		base.FormBorderStyle = FormBorderStyle.FixedDialog;
		base.MaximizeBox = (base.MinimizeBox = false);
		FlowLayoutPanel content = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			FlowDirection = FlowDirection.TopDown,
			WrapContents = false,
			AutoScroll = true,
			Padding = new Padding(14)
		};
		base.Controls.Add(content);
		content.Controls.Add(new Label
		{
			AutoSize = true,
			MaximumSize = new Size(590, 0),
			Text = "Distâncias em tiles (32 px). Valores são filtros de tentativa, não prova de hit.\nAA usa Attack. Tool usa o SLOT DA HOTBAR com a shuriken/kunai (ex.: S / HotbarF7).\nNão use ThrowWeapon: essa ação arremessa a arma equipada."
		});
		CheckBox useAA = new CheckBox
		{
			Text = "Usar AA",
			Checked = settings.UseAttack,
			AutoSize = true
		};
		CheckBox useTool = new CheckBox
		{
			Text = "Usar Tool/shuriken",
			Checked = settings.UseTool,
			AutoSize = true
		};
		content.Controls.Add(useAA);
		content.Controls.Add(useTool);
		ComboBox attack = Key("Tecla do Auto Ataque (AA)", settings.AttackKey, modifier: false);
		ComboBox attackMode = new ComboBox
		{
			DropDownStyle = ComboBoxStyle.DropDownList
		};
		attackMode.Items.AddRange(new object[2] { "Melee (radial)", "Fan (reto como shuriken)" });
		ComboBox comboBox = attackMode;
		AutoAttackMode attackMode2 = settings.AttackMode;
		comboBox.SelectedIndex = (int)(((uint)attackMode2 <= 1u) ? settings.AttackMode : ((AutoAttackMode)(-1)));
		Row("Tipo de arma / geometria do AA", attackMode);
		NumericUpDown radius = Number("Distância do AA — Melee radial / Fan reto (tiles)", (decimal)settings.WeaponRadius, 0.1m, 30m, 2);
		NumericUpDown fanWidth = Number("Meia-largura do projétil Fan (tiles)", (decimal)settings.EffectiveFanHalfWidth, 0.01m, 10m, 2);
		attackMode.SelectedIndexChanged += delegate
		{
			RefreshAttackMode();
		};
		RefreshAttackMode();
		content.Controls.Add(new Label
		{
			AutoSize = true,
			MaximumSize = new Size(590, 0),
			Text = "Fan usa a distância configurável do AA e sua largura própria. Se necessário, gira para o alvo, espera a direção observada e só então envia a tecla de AA. Tool mantém seu alcance/largura separados. Hisuy Fan indisponível. Sem garantia de acerto."
		});
		NumericUpDown aaDelay = Number("Intervalo entre tentativas de AA (ms)", settings.AttackIntervalMs, 80m, 60000m);
		ComboBox tool = Key("Tecla do slot da shuriken/Tool na hotbar", settings.ToolKey, modifier: false);
		NumericUpDown range = Number("Alcance reto da Tool (tiles)", (decimal)settings.ToolRange, 0.1m, 30m, 2);
		NumericUpDown halfWidth = Number("Meia-largura reta da Tool (tiles)", (decimal)settings.ToolHalfWidth, 0.01m, 1m, 2);
		NumericUpDown toolDelay = Number("Intervalo entre tentativas de Tool (ms)", settings.ToolIntervalMs, 80m, 60000m);
		ComboBox hold = Key("Follow: tecla que deve ficar pressionada", settings.FollowHoldKey, modifier: true);
		NumericUpDown stop = Number("Follow: distância de parada (tiles)", (decimal)settings.FollowStopDistance, 0.5m, 30m, 2);
		NumericUpDown moveDelay = Number("Follow: intervalo legado (sem pulsar as setas)", settings.FollowStepIntervalMs, 80m, 2000m);
		NumericUpDown staminaOff = Number("Stamina: desligar abaixo de (%)", settings.StaminaDisableBelowPercent, 0m, 100m);
		NumericUpDown staminaOn = Number("Stamina: religar acima de (%)", settings.StaminaEnableAbovePercent, 0m, 100m);
		staminaOff.Increment = (staminaOn.Increment = 1m);
		content.Controls.Add(new Label
		{
			AutoSize = true,
			MaximumSize = new Size(590, 0),
			Text = "Follow mantém UMA OU DUAS SETAS pressionadas (diagonais), sem cortar quinas bloqueadas. Para sem caminho/perigos conhecidos; não detecta traps invisíveis. Soltar a tecla ou F12 libera ambas.\nAuto Stamina usa os percentuais acima e mantém a decisão entre eles; o limite para religar deve ser maior.\nSalvar desarma todos os módulos; não altera bindings do jogo."
		});
		Button button = new Button
		{
			Text = "SALVAR E APLICAR",
			Width = 200,
			Height = 34
		};
		content.Controls.Add(button);
		button.Click += delegate
		{
			try
			{
				CombatAssistSettings combatAssistSettings = settings with
				{
					UseAttack = useAA.Checked,
					UseTool = useTool.Checked,
					AttackMode = (AutoAttackMode)attackMode.SelectedIndex,
					FanProjectileHalfWidth = (double)fanWidth.Value,
					AttackKey = (attack.SelectedItem?.ToString() ?? ""),
					ToolKey = (tool.SelectedItem?.ToString() ?? ""),
					AttackIntervalMs = (long)aaDelay.Value,
					ToolIntervalMs = (long)toolDelay.Value,
					WeaponRadius = (double)radius.Value,
					ToolRange = (double)range.Value,
					ToolHalfWidth = (double)halfWidth.Value,
					FollowHoldKey = (hold.SelectedItem?.ToString() ?? ""),
					FollowStopDistance = (double)stop.Value,
					FollowStepIntervalMs = (long)moveDelay.Value,
					StaminaDisableBelowPercent = (int)staminaOff.Value,
					StaminaEnableAbovePercent = (int)staminaOn.Value
				};
				combatAssistSettings.Validate();
				combatAssistForm.Settings = combatAssistSettings;
				combatAssistForm.DialogResult = DialogResult.OK;
			}
			catch (ArgumentException ex)
			{
				MessageBox.Show(combatAssistForm, ex.Message, "Configuração inválida");
			}
		};
		ComboBox Key(string caption, string value, bool modifier)
		{
			ComboBox comboBox2 = new ComboBox
			{
				DropDownStyle = ComboBoxStyle.DropDownList
			};
			comboBox2.Items.AddRange(LiveKeySettings.Keys.Where<KeyValuePair<string, ushort>>((KeyValuePair<string, ushort> k) => modifier || (k.Value < 160 && k.Key != "F")).Select((Func<KeyValuePair<string, ushort>, object>)((KeyValuePair<string, ushort> k) => k.Key)).ToArray());
			comboBox2.SelectedItem = LiveKeySettings.Keys.Keys.FirstOrDefault((string k) => k.Equals(value, StringComparison.OrdinalIgnoreCase));
			Row(caption, comboBox2);
			return comboBox2;
		}
		NumericUpDown Number(string caption, decimal value, decimal min, decimal max, int decimals = 0)
		{
			NumericUpDown numericUpDown = new NumericUpDown
			{
				Minimum = min,
				Maximum = max,
				DecimalPlaces = decimals,
				Value = Math.Clamp(value, min, max),
				Increment = ((decimals > 0) ? 0.1m : 10m)
			};
			Row(caption, numericUpDown);
			return numericUpDown;
		}
		void RefreshAttackMode()
		{
			NumericUpDown numericUpDown = radius;
			int selectedIndex = attackMode.SelectedIndex;
			bool enabled = (uint)selectedIndex <= 1u;
			numericUpDown.Enabled = enabled;
			fanWidth.Enabled = attackMode.SelectedIndex == 1;
		}
		Control Row(string caption, Control control)
		{
			FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
			{
				Width = 590,
				Height = 35
			};
			flowLayoutPanel.Controls.Add(new Label
			{
				Text = caption,
				Width = 360,
				Height = 28,
				TextAlign = ContentAlignment.MiddleLeft
			});
			control.Width = 180;
			flowLayoutPanel.Controls.Add(control);
			content.Controls.Add(flowLayoutPanel);
			return control;
		}
	}
}
