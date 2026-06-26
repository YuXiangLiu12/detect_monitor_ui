using System.IO.Ports;

namespace LeakMonitor;

/// <summary>
/// 串口连接配置对话框
/// </summary>
public class SerialConfigDialog : Form
{
    private ComboBox _cmbPort = null!;
    private ComboBox _cmbBaud = null!;
    private ComboBox _cmbDataBits = null!;
    private ComboBox _cmbParity = null!;
    private ComboBox _cmbStopBits = null!;
    private ComboBox _cmbProtocol = null!;
    private Button _btnConnect = null!;
    private Button _btnCancel = null!;

    /// <summary>用户选择的 COM 口</summary>
    public string SelectedPort => _cmbPort.SelectedItem?.ToString() ?? "";

    /// <summary>用户选择的波特率</summary>
    public int SelectedBaudRate => int.Parse(_cmbBaud.SelectedItem?.ToString() ?? "115200");

    /// <summary>用户选择的数据位</summary>
    public int SelectedDataBits => int.Parse(_cmbDataBits.SelectedItem?.ToString() ?? "8");

    /// <summary>用户选择的校验位</summary>
    public Parity SelectedParity
    {
        get
        {
            return _cmbParity.SelectedItem?.ToString() switch
            {
                "Odd" => Parity.Odd,
                "Even" => Parity.Even,
                "Mark" => Parity.Mark,
                "Space" => Parity.Space,
                _ => Parity.None
            };
        }
    }

    /// <summary>用户选择的停止位</summary>
    public StopBits SelectedStopBits
    {
        get
        {
            return _cmbStopBits.SelectedItem?.ToString() switch
            {
                "1.5" => StopBits.OnePointFive,
                "2" => StopBits.Two,
                _ => StopBits.One
            };
        }
    }

    /// <summary>用户选择的协议类型</summary>
    public string SelectedProtocol => _cmbProtocol.SelectedItem?.ToString() ?? "binary";

    /// <summary>
    /// DPI 缩放辅助 —— 以 96 DPI 为基准，将逻辑像素值转换为当前 DPI 的实际像素值
    /// </summary>
    private int S(int value96) => LogicalToDeviceUnits(value96);

    public SerialConfigDialog()
    {
        // DPI 自适应，与 MainForm 保持一致
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(96F, 96F);

        Text = "串口连接设置";
        Size = new Size(S(400), S(420));
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;

        BuildLayout();
        RefreshPortList();
    }

