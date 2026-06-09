namespace IonCounterGUI;

partial class MainForm
{
    private System.ComponentModel.IContainer components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null)) components.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        // ── Labels ────────────────────────────────────────────────────────────
        lblCsv = new Label
        {
            Text = "Reference CSV file:",
            Location = new Point(16, 20),
            Size = new Size(140, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        lblFolder = new Label
        {
            Text = ".dmt file folder:",
            Location = new Point(16, 60),
            Size = new Size(140, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        lblOutput = new Label
        {
            Text = "Output folder:",
            Location = new Point(16, 100),
            Size = new Size(140, 20),
            TextAlign = ContentAlignment.MiddleLeft
        };

        lblStatus = new Label
        {
            Text = "Ready.",
            Location = new Point(16, 260),
            Size = new Size(752, 20),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray
        };

        // ── TextBoxes ─────────────────────────────────────────────────────────
        txtCsv = new TextBox
        {
            Location = new Point(164, 18),
            Size = new Size(500, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        txtFolder = new TextBox
        {
            Location = new Point(164, 58),
            Size = new Size(500, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        txtOutput = new TextBox
        {
            Location = new Point(164, 98),
            Size = new Size(500, 23),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // ── Browse buttons ────────────────────────────────────────────────────
        btnBrowseCsv = new Button
        {
            Text = "Browse…",
            Location = new Point(672, 16),
            Size = new Size(90, 27),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnBrowseCsv.Click += BtnBrowseCsv_Click;

        btnBrowseFolder = new Button
        {
            Text = "Browse…",
            Location = new Point(672, 56),
            Size = new Size(90, 27),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnBrowseFolder.Click += BtnBrowseFolder_Click;

        btnBrowseOutput = new Button
        {
            Text = "Browse…",
            Location = new Point(672, 96),
            Size = new Size(90, 27),
            Anchor = AnchorStyles.Top | AnchorStyles.Right
        };
        btnBrowseOutput.Click += BtnBrowseOutput_Click;

        // ── Run / Open buttons ────────────────────────────────────────────────
        btnRun = new Button
        {
            Text = "▶  Run",
            Location = new Point(16, 136),
            Size = new Size(120, 34),
            BackColor = Color.SteelBlue,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold)
        };
        btnRun.Click += BtnRun_Click;

        btnOpenOutput = new Button
        {
            Text = "Open output file",
            Location = new Point(148, 136),
            Size = new Size(140, 34),
            Enabled = false
        };
        btnOpenOutput.Click += BtnOpenOutput_Click;

        // ── Progress bar ──────────────────────────────────────────────────────
        progressBar = new ProgressBar
        {
            Location = new Point(16, 184),
            Size = new Size(752, 22),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Minimum = 0,
            Value = 0,
            Style = ProgressBarStyle.Continuous
        };

        // ── Log box ───────────────────────────────────────────────────────────
        txtLog = new RichTextBox
        {
            Location = new Point(16, 218),
            Size = new Size(752, 200),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            ReadOnly = true,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.LightGreen,
            Font = new Font("Consolas", 9f),
            ScrollBars = RichTextBoxScrollBars.Vertical
        };

        // ── Separator ─────────────────────────────────────────────────────────
        var separator = new Label
        {
            Location = new Point(16, 130),
            Size = new Size(752, 2),
            BorderStyle = BorderStyle.Fixed3D,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
        };

        // ── Form ──────────────────────────────────────────────────────────────
        SuspendLayout();
        Text = "I2MS Proteoform Ion Counter";
        Size = new Size(808, 520);
        MinimumSize = new Size(640, 480);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.WhiteSmoke;

        Controls.AddRange(new Control[]
        {
            lblCsv, lblFolder, lblOutput, lblStatus,
            txtCsv, txtFolder, txtOutput,
            btnBrowseCsv, btnBrowseFolder, btnBrowseOutput,
            separator, btnRun, btnOpenOutput,
            progressBar, txtLog
        });

        ResumeLayout(false);
        PerformLayout();
    }

    // ── Controls ──────────────────────────────────────────────────────────────
    private Label      lblCsv        = null!;
    private Label      lblFolder     = null!;
    private Label      lblOutput     = null!;
    private Label      lblStatus     = null!;
    private TextBox    txtCsv        = null!;
    private TextBox    txtFolder     = null!;
    private TextBox    txtOutput     = null!;
    private Button     btnBrowseCsv  = null!;
    private Button     btnBrowseFolder = null!;
    private Button     btnBrowseOutput = null!;
    private Button     btnRun        = null!;
    private Button     btnOpenOutput = null!;
    private ProgressBar progressBar  = null!;
    private RichTextBox txtLog       = null!;
}
