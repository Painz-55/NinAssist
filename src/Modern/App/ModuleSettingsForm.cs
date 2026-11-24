using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal enum ModuleSettingsFeature { AutoTurning, SubPunish, AutoAttack, Tool, Follow, Stamina }

/// <summary>One isolated editor per module. Each save changes only that module's settings.</summary>
internal sealed class ModuleSettingsForm : Form
{
	public AutoAssistConfig.LiveSection LiveSettings { get; private set; }
	public CombatAssistSettings CombatSettings { get; private set; }

	public ModuleSettingsForm(ModuleSettingsFeature feature, AutoAssistConfig.LiveSection live, CombatAssistSettings combat)
	{
		LiveSettings = live;
		CombatSettings = combat;
		Text = feature switch
		{
			ModuleSettingsFeature.AutoTurning => "Auto Turning — teclas de ativação",
			ModuleSettingsFeature.SubPunish => "SubPunish — combo e tempos",
			ModuleSettingsFeature.AutoAttack => "Auto Ataque — AA",
			ModuleSettingsFeature.Tool => "Auto Tool — shuriken/kunai",
			ModuleSettingsFeature.Follow => "Auto Follow — movimento",
			_ => "Auto Stamina — corrida",
		};
		Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		ClientSize = new Size(600, feature == ModuleSettingsFeature.AutoTurning ? 510 : 440);
		FormBorderStyle = FormBorderStyle.FixedDialog;
		StartPosition = FormStartPosition.CenterParent;
		MaximizeBox = MinimizeBox = false;
		var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
			WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
		Controls.Add(content);
		void Note(string text) => content.Controls.Add(new Label { Text = text, Width = 545, AutoSize = true,
			MaximumSize = new Size(545, 0), Margin = new Padding(0, 7, 0, 7) });
		ComboBox Key(string caption, string value, bool allowModifier)
		{
			Note(caption);
			var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
			box.Items.AddRange(LiveKeySettings.Keys.Where(k => allowModifier || (k.Value < 160 && k.Key != "F"))
				.Select(k => (object)k.Key).ToArray());
			box.SelectedItem = LiveKeySettings.Keys.Keys.FirstOrDefault(k => k.Equals(value, StringComparison.OrdinalIgnoreCase));
			content.Controls.Add(box);
			return box;
		}
		NumericUpDown Number(string caption, decimal value, decimal min, decimal max, int decimals = 0)
		{
			Note(caption);
			var box = new NumericUpDown { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max),
				DecimalPlaces = decimals, Increment = decimals == 0 ? 10 : decimals == 1 ? 0.1m : 0.01m, Width = 180 };
			content.Controls.Add(box);
			return box;
		}
		Action save = () => { };
		switch (feature)
		{
			case ModuleSettingsFeature.AutoTurning:
			{
				Note("Segure qualquer tecla marcada para ativar o giro. Não modifica o combo do SubPunish. F12 continua reservado.");
				var activation = new CheckedListBox { Width = 540, Height = 300, CheckOnClick = true,
					MultiColumn = true, ColumnWidth = 130 };
				foreach (string key in LiveKeySettings.Keys.Keys)
					activation.Items.Add(key, live.TurnActivationKeys.Contains(key, StringComparer.OrdinalIgnoreCase));
				content.Controls.Add(activation);
				save = () =>
				{
					string[] keys = activation.CheckedItems.Cast<string>().ToArray();
					LiveKeySettings.Validate(live.PunishKeys, keys);
					LiveSettings = live with { TurnActivationKeys = keys };
				};
				break;
			}
			case ModuleSettingsFeature.SubPunish:
			{
				Note("Três posições do combo. 'null' pula uma posição. Auto Turning é configurado separadamente.");
				var keys = new ComboBox[3];
				for (int i = 0; i < 3; i++)
				{
					Note($"Tecla {i + 1}:");
					var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
					box.Items.Add("null");
					box.Items.AddRange(LiveKeySettings.Keys.Where(k => k.Value < 160 && k.Key != "F")
						.Select(k => (object)k.Key).ToArray());
					box.SelectedItem = live.PunishKeys.ElementAtOrDefault(i);
					content.Controls.Add(box);
					keys[i] = box;
				}
				var trigger = Number("Atraso após remoção do buff (ms):", live.PunishTriggerDelayMs, 0, 10000);
				var first = Number("Intervalo 1 → 2 (ms):", live.PunishFirstToSecondMs, 0, 2000);
				var second = Number("Intervalo 2 → 3 (ms):", live.PunishSecondToThirdMs, 0, 2000);
				save = () =>
				{
					string[] combo = keys.Select(k => k.SelectedItem?.ToString() ?? "").ToArray();
					string[] valid = LiveKeySettings.Validate(combo, live.TurnActivationKeys);
					if (first.Value + second.Value >= live.PunishSequenceTimeoutMs - 150)
						throw new ArgumentException("Intervalos excedem o limite do combo.");
					LiveSettings = live with { PunishKeys = valid.Take(3).ToArray(), PunishMode = LivePunishMode.Legacy,
						PunishTriggerDelayMs = (long)trigger.Value, PunishFirstToSecondMs = (long)first.Value,
						PunishSecondToThirdMs = (long)second.Value, AllowCounterAttackSequence = false };
				};
				break;
			}
			case ModuleSettingsFeature.AutoAttack:
			{
				Note("AA usa o binding Attack. Distâncias filtram tentativas; não comprovam acerto.");
				var use = new CheckBox { Text = "Usar AA quando o módulo estiver ON", Checked = combat.UseAttack, AutoSize = true };
				content.Controls.Add(use);
				var key = Key("Tecla do AA:", combat.AttackKey, false);
				Note("Geometria:");
				var mode = new ComboBox { Width = 230, DropDownStyle = ComboBoxStyle.DropDownList };
				mode.Items.AddRange(new object[] { "Melee (radial)", "Fan (reto)" });
				mode.SelectedIndex = combat.AttackMode == AutoAttackMode.Fan ? 1 : 0;
				content.Controls.Add(mode);
				var range = Number("Distância do AA (tiles):", (decimal)combat.WeaponRadius, 0.1m, 30, 2);
				var width = Number("Meia-largura Fan (tiles):", (decimal)combat.EffectiveFanHalfWidth, 0.01m, 10, 2);
				var interval = Number("Intervalo de AA (ms):", combat.AttackIntervalMs, 80, 60000);
				save = () => CombatSettings = combat with { UseAttack = use.Checked,
					AttackKey = key.SelectedItem?.ToString() ?? "", AttackMode = (AutoAttackMode)mode.SelectedIndex,
					WeaponRadius = (double)range.Value, FanProjectileHalfWidth = (double)width.Value,
					AttackIntervalMs = (long)interval.Value };
				break;
			}
			case ModuleSettingsFeature.Tool:
			{
				Note("Tool usa o slot da hotbar da shuriken/kunai, nunca ThrowWeapon. Configure AA em sua própria tela.");
				var use = new CheckBox { Text = "Usar Tool quando o módulo estiver ON", Checked = combat.UseTool, AutoSize = true };
				content.Controls.Add(use);
				var key = Key("Tecla da Tool:", combat.ToolKey, false);
				var range = Number("Alcance reto (tiles):", (decimal)combat.ToolRange, 0.1m, 30, 2);
				var width = Number("Meia-largura (tiles):", (decimal)combat.ToolHalfWidth, 0.01m, 1, 2);
				var interval = Number("Intervalo de Tool (ms):", combat.ToolIntervalMs, 80, 60000);
				save = () => CombatSettings = combat with { UseTool = use.Checked, ToolKey = key.SelectedItem?.ToString() ?? "",
					ToolRange = (double)range.Value, ToolHalfWidth = (double)width.Value,
					ToolIntervalMs = (long)interval.Value };
				break;
			}
			case ModuleSettingsFeature.Follow:
			{
				Note("Segure a tecla configurada para seguir. F12 ou soltar a tecla libera as setas.");
				var key = Key("Tecla de ativação:", combat.FollowHoldKey, true);
				var stop = Number("Distância de parada (tiles):", (decimal)combat.FollowStopDistance, 0.5m, 30, 2);
				var interval = Number("Intervalo de passo legado (ms):", combat.FollowStepIntervalMs, 80, 2000);
				save = () => CombatSettings = combat with { FollowHoldKey = key.SelectedItem?.ToString() ?? "",
					FollowStopDistance = (double)stop.Value, FollowStepIntervalMs = (long)interval.Value };
				break;
			}
			case ModuleSettingsFeature.Stamina:
			{
				Note("Auto Stamina exige binding Run compatível e HoldToRun=False. A tecla Run é lida do jogo e não pode conflitar com Follow.");
				var off = Number("Desligar corrida abaixo de (%):", combat.StaminaDisableBelowPercent, 0, 100);
				var on = Number("Religar corrida acima de (%):", combat.StaminaEnableAbovePercent, 0, 100);
				save = () => CombatSettings = combat with { StaminaDisableBelowPercent = (int)off.Value,
					StaminaEnableAbovePercent = (int)on.Value };
				break;
			}
		}
		var button = new Button { Text = "Salvar (módulos permanecem OFF)", AutoSize = true, Height = 36 };
		button.Click += (_, _) =>
		{
			try
			{
				save();
				CombatSettings.Validate();
				DialogResult = DialogResult.OK;
				Close();
			}
			catch (ArgumentException ex)
			{
				MessageBox.Show(this, ex.Message, "Configuração inválida");
			}
		};
		content.Controls.Add(button);
	}
}