    private void BuildLayout()
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 9,
            Padding = new Padding(S(20), S(20), S(20), S(16)),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(80)));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        // 行: 标题 / port / baud / databits / parity / stopbits / protocol / 空 / 按钮
        for (int i = 0; i < 9; i++)
            table.RowStyles.Add(new RowStyle(i == 7 ? SizeType.Percent : SizeType.Absolute,
                i == 7 ? 100 : S(38)));
        Controls.Add(table);

        // ---- Row 0: 标题 ----
        var title = new Label
        {
            Text = "串口连接参数",
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, S(8))
        };
        table.Controls.Add(title, 0, 0);
        table.SetColumnSpan(title, 2);

        // ---- Row 1: COM 口 ----
        var lblPort = new Label { Text = "COM 口:", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        _cmbPort = new ComboBox
        {
            Width = S(200),
            Height = S(30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, S(2), 0, 0)
        };
        table.Controls.Add(lblPort, 0, 1);
        table.Controls.Add(_cmbPort, 1, 1);

        // ---- Row 2: 波特率 ----
        var lblBaud = new Label { Text = "波特率:", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        _cmbBaud = new ComboBox
        {
            Width = S(200),
            Height = S(30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, S(2), 0, 0)
        };
        _cmbBaud.Items.AddRange(new object[] { "4800", "9600", "19200", "38400", "57600", "115200", "230400", "460800", "921600" });
        _cmbBaud.SelectedItem = "115200";
        table.Controls.Add(lblBaud, 0, 2);
        table.Controls.Add(_cmbBaud, 1, 2);

        // ---- Row 3: 数据位 ----
        var lblDataBits = new Label { Text = "数据位:", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        _cmbDataBits = new ComboBox
        {
            Width = S(200),
            Height = S(30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, S(2), 0, 0)
        };
        _cmbDataBits.Items.AddRange(new object[] { "5", "6", "7", "8" });
        _cmbDataBits.SelectedItem = "8";
        table.Controls.Add(lblDataBits, 0, 3);
        table.Controls.Add(_cmbDataBits, 1, 3);

        // ---- Row 4: 校验位 ----
        var lblParity = new Label { Text = "校验位:", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        _cmbParity = new ComboBox
        {
            Width = S(200),
            Height = S(30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, S(2), 0, 0)
        };
        _cmbParity.Items.AddRange(new object[] { "None", "Odd", "Even", "Mark", "Space" });
        _cmbParity.SelectedItem = "None";
        table.Controls.Add(lblParity, 0, 4);
        table.Controls.Add(_cmbParity, 1, 4);

        // ---- Row 5: 停止位 ----
        var lblStopBits = new Label { Text = "停止位:", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        _cmbStopBits = new ComboBox
        {
            Width = S(200),
            Height = S(30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, S(2), 0, 0)
        };
        _cmbStopBits.Items.AddRange(new object[] { "1", "1.5", "2" });
        _cmbStopBits.SelectedItem = "1";
        table.Controls.Add(lblStopBits, 0, 5);
        table.Controls.Add(_cmbStopBits, 1, 5);

        // ---- Row 6: 协议类型 ----
        var lblProtocol = new Label { Text = "协议类型:", AutoSize = true, Margin = new Padding(0, S(6), 0, 0) };
        _cmbProtocol = new ComboBox
        {
            Width = S(200),
            Height = S(30),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Margin = new Padding(0, S(2), 0, 0)
        };
        _cmbProtocol.Items.AddRange(new object[] { "binary", "demo_ascii" });
        _cmbProtocol.SelectedItem = "binary";
        table.Controls.Add(lblProtocol, 0, 6);
        table.Controls.Add(_cmbProtocol, 1, 6);

        // ---- Row 7: 空白占位 ----
        // (Percent row takes remaining space)

        // ---- Row 8: 按钮 ----
        var btnPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Margin = new Padding(0, S(8), 0, 0)
        };

        _btnCancel = new Button
        {
            Text = "取消",
            Width = S(90),
            Height = S(34),
            DialogResult = DialogResult.Cancel,
            Margin = new Padding(S(12), 0, 0, 0)
        };
        _btnConnect = new Button
        {
            Text = "连接",
            Width = S(90),
            Height = S(34),
            BackColor = Color.FromArgb(0, 120, 215),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            UseVisualStyleBackColor = false,
            DialogResult = DialogResult.OK
        };
        _btnConnect.FlatAppearance.BorderSize = 0;
        _btnConnect.Click += (s, e) =>
        {
            string? port = _cmbPort.SelectedItem?.ToString();
            if (string.IsNullOrEmpty(port) || port == "(无可用串口)" || !port.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("请选择有效的 COM 口。", "提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;  // 阻止关闭对话框
            }
        };

        btnPanel.Controls.Add(_btnCancel);
        btnPanel.Controls.Add(_btnConnect);
        table.Controls.Add(btnPanel, 1, 8);

        // 将 CancelButton 绑定到 Esc 键
        CancelButton = _btnCancel;
        AcceptButton = _btnConnect;
    }

    /// <summary>
    /// 刷新可用串口列表
    /// </summary>
    private void RefreshPortList()
    {
        _cmbPort.Items.Clear();
        string[] ports;
        try { ports = SerialReader.GetAvailablePorts(); }
        catch { ports = Array.Empty<string>(); }

        foreach (var p in ports)
            _cmbPort.Items.Add(p);

        if (_cmbPort.Items.Count > 0)
            _cmbPort.SelectedIndex = 0;
        else
            _cmbPort.Items.Add("(无可用串口)");
    }
}
