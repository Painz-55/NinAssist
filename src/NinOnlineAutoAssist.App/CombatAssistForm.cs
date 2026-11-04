using NinOnlineAutoAssist.Core.Config;

namespace NinOnlineAutoAssist.App;

internal sealed class CombatAssistForm : Form
{
    public CombatAssistSettings Settings { get; private set; }
    public CombatAssistForm(CombatAssistSettings settings)
    {
        Settings = settings;
        Text = "AA / Tool / Follow / Stamina";
        ClientSize = new Size(640, 660);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false;
        var content = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(14) };
        Controls.Add(content);
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(590, 0),
            Text = "Distâncias em tiles (32 px). Valores são filtros de tentativa, não prova de hit.\nAA usa Attack. Tool usa o SLOT DA HOTBAR com a shuriken/kunai (ex.: S / HotbarF7).\nNão use ThrowWeapon: essa ação arremessa a arma equipada." });
        Control Row(string caption, Control control)
        {
            var row = new FlowLayoutPanel { Width = 590, Height = 35 };
            row.Controls.Add(new Label { Text = caption, Width = 360, Height = 28, TextAlign = ContentAlignment.MiddleLeft });
            control.Width = 180; row.Controls.Add(control); content.Controls.Add(row); return control;
        }
        ComboBox Key(string caption, string value, bool modifier)
        {
            var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            box.Items.AddRange(LiveKeySettings.Keys.Where(k => modifier || (k.Value < 0xA0 && k.Key != "F"))
                .Select(k => (object)k.Key).ToArray());
            box.SelectedItem = LiveKeySettings.Keys.Keys.FirstOrDefault(k => k.Equals(value, StringComparison.OrdinalIgnoreCase));
            Row(caption, box); return box;
        }
        NumericUpDown Number(string caption, decimal value, decimal min, decimal max, int decimals = 0)
        {
            var number = new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = decimals,
                Value = Math.Clamp(value, min, max), Increment = decimals > 0 ? 0.1m : 10 };
            Row(caption, number); return number;
        }
        var useAA = new CheckBox { Text = "Usar AA", Checked = settings.UseAttack, AutoSize = true };
        var useTool = new CheckBox { Text = "Usar Tool/shuriken", Checked = settings.UseTool, AutoSize = true };
        content.Controls.Add(useAA); content.Controls.Add(useTool);
        var attack = Key("Tecla do Auto Ataque (AA)", settings.AttackKey, false);
        var attackMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        attackMode.Items.AddRange(new object[] { "Melee (radial)", "Fan (frontal)", "Hisuy Fan (frontal)" });
        attackMode.SelectedIndex = Enum.IsDefined(settings.AttackMode) ? (int)settings.AttackMode : -1;
        Row("Tipo de arma / geometria do AA", attackMode);
        var radius = Number("Raio da arma — AA em volta do personagem", (decimal)settings.WeaponRadius, 0.1m, 30, 2);
        var fanRange = Number("Fan: alcance frontal (tiles)", (decimal)settings.FanRange, 0.1m, 30, 2);
        var fanWidth = Number("Fan: meia-largura frontal (tiles)", (decimal)settings.FanHalfWidth, 0.01m, 10, 2);
        var hisuyRange = Number("Hisuy Fan: alcance frontal (tiles)", (decimal)settings.HisuyFanRange, 0.1m, 30, 2);
        var hisuyWidth = Number("Hisuy Fan: meia-largura frontal (tiles)", (decimal)settings.HisuyFanHalfWidth, 0.01m, 10, 2);
        void RefreshAttackMode()
        {
            radius.Enabled = attackMode.SelectedIndex == 0;
            fanRange.Enabled = fanWidth.Enabled = attackMode.SelectedIndex == 1;
            hisuyRange.Enabled = hisuyWidth.Enabled = attackMode.SelectedIndex == 2;
        }
        attackMode.SelectedIndexChanged += (_, _) => RefreshAttackMode();
        RefreshAttackMode();
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(590, 0),
            Text = "Fan/Hisuy Fan usam AA para frente, não ThrowWeapon. Valores iniciais são referências NÃO calibradas: ajuste ao alcance/largura da sua arma. Meia-largura = metade da faixa total. O modo é escolhido manualmente, sem detectar arma equipada." });
        var aaDelay = Number("Intervalo entre tentativas de AA (ms)", settings.AttackIntervalMs, 80, 60000);
        var tool = Key("Tecla do slot da shuriken/Tool na hotbar", settings.ToolKey, false);
        var range = Number("Alcance frontal máximo da Tool (tiles)", (decimal)settings.ToolRange, 0.1m, 30, 2);
        var halfWidth = Number("Meia-largura da faixa frontal da Tool (tiles)", (decimal)settings.ToolHalfWidth, 0.01m, 1, 2);
        var toolDelay = Number("Intervalo entre tentativas de Tool (ms)", settings.ToolIntervalMs, 80, 60000);
        var hold = Key("Follow: tecla que deve ficar pressionada", settings.FollowHoldKey, true);
        var stop = Number("Follow: distância de parada (tiles)", (decimal)settings.FollowStopDistance, 0.5m, 30, 2);
        var moveDelay = Number("Follow: intervalo legado (sem pulsar as setas)", settings.FollowStepIntervalMs, 80, 2000);
        var staminaOff = Number("Stamina: desligar abaixo de (%)", settings.StaminaDisableBelowPercent, 0, 100);
        var staminaOn = Number("Stamina: religar acima de (%)", settings.StaminaEnableAbovePercent, 0, 100);
        staminaOff.Increment = staminaOn.Increment = 1;
        content.Controls.Add(new Label { AutoSize = true, MaximumSize = new Size(590, 0),
            Text = "Follow mantém UMA OU DUAS SETAS pressionadas (diagonais), sem cortar quinas bloqueadas. Para sem caminho/perigos conhecidos; não detecta traps invisíveis. Soltar a tecla ou F12 libera ambas.\nAuto Stamina usa os percentuais acima e mantém a decisão entre eles; o limite para religar deve ser maior.\nSalvar desarma todos os módulos; não altera bindings do jogo." });
        var save = new Button { Text = "SALVAR E APLICAR", Width = 200, Height = 34 };
        content.Controls.Add(save);
        save.Click += (_, _) =>
        {
            try
            {
                var updated = settings with
                {
                    UseAttack = useAA.Checked, UseTool = useTool.Checked,
                    AttackMode = (AutoAttackMode)attackMode.SelectedIndex,
                    FanRange = (double)fanRange.Value, FanHalfWidth = (double)fanWidth.Value,
                    HisuyFanRange = (double)hisuyRange.Value, HisuyFanHalfWidth = (double)hisuyWidth.Value,
                    AttackKey = attack.SelectedItem?.ToString() ?? "", ToolKey = tool.SelectedItem?.ToString() ?? "",
                    AttackIntervalMs = (long)aaDelay.Value, ToolIntervalMs = (long)toolDelay.Value,
                    WeaponRadius = (double)radius.Value, ToolRange = (double)range.Value, ToolHalfWidth = (double)halfWidth.Value,
                    FollowHoldKey = hold.SelectedItem?.ToString() ?? "", FollowStopDistance = (double)stop.Value,
                    FollowStepIntervalMs = (long)moveDelay.Value,
                    StaminaDisableBelowPercent = (int)staminaOff.Value,
                    StaminaEnableAbovePercent = (int)staminaOn.Value
                };
                updated.Validate(); Settings = updated; DialogResult = DialogResult.OK;
            }
            catch (ArgumentException ex) { MessageBox.Show(this, ex.Message, "Configuração inválida"); }
        };
    }
}
