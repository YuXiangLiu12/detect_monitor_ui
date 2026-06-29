using System.Diagnostics;
using System.IO.Ports;

namespace LeakMonitor;

/// <summary>
/// 主窗口 —— 完整的 UI 布局
///
/// ★ 二次开发：修改界面布局，改 _BuildLayout() 方法即可
/// </summary>
public class MainForm : Form
{
    // ============ 核心组件 ============
    private readonly BaseProtocolParser _parser;
    private readonly SerialReader _serialReader;
    private readonly DataHistory _history;
    private readonly System.Windows.Forms.Timer _pollTimer;

    // ============ 标题栏控件 ============
    private ToolStripMenuItem _menuConnect = null!;   // 菜单栏中的"连接"项

    // ============ 主体分栏控件 ============
    private SplitContainer _splitMain = null!;     // 左右分栏（实时数据 | 历史数据）
    private SplitContainer _rightSplit = null!;    // 右侧上下分栏（实时状态 | 历史表格）
    private SplitContainer _horizSplit = null!;    // 上下分栏（主体 | 事件日志）

    // ============ 右侧实时数据控件 ============
    private Label _lblRightStatus = null!;      // 管道状态（正常/报警原因）
    private Label _lblRightDistance = null!;    // 液漏距离
    private Label _lblRightTimestamp = null!;   // 最新数据时间戳

    // ============ 历史数据表格控件 ============
    private DataGridView _gridHistory = null!;

    // ============ 管道监控灯（一道 / 二道，各10个监控站）============
    private Label[] _pipe1Lights = null!;  // 一道 10 个监控灯 ●
    private Label[] _pipe2Lights = null!;  // 二道 10 个监控灯 ●

    // ============ 报警状态 ============
    private System.Windows.Forms.Timer _flashTimer = null!;  // 报警闪烁定时器
    private bool _flashOn;                      // 闪烁状态
    private uint _currentAlarmCode;             // 当前报警码

    // ============ 事件日志控件 ============
    private RichTextBox _txtLog = null!;
    private TextBox _txtRawFrame = null!;

    // ============ 连接参数（从对话框获取后保存） ============
    private string _connectedPort = "";
    private int _connectedBaudRate = 115200;
    private int _connectedDataBits = 8;
    private Parity _connectedParity = Parity.None;
    private StopBits _connectedStopBits = StopBits.One;
    private string _connectedProtocol = "binary";

    // ============ 状态栏控件 ============
    private StatusStrip _statusStrip = null!;
    private ToolStripStatusLabel _statusLabel = null!;
    private ToolStripStatusLabel _statusPortInfo = null!;
    private ToolStripStatusLabel _statusTime = null!;

    // (统计数据已移至 SerialReader 内部)

    /// <summary>
    /// DPI 缩放辅助 —— 以 96 DPI 为基准，将逻辑像素值转换为当前 DPI 的实际像素值
    /// 所有硬编码的布局数值（Padding、Size、Width/Height 等）都应通过此方法缩放
    /// </summary>
    private int S(int value96) => LogicalToDeviceUnits(value96);

