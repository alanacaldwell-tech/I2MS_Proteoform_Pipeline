namespace IonCounterGUI;

public partial class MainForm : Form
{
    private string? _lastOutputPath;

    public MainForm()
    {
        InitializeComponent();

        // Pre-fill output folder to the user's Desktop for convenience.
        txtOutput.Text = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
    }

    // ── Browse handlers ───────────────────────────────────────────────────────

    private void BtnBrowseCsv_Click(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog
        {
            Title  = "Select reference CSV file",
            Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
        };

        if (dlg.ShowDialog() == DialogResult.OK)
        {
            txtCsv.Text = dlg.FileName;

            // Default output to the same folder as the CSV if not already set.
            if (string.IsNullOrWhiteSpace(txtOutput.Text))
                txtOutput.Text = Path.GetDirectoryName(dlg.FileName);
        }
    }

    private void BtnBrowseFolder_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "Select folder containing .dmt files",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == DialogResult.OK)
            txtFolder.Text = dlg.SelectedPath;
    }

    private void BtnBrowseOutput_Click(object? sender, EventArgs e)
    {
        using var dlg = new FolderBrowserDialog
        {
            Description         = "Select output folder",
            UseDescriptionForTitle = true
        };

        if (dlg.ShowDialog() == DialogResult.OK)
            txtOutput.Text = dlg.SelectedPath;
    }

    // ── Run ───────────────────────────────────────────────────────────────────

    private async void BtnRun_Click(object? sender, EventArgs e)
    {
        if (!ValidateInputs()) return;

        SetRunning(true);
        txtLog.Clear();
        btnOpenOutput.Enabled = false;
        _lastOutputPath = null;

        string csvPath    = txtCsv.Text.Trim();
        string dmtFolder  = txtFolder.Text.Trim();
        string outputDir  = txtOutput.Text.Trim();

        try
        {
            await Task.Run(() => RunProcessing(csvPath, dmtFolder, outputDir));
        }
        catch (Exception ex)
        {
            LogError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            SetRunning(false);
        }
    }

    private void RunProcessing(string csvPath, string dmtFolder, string outputDir)
    {
        // ── Load CSV ─────────────────────────────────────────────────────────
        Log("Loading reference CSV…");
        List<CentroidEntry> entries;
        string[] originalHeaders;

        try
        {
            (entries, originalHeaders) = CsvReader.Parse(csvPath);
        }
        catch (Exception ex)
        {
            LogError($"Error reading CSV: {ex.Message}");
            return;
        }

        Log($"  {entries.Count} centroid(s) loaded.");
        foreach (var entry in entries)
            Log($"    {entry.CentroidMass:G} ± {entry.Tolerance:G} Da");

        // ── Find .dmt files ──────────────────────────────────────────────────
        var dmtFiles = Directory.GetFiles(dmtFolder, "*.dmt", SearchOption.TopDirectoryOnly)
                                .OrderBy(f => f)
                                .ToArray();

        if (dmtFiles.Length == 0)
        {
            LogError("No .dmt files found in the selected folder.");
            return;
        }

        Log($"\nFound {dmtFiles.Length} .dmt file(s).\n");
        SetProgress(0, dmtFiles.Length);

        var dmtFileNames = dmtFiles.Select(Path.GetFileName).ToList()!;

        // ── Process each file ────────────────────────────────────────────────
        for (int f = 0; f < dmtFiles.Length; f++)
        {
            string filePath = dmtFiles[f];
            string fileName = dmtFileNames[f];

            Log($"[{f + 1}/{dmtFiles.Length}]  {fileName}…");

            var counts = new long[entries.Count];

            try
            {
                foreach (double mass in DmtParser.ReadMasses(filePath))
                {
                    for (int e = 0; e < entries.Count; e++)
                        if (entries[e].Contains(mass))
                            counts[e]++;
                }
            }
            catch (Exception ex)
            {
                LogError($"  Error reading {fileName}: {ex.Message}");
                for (int e = 0; e < entries.Count; e++)
                    entries[e].Counts.Add((fileName, 0));
                SetProgress(f + 1, dmtFiles.Length);
                continue;
            }

            long total = 0;
            for (int e = 0; e < entries.Count; e++)
            {
                entries[e].Counts.Add((fileName, counts[e]));
                total += counts[e];
            }

            Log($"  → {total} matching ion(s) across all centroids");
            SetProgress(f + 1, dmtFiles.Length);
        }

        // ── Write output ─────────────────────────────────────────────────────
        string baseName    = Path.GetFileNameWithoutExtension(csvPath);
        string outputPath  = Path.Combine(outputDir, baseName + "_ion_counts.csv");

        try
        {
            CsvWriter.Write(outputPath, entries, originalHeaders, dmtFileNames);
        }
        catch (Exception ex)
        {
            LogError($"Error writing output: {ex.Message}");
            return;
        }

        Log($"\nOutput written to:\n  {outputPath}");

        // ── Summary ──────────────────────────────────────────────────────────
        Log("\n── Summary ─────────────────────────────────────");
        foreach (var entry in entries)
        {
            long grand = entry.Counts.Sum(c => c.Count);
            Log($"  {entry.CentroidMass,14:G}  ±{entry.Tolerance:G} Da  →  {grand,8} ion(s)");
        }

        _lastOutputPath = outputPath;
        Invoke(() =>
        {
            btnOpenOutput.Enabled = true;
            lblStatus.Text = $"Done — output saved to {outputPath}";
            lblStatus.ForeColor = Color.DarkGreen;
        });
    }

    // ── Open output ───────────────────────────────────────────────────────────

    private void BtnOpenOutput_Click(object? sender, EventArgs e)
    {
        if (_lastOutputPath is null || !File.Exists(_lastOutputPath)) return;

        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName        = _lastOutputPath,
            UseShellExecute = true   // opens with the default app (Excel, Notepad, etc.)
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private bool ValidateInputs()
    {
        if (!File.Exists(txtCsv.Text.Trim()))
        {
            MessageBox.Show("Please select a valid reference CSV file.", "Missing input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!Directory.Exists(txtFolder.Text.Trim()))
        {
            MessageBox.Show("Please select a valid folder containing .dmt files.", "Missing input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        if (!Directory.Exists(txtOutput.Text.Trim()))
        {
            MessageBox.Show("Please select a valid output folder.", "Missing input",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        return true;
    }

    private void SetRunning(bool running)
    {
        Invoke(() =>
        {
            btnRun.Enabled = !running;
            btnRun.Text    = running ? "Running…" : "▶  Run";
            lblStatus.Text      = running ? "Processing…" : lblStatus.Text;
            lblStatus.ForeColor = running ? Color.DarkOrange : lblStatus.ForeColor;
        });
    }

    private void SetProgress(int value, int max)
    {
        Invoke(() =>
        {
            progressBar.Maximum = max;
            progressBar.Value   = value;
        });
    }

    private void Log(string message)
    {
        Invoke(() =>
        {
            txtLog.AppendText(message + "\n");
            txtLog.ScrollToCaret();
        });
    }

    private void LogError(string message)
    {
        Invoke(() =>
        {
            int start = txtLog.TextLength;
            txtLog.AppendText("ERROR: " + message + "\n");
            txtLog.Select(start, txtLog.TextLength - start);
            txtLog.SelectionColor = Color.OrangeRed;
            txtLog.SelectionLength = 0;
            txtLog.ScrollToCaret();
            lblStatus.Text      = "Error — see log for details.";
            lblStatus.ForeColor = Color.Crimson;
        });
    }
}
