using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using NinOnlineAutoAssist.Core.Security;
using NinOnlineAutoAssist.Diagnostics;

namespace NinOnlineAutoAssist.App;

public sealed class ResourceInspectionForm : Form
{
	private readonly AccessLease _access;

	private readonly Timer _permissionTimer = new Timer
	{
		Interval = 100
	};

	private readonly ComboBox _processes = new ComboBox
	{
		Width = 200,
		DropDownStyle = ComboBoxStyle.DropDownList
	};

	private readonly Button _read = new Button
	{
		Text = "Ler memória",
		AutoSize = true
	};

	private readonly Button _save = new Button
	{
		Text = "Exportar JSON",
		AutoSize = true,
		Enabled = false
	};

	private readonly TextBox _filter = new TextBox
	{
		Width = 220,
		PlaceholderText = "Filtrar nome do jutsu"
	};

	private readonly Label _status = new Label
	{
		Dock = DockStyle.Fill,
		AutoSize = false
	};

	private readonly DataGridView _grid = new DataGridView
	{
		Dock = DockStyle.Fill,
		ReadOnly = true,
		AutoGenerateColumns = true,
		AllowUserToAddRows = false,
		AllowUserToDeleteRows = false,
		AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells
	};

	private ResourceInspection? _report;

	public ResourceInspectionForm(AccessLease access)
	{
		base.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
		_access = access;
		if (!access.Allows(AppFeature.ResourceInspection))
		{
			throw new AccessDeniedException("Inspeção não autorizada.");
		}
		_permissionTimer.Tick += delegate
		{
			if (!_access.Allows(AppFeature.ResourceInspection))
			{
				_report = null;
				_grid.DataSource = null;
				Close();
			}
		};
		_permissionTimer.Start();
		base.FormClosed += delegate
		{
			_permissionTimer.Stop();
			_permissionTimer.Dispose();
			_report = null;
		};
		Text = "Recursos observados — Nin Online (somente leitura)";
		base.Size = new Size(1060, 650);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			RowCount = 3,
			ColumnCount = 1,
			Padding = new Padding(10)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 90f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill
		};
		flowLayoutPanel.Controls.AddRange(new Control[4] { _processes, _read, _save, _filter });
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel.Controls.Add(_status, 0, 1);
		tableLayoutPanel.Controls.Add(_grid, 0, 2);
		base.Controls.Add(tableLayoutPanel);
		Process[] processesByName = Process.GetProcessesByName("NinOnline");
		foreach (Process process in processesByName)
		{
			using (process)
			{
				_processes.Items.Add(process.Id);
			}
		}
		if (_processes.Items.Count > 0)
		{
			_processes.SelectedIndex = 0;
		}
		_status.Text = "Selecione o processo e clique em Ler memória (pode levar até alguns segundos).\nSão definições carregadas, não habilidades equipadas. CDTime/CastTime/Range são valores brutos; unidades não validadas.\nNão lê chat, credenciais ou rede. Não escreve memória, não injeta código e não envia teclas.";
		_read.Click += async delegate
		{
			await ReadAsync();
		};
		_filter.TextChanged += delegate
		{
			RefreshGrid();
		};
		_save.Click += delegate
		{
			SaveReport();
		};
	}

	private async Task ReadAsync()
	{
		if (!_access.Allows(AppFeature.ResourceInspection))
		{
			return;
		}
		object selectedItem = _processes.SelectedItem;
		if (selectedItem is int)
		{
			int pid = (int)selectedItem;
			Button read = _read;
			bool enabled = (_save.Enabled = false);
			read.Enabled = enabled;
			_report = null;
			RefreshGrid();
			_status.Text = "Lendo catálogo em modo passivo; nenhum estado de combate será inferido…";
			try
			{
				ResourceInspection resourceInspection = await Task.Run(() => (!_access.Allows(AppFeature.ResourceInspection)) ? null : LiveInspection.Inspect(pid, () => _access.Allows(AppFeature.ResourceInspection)));
				if (!base.IsDisposed && _access.Allows(AppFeature.ResourceInspection) && (object)resourceInspection != null)
				{
					_report = resourceInspection;
					RefreshGrid();
					_status.Text = $"OBSERVADO em {resourceInspection.ObservedAtUtc.ToLocalTime():HH:mm:ss} · PID {pid} · {resourceInspection.Spells.Count} definições · {resourceInspection.ElapsedMs} ms.\nVarredura {(resourceInspection.Truncated ? "PARCIAL (limite de tempo/objetos)" : "concluída")} · falhas de leitura de jutsus: {resourceInspection.FailedSpellReads}. GC pode alterar objetos durante a leitura.\n" + "CDTime/CastTime/Range brutos (unidades não validadas). CurrentLevel não indica nível equipado. Chakra atual/alvo/cooldown restante desconhecidos.";
					_save.Enabled = true;
				}
				return;
			}
			catch (Exception ex)
			{
				if (!base.IsDisposed)
				{
					_status.Text = "Dados indisponíveis: " + ex.Message + "\nNenhum bypass ou fallback inventado foi aplicado.";
				}
				return;
			}
			finally
			{
				if (!base.IsDisposed)
				{
					_read.Enabled = true;
				}
			}
		}
		_status.Text = "NinOnline.exe não encontrado. Abra o jogo e reabra esta janela.";
	}

	private void RefreshGrid()
	{
		_grid.DataSource = _report?.Spells.Where((ObservedSpell s) => s.Name.Contains(_filter.Text, StringComparison.OrdinalIgnoreCase)).ToList();
	}

	private void SaveReport()
	{
		if ((object)_report == null || !_access.Allows(AppFeature.ResourceInspection))
		{
			return;
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "JSON|*.json",
			FileName = "ninonline-resources.json"
		};
		if (saveFileDialog.ShowDialog(this) != DialogResult.OK || !_access.Allows(AppFeature.ResourceInspection))
		{
			return;
		}
		try
		{
			File.WriteAllText(saveFileDialog.FileName, JsonSerializer.Serialize(_report, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex) when (((ex is IOException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			_status.Text = "Não foi possível exportar: " + ex.Message;
		}
	}
}