    public MainForm()
    {
        // ---- DPI 自适应设置 ----
        // 在不同缩放比例（100%/125%/150%/175%）的电脑上避免控件重叠
        AutoScaleMode = AutoScaleMode.Font;
        AutoScaleDimensions = new SizeF(96F, 96F);

        // ---- 硬编码配置 ----
        const string protocolType = "binary";          // 协议类型: demo_ascii / binary
        const string windowTitle = "徐圩核电站液态排放物双层管线泄漏监测系统 v2.0";
        const int pollIntervalMs = 100;

        // 创建协议解析器
        _parser = ProtocolRegistry.CreateParser(protocolType);

        // 创建串口读取器
        _serialReader = new SerialReader(_parser);
        _serialReader.OnStatusChanged += OnSerialStatusChanged;
        _serialReader.OnError += OnSerialError;

        // 数据历史（最近 300 条）
        _history = new DataHistory(300);

        // UI 轮询定时器
        _pollTimer = new System.Windows.Forms.Timer
        {
            Interval = pollIntervalMs
        };
        _pollTimer.Tick += OnPollTimerTick;

        // 报警闪烁定时器（500ms 切换一次）
        _flashTimer = new System.Windows.Forms.Timer
        {
            Interval = 500
        };
        _flashTimer.Tick += OnFlashTimerTick;

        // ---- 构建 UI ----
        Text = windowTitle;
        // 窗体大小按屏幕工作区比例计算，适应不同 DPI
        var screen = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1040);
        Size = new Size(
            Math.Max(S(1000), screen.Width * 65 / 100),  // 屏幕宽度的 65%，最小 1000（DPI 缩放）
            Math.Max(S(750), screen.Height * 80 / 100)    // 屏幕高度的 80%，最小 750（DPI 缩放）
        );
        MinimumSize = new Size(S(900), S(680));
        StartPosition = FormStartPosition.CenterScreen;

        BuildLayout();

        // 窗体加载完成后设置分栏参数（此时控件已有实际尺寸）
        // 注意：min size 必须留出余地，否则在小屏/高 DPI 设备上会因
        //       "SplitterDistance 必须在 Panel1MinSize 和 Width-Panel2MinSize 之间" 而崩溃
        Load += (s, e) =>
        {
            // 左右分栏：左侧占 55%，min size 按比例而非绝对值确保兼容小屏幕
            _splitMain.Panel1MinSize = Math.Min(_splitMain.Width * 35 / 100, S(300));
            _splitMain.Panel2MinSize = Math.Min(_splitMain.Width * 25 / 100, S(250));
            _splitMain.SplitterDistance = Math.Max(_splitMain.Panel1MinSize,
                Math.Min(_splitMain.Width - _splitMain.Panel2MinSize,
                         _splitMain.Width * 55 / 100));

            // 右侧上下分栏：上方实时状态 ~35%
            _rightSplit.Panel1MinSize = Math.Min(_rightSplit.Height * 20 / 100, S(100));
            _rightSplit.Panel2MinSize = Math.Min(_rightSplit.Height * 25 / 100, S(120));
            _rightSplit.SplitterDistance = Math.Max(_rightSplit.Panel1MinSize,
                Math.Min(_rightSplit.Height - _rightSplit.Panel2MinSize,
                         _rightSplit.Height * 35 / 100));

            // 上下分栏：下方日志区初始较小
            var horizDist = _horizSplit.Height - S(140);
            _horizSplit.SplitterDistance = Math.Max(_horizSplit.Panel1MinSize,
                Math.Min(_horizSplit.Height - _horizSplit.Panel2MinSize, horizDist));
        };
    }

    // ================================================================
    // UI 布局构建
    // ★ 二次开发：修改界面就在这里
    // ================================================================
    private void BuildLayout()
    {
        // ---- 菜单栏 ----
        var menuStrip = new MenuStrip();
        var fileMenu = new ToolStripMenuItem("文件(&F)");
        fileMenu.DropDownItems.Add("导出数据(&E)...", null, (s, e) => ExportData());
        fileMenu.DropDownItems.Add(new ToolStripSeparator());
        fileMenu.DropDownItems.Add("退出(&X)", null, (s, e) => Close());
        menuStrip.Items.Add(fileMenu);

        // "连接"菜单项（点击触发连接/断开，替代原来的串口设置）
        _menuConnect = new ToolStripMenuItem("连接(&C)");
        _menuConnect.Click += OnConnectClick;
        menuStrip.Items.Add(_menuConnect);

        var viewMenu = new ToolStripMenuItem("视图(&V)");
        viewMenu.DropDownItems.Add("清除日志(&C)", null, (s, e) =>
        {
            _txtLog.Clear();
            _gridHistory.Rows.Clear();
            _history.Clear();
        });
        menuStrip.Items.Add(viewMenu);

        var helpMenu = new ToolStripMenuItem("帮助(&H)");
        helpMenu.DropDownItems.Add("关于(&A)...", null, (s, e) => ShowAbout());
        menuStrip.Items.Add(helpMenu);

        MainMenuStrip = menuStrip;
        Controls.Add(menuStrip);

        // ---- 主布局 TableLayoutPanel ----
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(S(8), S(20), S(8), S(8)),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, S(80)));   // Logo 标题
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 主体 + 日志（可拖拽分隔）
        Controls.Add(mainLayout);

        // ---- Row 0: 标题面板 ----
        var titlePanel = BuildTitlePanel();
        mainLayout.Controls.Add(titlePanel, 0, 0);

        // ---- Row 1: 水平 SplitContainer（上：左右分栏主体 / 下：事件日志+原始帧） ----
        var horizSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = S(5),
            Panel1MinSize = S(250),
            Panel2MinSize = S(100)
        };
        _horizSplit = horizSplit;
        mainLayout.Controls.Add(horizSplit, 0, 1);

        // Panel1（上）：左右分栏主体
        horizSplit.Panel1.Controls.Add(BuildMainPanel());

        // Panel2（下）：事件日志 + 原始帧
        horizSplit.Panel2.Controls.Add(BuildLogPanel());

        // ---- 状态栏 ----
        _statusStrip = new StatusStrip();
        _statusLabel = new ToolStripStatusLabel("就绪");
        _statusPortInfo = new ToolStripStatusLabel("未连接");
        _statusTime = new ToolStripStatusLabel(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        _statusStrip.Items.Add(_statusLabel);
        _statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        _statusStrip.Items.Add(_statusPortInfo);
        _statusStrip.Items.Add(new ToolStripStatusLabel(" | "));
        _statusStrip.Items.Add(_statusTime);
        Controls.Add(_statusStrip);

        // 定时更新状态栏时间
        var clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        clockTimer.Tick += (s, e) => _statusTime.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        clockTimer.Start();
    }

    // ================================================================
    // 标题面板 —— 居中软件 Logo / 标题
    // ================================================================
    private Panel BuildTitlePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(240, 245, 252)
        };

        // ---- 软件标题（Logo 文字），居中显示 ----
        var lblLogo = new Label
        {
            Text = "徐圩核电站液态排放物双层管线泄漏监测",
            Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 80, 160),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };

        panel.Controls.Add(lblLogo);
        return panel;
    }

    // ================================================================
    // 主体面板 —— 左右分栏（实时数据 | 右侧：实时状态 + 历史数据）
    // ================================================================
    private SplitContainer BuildMainPanel()
    {
        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterWidth = S(4)
        };
        _splitMain = split;

        // 左面板：实时管道监控灯
        split.Panel1.Controls.Add(BuildRealTimePanel());

        // 右面板：上下分栏 — 实时状态（上）+ 历史数据表格（下）
        var rightSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterWidth = S(4)
            // Panel1MinSize / Panel2MinSize / SplitterDistance
            // 延迟到 Load 中设置，避免构造阶段控件无实际尺寸导致异常
        };
        rightSplit.Panel1.Controls.Add(BuildRightRealtimePanel());
        rightSplit.Panel2.Controls.Add(BuildHistoryPanel());
        _rightSplit = rightSplit;
        split.Panel2.Controls.Add(rightSplit);

        return split;
    }

    // ================================================================
    // 实时数据面板（左侧）—— 双管道监控灯 + 报警原因
    // ================================================================
    private Panel BuildRealTimePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(248, 248, 248)
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(S(8), S(6), S(8), S(6))
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));   // 一道
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50));   // 二道
        panel.Controls.Add(table);

        table.Controls.Add(BuildPipelinePanel("一道", Color.FromArgb(0, 100, 180), out _pipe1Lights), 0, 0);
        table.Controls.Add(BuildPipelinePanel("二道", Color.FromArgb(0, 140, 80), out _pipe2Lights), 0, 1);

        return panel;
    }

    /// <summary>
    /// 构建一条管道监控面板 —— 带管道壁连线效果
    /// 每条管道代表 20 公里（10 个监控站 × 每站 2 公里）
    /// </summary>
    private Panel BuildPipelinePanel(string name, Color accentColor, out Label[] lights)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(S(4)),
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(S(8), S(4), S(8), S(2))
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 管道名称 + 长度
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 管道主体
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // 底部留白
        panel.Controls.Add(layout);

        // ---- 管道名称 + 总长 ----
        var lblName = new Label
        {
            Text = $"{name}（10 区 × 2km = 20km）",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = accentColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, S(2))
        };
        layout.Controls.Add(lblName, 0, 0);

        // ---- 管道主体（管壁连线效果）----
        var pipeBody = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(235, 240, 248),
            Padding = new Padding(0, S(4), 0, S(4))
        };

        // 管道上壁线
        var pipeTop = new Panel
        {
            Dock = DockStyle.Top,
            Height = S(3),
            BackColor = Color.FromArgb(160, 175, 200)
        };
        // 管道下壁线
        var pipeBottom = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = S(3),
            BackColor = Color.FromArgb(160, 175, 200)
        };

        // 监控灯 + 拓扑连线均匀分布网格
        // 20 列: 连线-灯-连线-灯-...-连线-灯（10灯 + 10连线，左侧起线）
        var lightsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 20,
            RowCount = 1
        };

        // 列宽：偶数列=连线(窄)，奇数列=灯(宽)
        for (int i = 0; i < 20; i++)
        {
            if (i % 2 == 0)
                lightsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(16)));   // 拓扑连线
            else
                lightsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 10));       // 监控灯
        }

        pipeBody.Controls.Add(pipeTop);
        pipeBody.Controls.Add(pipeBottom);
        pipeBody.Controls.Add(lightsGrid);

        lights = new Label[10];
        for (int i = 0; i < 10; i++)
        {
            int lightCol = i * 2 + 1;   // 奇数列: 1,3,5,...,19
            int lineCol = i * 2;        // 偶数列: 0,2,4,...,18

            // 连线（每个灯左侧都有一条连线，包括第1个灯）
            lightsGrid.Controls.Add(BuildConnectingLine(), lineCol, 0);

            // 监控灯（● + 区号标签紧贴下方）
            lightsGrid.Controls.Add(BuildLightUnit(i + 1, out lights[i]), lightCol, 0);
        }

        layout.Controls.Add(pipeBody, 0, 1);
        return panel;
    }

    /// <summary>
    /// 构建单个监控灯单元（● 灯 + 区号标签紧贴下方，在连线之下）
    /// </summary>
    private Panel BuildLightUnit(int zoneNumber, out Label lightLabel)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(S(1))
        };

        // 内表：上留白 → ● 灯 → 区号（紧贴） → 下留白
        var innerTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        innerTable.RowStyles.Add(new RowStyle(SizeType.Percent, 45));   // 上方留白
        innerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // ● 灯
        innerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 区号（紧贴 ●）
        innerTable.RowStyles.Add(new RowStyle(SizeType.Percent, 55));   // 下方留白
        panel.Controls.Add(innerTable);

        // ● 灯
        var light = new Label
        {
            Text = "●",
            Font = new Font("Microsoft YaHei UI", 22F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 180, 80),   // 绿色 = 正常
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 0)
        };

        // 区号标签：紧贴 ● 下方（连线之下）
        var lblZone = new Label
        {
            Text = $"{zoneNumber}区",
            Font = new Font("Microsoft YaHei UI", 7F),
            ForeColor = Color.FromArgb(110, 110, 110),
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, 0, 0, 0)
        };

        innerTable.Controls.Add(light, 0, 1);
        innerTable.Controls.Add(lblZone, 0, 2);

        lightLabel = light;
        return panel;
    }

    /// <summary>
    /// 构建拓扑连线 —— 水平短线，与 ● 灯同一高度，左右拉伸填满列宽
    /// </summary>
    private Panel BuildConnectingLine()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        // 与 BuildLightUnit 使用相同的 4 行内表，保证连线与 ● 灯在同一水平线
        var innerTable = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        innerTable.RowStyles.Add(new RowStyle(SizeType.Percent, 45));   // 上方留白
        innerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 连线（与 ● 对齐）
        innerTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 空白（与区号同高）
        innerTable.RowStyles.Add(new RowStyle(SizeType.Percent, 55));   // 下方留白
        panel.Controls.Add(innerTable);

        // 水平连线
        var line = new Panel
        {
            Height = S(2),
            BackColor = Color.FromArgb(160, 175, 200),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(S(2), 0, S(2), 0)
        };
        innerTable.Controls.Add(line, 0, 1);

        return panel;
    }

    // ================================================================
    // 历史数据面板（右侧）—— DataGridView 表格
    // ================================================================
    private Panel BuildHistoryPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(S(10), S(10), S(10), S(10))
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, S(26)));   // 标题
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 表格
        panel.Controls.Add(table);

        var title = new Label
        {
            Text = "历史数据",
            Dock = DockStyle.Fill,
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        table.Controls.Add(title, 0, 0);

        _gridHistory = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Color.FromArgb(245, 245, 245),
            BorderStyle = BorderStyle.None,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        };
        _gridHistory.DefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F);
        _gridHistory.ColumnHeadersDefaultCellStyle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);

        // 三列：时间 | 报警站点 | 报警原因（仅报警数据）
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "时间",
            Name = "ColTimestamp",
            FillWeight = 38,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "报警站点",
            Name = "ColStation",
            FillWeight = 30,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });
        _gridHistory.Columns.Add(new DataGridViewTextBoxColumn
        {
            HeaderText = "报警原因",
            Name = "ColReason",
            FillWeight = 32,
            DefaultCellStyle = new DataGridViewCellStyle { Alignment = DataGridViewContentAlignment.MiddleCenter }
        });

        table.Controls.Add(_gridHistory, 0, 1);

        return panel;
    }

    // ================================================================
    // 右侧实时数据面板 —— 管道状态、报警原因、液漏距离
    // ================================================================
    private Panel BuildRightRealtimePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(S(16), S(12), S(16), S(12))
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 标题
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 状态（主显示区）
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 液漏距离
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 时间戳
        panel.Controls.Add(table);

        // ---- 标题 ----
        var lblTitle = new Label
        {
            Text = "实时管道状态",
            Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 80, 160),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, S(8))
        };
        table.Controls.Add(lblTitle, 0, 0);

        // ---- 状态文字（大字居中）----
        _lblRightStatus = new Label
        {
            Text = "等待数据...",
            Font = new Font("Microsoft YaHei UI", 18F, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 100, 100),
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleCenter,
            Dock = DockStyle.Fill
        };
        table.Controls.Add(_lblRightStatus, 0, 1);

        // ---- 液漏距离（报警时显示）----
        _lblRightDistance = new Label
        {
            Text = "",
            Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold),
            ForeColor = Color.Red,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, S(4), 0, 0),
            Visible = false
        };
        table.Controls.Add(_lblRightDistance, 0, 2);

        // ---- 最新时间戳 ----
        _lblRightTimestamp = new Label
        {
            Text = "",
            Font = new Font("Microsoft YaHei UI", 8F),
            ForeColor = Color.Gray,
            AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter,
            Anchor = AnchorStyles.None,
            Margin = new Padding(0, S(2), 0, 0)
        };
        table.Controls.Add(_lblRightTimestamp, 0, 3);

        return panel;
    }

    // ================================================================
    // 日志面板 —— 事件日志 + 最新原始帧（比例布局）
    // ================================================================
    private Panel BuildLogPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(S(10), S(8), S(10), S(8))
        };

        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 0: "事件日志"
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); // 1: 事件日志 RichTextBox
        table.RowStyles.Add(new RowStyle(SizeType.AutoSize));   // 2: "最新原始帧"
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, S(22))); // 3: 原始帧 TextBox
        panel.Controls.Add(table);

        var lblLogTitle = new Label
        {
            Text = "事件日志",
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, S(2))
        };
        table.Controls.Add(lblLogTitle, 0, 0);

        _txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            ReadOnly = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220),
            BorderStyle = BorderStyle.None
        };
        table.Controls.Add(_txtLog, 0, 1);

        var lblRawTitle = new Label
        {
            Text = "最新原始帧",
            Font = new Font("Microsoft YaHei UI", 9F),
            ForeColor = Color.Gray,
            AutoSize = true,
            Margin = new Padding(0, S(6), 0, S(2))
        };
        table.Controls.Add(lblRawTitle, 0, 2);

        _txtRawFrame = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Consolas", 9F),
            ReadOnly = true,
            BackColor = Color.FromArgb(240, 240, 240),
            BorderStyle = BorderStyle.FixedSingle
        };
        table.Controls.Add(_txtRawFrame, 0, 3);

        return panel;
    }

    // ================================================================
    // 事件处理
    // ================================================================

    private void OnConnectClick(object? sender, EventArgs e)
    {
        if (_serialReader.IsConnected)
        {
            // 断开
            _serialReader.Disconnect();
            _pollTimer.Stop();
            _flashTimer.Stop();
            _menuConnect.Text = "连接(&C)";
            _statusPortInfo.Text = "未连接";
            // 恢复所有监控灯为绿色
            _currentAlarmCode = 0;
            _flashOn = false;
            for (int i = 0; i < 10; i++)
            {
                if (_pipe1Lights[i] != null) _pipe1Lights[i].ForeColor = Color.FromArgb(0, 180, 80);
                if (_pipe2Lights[i] != null) _pipe2Lights[i].ForeColor = Color.FromArgb(0, 180, 80);
            }
            AppendLog("INFO", "已断开连接");
            return;
        }

        // 连接 —— 打开串口配置对话框
        using var dialog = new SerialConfigDialog();
        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        // 保存用户选择的参数
        _connectedPort = dialog.SelectedPort;
        _connectedBaudRate = dialog.SelectedBaudRate;
        _connectedDataBits = dialog.SelectedDataBits;
        _connectedParity = dialog.SelectedParity;
        _connectedStopBits = dialog.SelectedStopBits;
        _connectedProtocol = dialog.SelectedProtocol;

        try
        {
            _serialReader.Connect(_connectedPort, _connectedBaudRate,
                _connectedDataBits, _connectedParity, _connectedStopBits);

            _pollTimer.Start();
            _menuConnect.Text = "断开(&C)";

            string parityStr = _connectedParity switch
            {
                Parity.Odd => "O",
                Parity.Even => "E",
                Parity.Mark => "M",
                Parity.Space => "S",
                _ => "N"
            };
            string stopBitsStr = _connectedStopBits switch
            {
                StopBits.OnePointFive => "1.5",
                StopBits.Two => "2",
                _ => "1"
            };
            _statusPortInfo.Text = $"{_connectedPort} {_connectedBaudRate} {_connectedDataBits}{parityStr}{stopBitsStr}";

            AppendLog("INFO", $"已连接到 {_connectedPort} @ {_connectedBaudRate} bps, {_connectedDataBits}{parityStr}{stopBitsStr}, 协议: {_connectedProtocol}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"连接失败:\n{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog("ERROR", $"连接失败: {ex.Message}");
        }
    }

    private void OnSerialStatusChanged(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnSerialStatusChanged(status));
            return;
        }
        AppendLog("INFO", status);
    }

    private void OnSerialError(Exception ex)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => OnSerialError(ex));
            return;
        }
        AppendLog("ERROR", ex.Message);
    }

    /// <summary>
    /// 定时轮询数据队列（在 UI 线程中执行）
    /// </summary>
    private void OnPollTimerTick(object? sender, EventArgs e)
    {
        int processed = 0;
        while (_serialReader.TryDequeue(out var envelope) && processed < 20)
        {
            processed++;
            if (envelope.IsValid && envelope.Data != null)
            {
                var data = envelope.Data;
                _txtRawFrame.Text = data.RawFrame;
                AppendHistoryLog(data);
                _history.Add(data);

                // ---- 报警状态更新（当前仅映射一道 1 区）----
                _currentAlarmCode = data.AlarmCode;
                UpdateAlarmDisplay();

                // ---- 右侧实时面板更新 ----
                UpdateRightRealtimePanel(data);
            }
            else if (!string.IsNullOrEmpty(envelope.Error))
            {
                AppendLog("WARN", envelope.Error + (string.IsNullOrEmpty(envelope.RawLine) ? "" : $": {envelope.RawLine}"));
            }
        }
    }

    /// <summary>
    /// 根据当前报警码更新一道 1 区监控灯和报警原因显示
    /// </summary>
    private void UpdateAlarmDisplay()
    {
        bool isAlarm = _currentAlarmCode != 0;

        if (isAlarm)
        {
            // 启动闪烁
            if (!_flashTimer.Enabled)
                _flashTimer.Start();
        }
        else
        {
            // 恢复正常
            _flashTimer.Stop();
            _flashOn = false;
            _pipe1Lights[0].ForeColor = Color.FromArgb(0, 180, 80);  // 绿色
        }
    }

    /// <summary>
    /// 更新右侧实时管道状态面板
    /// </summary>
    private void UpdateRightRealtimePanel(LeakData data)
    {
        if (_lblRightStatus.IsDisposed) return;

        bool isPipeLeak = (data.AlarmCode & 1) != 0;     // bit0 = 管道泄漏
        bool isTankFull = (data.AlarmCode & 2) != 0;     // bit1 = 储液槽已满

        if (data.AlarmCode == 0)
        {
            // 正常状态
            _lblRightStatus.Text = "正常";
            _lblRightStatus.ForeColor = Color.FromArgb(0, 150, 80);
            _lblRightDistance.Visible = false;
        }
        else
        {
            // 报警状态 —— 红色大字
            string reason;
            if (isPipeLeak && isTankFull)
                reason = "管道泄漏 + 储液槽已满";
            else if (isPipeLeak)
                reason = "管道泄漏";
            else if (isTankFull)
                reason = "储液槽已满";
            else
                reason = $"未知报警 (code={data.AlarmCode})";

            _lblRightStatus.Text = reason;
            _lblRightStatus.ForeColor = Color.Red;

            // 管道泄漏时显示液漏距离
            if (isPipeLeak)
            {
                _lblRightDistance.Text = $"液漏距离: {data.Distance:F1} 米";
                _lblRightDistance.Visible = true;
            }
            else
            {
                _lblRightDistance.Visible = false;
            }
        }

        _lblRightTimestamp.Text = $"更新时间: {data.Timestamp}";
    }

    /// <summary>
    /// 闪烁定时器：仅切换一道 1 区监控灯红/灰
    /// </summary>
    private void OnFlashTimerTick(object? sender, EventArgs e)
    {
        _flashOn = !_flashOn;

        if (_currentAlarmCode != 0)
        {
            _pipe1Lights[0].ForeColor = _flashOn
                ? Color.Red
                : Color.FromArgb(60, 60, 60);
        }
    }

    private void AppendHistoryLog(LeakData data)
    {
        // 仅记录报警数据
        if (data.AlarmCode == 0) return;

        if (_gridHistory.IsDisposed) return;
        try
        {
            // 报警原因
            bool isPipeLeak = (data.AlarmCode & 1) != 0;
            bool isTankFull = (data.AlarmCode & 2) != 0;
            string reason;
            if (isPipeLeak && isTankFull)
                reason = "管道泄漏 + 储液槽已满";
            else if (isPipeLeak)
                reason = $"管道泄漏 ({data.Distance:F1}m)";
            else if (isTankFull)
                reason = "储液槽已满";
            else
                reason = $"未知 (code={data.AlarmCode})";

            // 报警站点（当前硬编码一道1区）
            string station = "一道 1区";

            _gridHistory.Rows.Add(data.Timestamp, station, reason);

            // 限制行数（500 行）
            if (_gridHistory.Rows.Count > 500)
            {
                int remove = _gridHistory.Rows.Count - 500;
                for (int i = 0; i < remove; i++)
                    _gridHistory.Rows.RemoveAt(0);
            }

            // 自动滚动到最后一行
            if (_gridHistory.Rows.Count > 0)
                _gridHistory.FirstDisplayedScrollingRowIndex = _gridHistory.Rows.Count - 1;
        }
        catch { /* 忽略 UI 更新失败 */ }
    }

    // ================================================================
    // 辅助方法
    // ================================================================

    private void AppendLog(string level, string message)
    {
        if (_txtLog.IsDisposed) return;
        try
        {
            string timestamp = DateTime.Now.ToString("HH:mm:ss");
            _txtLog.AppendText($"[{timestamp}] {level,-5} {message}\n");

            // 限制日志行数
            if (_txtLog.Lines.Length > 1000)
            {
                var lines = _txtLog.Lines;
                var recent = lines.Skip(lines.Length - 500).ToArray();
                _txtLog.Text = string.Join("\n", recent) + "\n";
            }

            // 自动滚动到底部
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }
        catch { /* 忽略 UI 更新失败 */ }
    }

    private void ExportData()
    {
        using var sfd = new SaveFileDialog
        {
            Filter = "CSV 文件|*.csv|文本文件|*.txt|所有文件|*.*",
            DefaultExt = "csv",
            FileName = $"LeakData_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
        };
        if (sfd.ShowDialog() != DialogResult.OK) return;

        try
        {
            var data = _history.GetRecent(_history.Count);
            using var sw = new StreamWriter(sfd.FileName, false, System.Text.Encoding.UTF8);
            sw.WriteLine("时间,报警站点,报警原因,液漏距离(m),原始帧,本地接收时间");
            foreach (var item in data)
            {
                bool isPipeLeak = (item.AlarmCode & 1) != 0;
                bool isTankFull = (item.AlarmCode & 2) != 0;
                string reason;
                if (isPipeLeak && isTankFull)
                    reason = "管道泄漏 + 储液槽已满";
                else if (isPipeLeak)
                    reason = "管道泄漏";
                else if (isTankFull)
                    reason = "储液槽已满";
                else
                    reason = $"未知({item.AlarmCode})";

                sw.WriteLine($"\"{item.Timestamp}\",\"一道 1区\",\"{reason}\",{item.Distance:F1},\"{item.RawFrame}\",\"{item.ArrivalTime:yyyy-MM-dd HH:mm:ss.fff}\"");
            }
            AppendLog("INFO", $"数据已导出到: {sfd.FileName} ({data.Count} 条)");
            MessageBox.Show($"已导出 {data.Count} 条数据到:\n{sfd.FileName}", "导出成功",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"导出失败:\n{ex.Message}", "错误",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenSerialSettings()
    {
        MessageBox.Show(
            "串口参数:\n\n" +
            $"  COM 口:    {_connectedPort}\n" +
            $"  波特率:    {_connectedBaudRate}\n" +
            $"  数据位:    {_connectedDataBits}\n" +
            $"  校验位:    {_connectedParity}\n" +
            $"  停止位:    {_connectedStopBits}\n" +
            $"  协议:      {_connectedProtocol}\n\n" +
            "点击【连接】按钮可重新配置串口参数。",
            "串口设置", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "徐圩核电站液态排放物双层管线泄漏监测系统 v2.0\n\n" +
            "基于 .NET 8.0 + WinForms 构建\n" +
            "串口通信: System.IO.Ports\n\n" +
            "功能:\n" +
            "  - TTL-USB 串口数据读取\n" +
            "  - 实时数据解析与显示\n" +
            "  - 历史数据日志滚动\n" +
            "  - 双路报警指示灯\n" +
            "  - 数据导出 CSV\n\n" +
            $"协议解析器: {_parser.ProtocolName}",
            "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _pollTimer.Stop();
        _flashTimer.Stop();
        _serialReader?.Disconnect();
        _serialReader?.Dispose();
        base.OnFormClosing(e);
    }
}
