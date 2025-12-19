using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace MiniHttpServer
{
    public partial class Form1 : Form
    {
        class HostedFile
        {
            public string Path;
            public int Port = 8080;
            public string Host = "+";
            public HttpListener Listener;
            public Thread Thread;
            public bool IsRunning = false;
            public int Hits;
            public string Url
            {
                get
                {
                    if (string.IsNullOrEmpty(Host) || Host == "+")
                        return $"http://localhost:{Port}/{System.IO.Path.GetFileName(Path)}";
                    else
                        return $"http://{Host}:{Port}/{System.IO.Path.GetFileName(Path)}";
                }
            }
        }

        private List<HostedFile> files = new List<HostedFile>();
        private List<string> availableIPs = new List<string>();
        private readonly Font logFont = new Font("Consolas", 9f);
        private readonly Color errorColor = Color.FromArgb(255, 100, 100);
        private readonly Color infoColor = Color.FromArgb(200, 200, 200);
        private readonly Color successColor = Color.FromArgb(100, 255, 100);
        private readonly Color warningColor = Color.FromArgb(255, 200, 100);

        public Form1()
        {
            try
            {
                InitializeComponent();
                SetupScaling();
                LoadNetworkIPs();
                SetupToolTips();
                SetupLogColors();

                numPort.Value = 8080;
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during initialization: {ex.Message}\n\n{ex.StackTrace}",
                    "Fatal Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SetupScaling()
        {
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.Font = new Font("Segoe UI", 9f * GetScalingFactor());
        }

        private float GetScalingFactor()
        {
            using (Graphics g = this.CreateGraphics())
            {
                return g.DpiX / 96f;
            }
        }

        private void SetupLogColors()
        {
            txtLog.Font = logFont;
            txtLog.BackColor = Color.FromArgb(25, 25, 30);
            txtLog.ForeColor = infoColor;
        }

        private void LoadNetworkIPs()
        {
            try
            {
                availableIPs.Clear();
                availableIPs.Add("+ (All interfaces)");
                availableIPs.Add("localhost");

                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        availableIPs.Add(ip.ToString());
                    }
                }

                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (UnicastIPAddressInformation ip in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                string ipStr = ip.Address.ToString();
                                if (!availableIPs.Contains(ipStr))
                                    availableIPs.Add(ipStr);
                            }
                        }
                    }
                }

                comboHostIP.DataSource = new List<string>(availableIPs);
                comboHostIP.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR loading network interfaces: {ex.Message}", LogType.Error);
            }
        }

        private void SetupToolTips()
        {
            try
            {
                var toolTip = new ToolTip();
                toolTip.SetToolTip(btnAddFile, "Add files to host");
                toolTip.SetToolTip(btnStartAll, "Start hosting all files");
                toolTip.SetToolTip(btnStopAll, "Stop all hosted files");
                toolTip.SetToolTip(btnRemoveFile, "Remove selected file");
                toolTip.SetToolTip(btnRemoveAll, "Remove all files");
                toolTip.SetToolTip(btnCopy, "Copy selected URL to clipboard");
                toolTip.SetToolTip(btnRefreshIP, "Refresh network interfaces");
                toolTip.SetToolTip(numPort, "Port number for hosting");
                toolTip.SetToolTip(comboHostIP, "Select network interface to bind");
                toolTip.SetToolTip(txtLog, "Server activity log");
                toolTip.SetToolTip(btnOpenInBrowser, "Open selected URL in browser");
                toolTip.SetToolTip(btnClearLog, "Clear log messages");
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in SetupToolTips: {ex.Message}", LogType.Error);
            }
        }

        private void btnAddFile_Click(object sender, EventArgs e)
        {
            try
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "All files (*.*)|*.*";
                    ofd.Title = "Select files to host";
                    ofd.Multiselect = true;

                    if (ofd.ShowDialog() == DialogResult.OK)
                    {
                        int addedCount = 0;
                        foreach (string fileName in ofd.FileNames)
                        {
                            HostedFile hf = new HostedFile();
                            hf.Path = fileName;
                            hf.Port = (int)numPort.Value;
                            string selectedHost = comboHostIP.SelectedItem.ToString();
                            hf.Host = selectedHost.Contains("(") ?
                                selectedHost.Split('(')[0].Trim() : selectedHost;
                            files.Add(hf);
                            addedCount++;
                        }

                        RefreshGrid();
                        LogMessage($"Added {addedCount} file(s)", LogType.Success);

                        if (dgvFiles.Rows.Count > 0)
                        {
                            dgvFiles.ClearSelection();
                            dgvFiles.Rows[dgvFiles.Rows.Count - 1].Selected = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnAddFile_Click: {ex.Message}", LogType.Error);
                MessageBox.Show($"Error adding files: {ex.Message}", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshGrid()
        {
            try
            {
                dgvFiles.Rows.Clear();
                foreach (var f in files)
                {
                    dgvFiles.Rows.Add(
                        f.IsRunning ? "● Running" : "○ Stopped",
                        Path.GetFileName(f.Path),
                        f.Port,
                        f.Host == "+" ? "All" : f.Host,
                        f.Hits,
                        f.IsRunning ? f.Url : "Not running",
                        f.Path
                    );
                }
                UpdateButtons();
                UpdateStatusBar();
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in RefreshGrid: {ex.Message}", LogType.Error);
            }
        }

        private void dgvFiles_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateButtons();
                if (dgvFiles.SelectedRows.Count > 0)
                {
                    var selectedRow = dgvFiles.SelectedRows[0];
                    if (selectedRow.Cells[6].Value != null)
                    {
                        string filePath = selectedRow.Cells[6].Value.ToString();
                        var file = files.Find(f => f.Path == filePath);
                        if (file != null)
                        {
                            numPort.Value = file.Port;
                            var hostToSelect = file.Host == "+" ? "+ (All interfaces)" : file.Host;
                            int index = comboHostIP.FindString(hostToSelect);
                            if (index >= 0) comboHostIP.SelectedIndex = index;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in dgvFiles_SelectionChanged: {ex.Message}", LogType.Error);
            }
        }

        private HostedFile GetSelectedFile()
        {
            if (dgvFiles.SelectedRows.Count > 0)
            {
                var selectedRow = dgvFiles.SelectedRows[0];
                if (selectedRow.Cells[6].Value != null)
                {
                    string filePath = selectedRow.Cells[6].Value.ToString();
                    return files.Find(f => f.Path == filePath);
                }
            }
            return null;
        }

        private void btnStartAll_Click(object sender, EventArgs e)
        {
            try
            {
                int started = 0;
                int failed = 0;

                foreach (var file in files)
                {
                    if (!file.IsRunning)
                    {
                        if (StartFileServer(file))
                            started++;
                        else
                            failed++;
                    }
                }

                RefreshGrid();

                if (started > 0)
                    LogMessage($"Started {started} server(s)", LogType.Success);
                if (failed > 0)
                    LogMessage($"Failed to start {failed} server(s)", LogType.Warning);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnStartAll_Click: {ex.Message}", LogType.Error);
            }
        }

        private bool StartFileServer(HostedFile file)
        {
            try
            {
                if (file.IsRunning) return true;

                string host = file.Host == "+" ? "*" : file.Host;

                if (host != "*" && host != "localhost")
                {
                    try { IPAddress.Parse(host); }
                    catch
                    {
                        LogMessage($"Invalid IP address: {host}", LogType.Error);
                        return false;
                    }
                }

                file.Listener = new HttpListener();
                file.Listener.Prefixes.Add($"http://{host}:{file.Port}/");
                file.Listener.Start();
                file.IsRunning = true;

                file.Thread = new Thread(() => HostFile(file));
                file.Thread.IsBackground = true;
                file.Thread.Start();

                LogMessage($"Server started: {file.Url}", LogType.Success);
                return true;
            }
            catch (HttpListenerException ex)
            {
                LogMessage($"Cannot start server on port {file.Port}: {ex.Message}", LogType.Error);
                return false;
            }
            catch (Exception ex)
            {
                LogMessage($"Error starting server: {ex.Message}", LogType.Error);
                return false;
            }
        }

        private void btnStopAll_Click(object sender, EventArgs e)
        {
            try
            {
                int stopped = 0;
                foreach (var file in files)
                {
                    if (file.IsRunning)
                    {
                        StopFileServer(file);
                        stopped++;
                    }
                }

                RefreshGrid();
                if (stopped > 0)
                    LogMessage($"Stopped {stopped} server(s)", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnStopAll_Click: {ex.Message}", LogType.Error);
            }
        }

        private void StopFileServer(HostedFile file)
        {
            try
            {
                file.IsRunning = false;

                if (file.Listener != null)
                {
                    file.Listener.Stop();
                    file.Listener.Close();
                    file.Listener = null;
                }

                if (file.Thread != null && file.Thread.IsAlive)
                {
                    file.Thread.Join(1000);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error stopping server: {ex.Message}", LogType.Error);
            }
        }

        private void btnRemoveFile_Click(object sender, EventArgs e)
        {
            try
            {
                var file = GetSelectedFile();
                if (file != null)
                {
                    if (file.IsRunning)
                    {
                        var result = MessageBox.Show("File is currently being hosted. Stop and remove?",
                            "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                        if (result == DialogResult.No) return;

                        StopFileServer(file);
                    }

                    files.Remove(file);
                    RefreshGrid();
                    LogMessage($"Removed: {Path.GetFileName(file.Path)}", LogType.Info);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnRemoveFile_Click: {ex.Message}", LogType.Error);
            }
        }

        private void btnRemoveAll_Click(object sender, EventArgs e)
        {
            try
            {
                if (files.Count == 0) return;

                var result = MessageBox.Show($"Remove all {files.Count} files?",
                    "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (result == DialogResult.No) return;

                foreach (var file in files)
                {
                    if (file.IsRunning)
                        StopFileServer(file);
                }

                int removedCount = files.Count;
                files.Clear();
                RefreshGrid();
                LogMessage($"Removed all {removedCount} files", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnRemoveAll_Click: {ex.Message}", LogType.Error);
            }
        }

        private void btnCopy_Click(object sender, EventArgs e)
        {
            try
            {
                var file = GetSelectedFile();
                if (file != null && file.IsRunning)
                {
                    Clipboard.SetText(file.Url);
                    LogMessage($"Copied to clipboard: {file.Url}", LogType.Success);
                }
                else if (file != null && !file.IsRunning)
                {
                    LogMessage($"Server not running for: {Path.GetFileName(file.Path)}", LogType.Warning);
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnCopy_Click: {ex.Message}", LogType.Error);
            }
        }

        private void btnOpenInBrowser_Click(object sender, EventArgs e)
        {
            try
            {
                var file = GetSelectedFile();
                if (file != null && file.IsRunning)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = file.Url,
                            UseShellExecute = true
                        });
                        file.Hits++;
                        RefreshGrid();
                        LogMessage($"Opened in browser: {file.Url}", LogType.Success);
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Failed to open URL: {ex.Message}", LogType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnOpenInBrowser_Click: {ex.Message}", LogType.Error);
            }
        }

        private void btnRefreshIP_Click(object sender, EventArgs e)
        {
            try
            {
                LoadNetworkIPs();
                LogMessage("Network interfaces refreshed", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnRefreshIP_Click: {ex.Message}", LogType.Error);
            }
        }

        private void ClearLog_Click(object sender, EventArgs e)
        {
            try
            {
                txtLog.Clear();
                LogMessage("Log cleared", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"Error clearing log: {ex.Message}", LogType.Error);
            }
        }

        private void HostFile(HostedFile file)
        {
            try
            {
                while (file.IsRunning && file.Listener != null && file.Listener.IsListening)
                {
                    try
                    {
                        var context = file.Listener.GetContext();

                        ThreadPool.QueueUserWorkItem((ctx) =>
                        {
                            try
                            {
                                HttpListenerContext httpContext = (HttpListenerContext)ctx;
                                file.Hits++;

                                this.Invoke(new Action(() =>
                                {
                                    LogMessage($"Request: {Path.GetFileName(file.Path)} from {httpContext.Request.RemoteEndPoint?.Address}", LogType.Info);
                                    RefreshGrid();
                                }));

                                byte[] data = File.ReadAllBytes(file.Path);
                                httpContext.Response.ContentType = GetContentType(file.Path);
                                httpContext.Response.ContentLength64 = data.Length;
                                httpContext.Response.OutputStream.Write(data, 0, data.Length);
                                httpContext.Response.OutputStream.Close();
                            }
                            catch (Exception ex)
                            {
                                this.Invoke(new Action(() =>
                                {
                                    LogMessage($"Request error: {ex.Message}", LogType.Error);
                                }));
                            }
                        }, context);
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        this.Invoke(new Action(() =>
                        {
                            LogMessage($"Listener error: {ex.Message}", LogType.Error);
                        }));
                        Thread.Sleep(1000);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Invoke(new Action(() =>
                {
                    LogMessage($"Server error: {ex.Message}", LogType.Error);
                }));
            }
            finally
            {
                this.Invoke(new Action(() =>
                {
                    if (file.IsRunning)
                    {
                        file.IsRunning = false;
                        RefreshGrid();
                        LogMessage($"Server stopped: {Path.GetFileName(file.Path)}", LogType.Info);
                    }
                }));
            }
        }

        private void UpdateButtons()
        {
            try
            {
                var selectedFile = GetSelectedFile();
                bool hasSelection = selectedFile != null;
                bool isRunning = hasSelection && selectedFile.IsRunning;
                bool anyRunning = false;
                bool anyStopped = false;

                foreach (var f in files)
                {
                    if (f.IsRunning) anyRunning = true;
                    else anyStopped = true;
                }

                btnAddFile.Enabled = true;
                btnStartAll.Enabled = anyStopped;
                btnStopAll.Enabled = anyRunning;
                btnRemoveFile.Enabled = hasSelection;
                btnRemoveAll.Enabled = files.Count > 0;
                btnCopy.Enabled = hasSelection && isRunning;
                btnRefreshIP.Enabled = true;
                btnOpenInBrowser.Enabled = hasSelection && isRunning;
                btnClearLog.Enabled = true;
                numPort.Enabled = hasSelection && !isRunning;
                comboHostIP.Enabled = hasSelection && !isRunning;

                if (hasSelection)
                {
                    lblStatus.Text = isRunning ? $"● RUNNING: {selectedFile.Url}" : "○ STOPPED";
                    lblStatus.ForeColor = isRunning ? successColor : infoColor;
                }
                else
                {
                    lblStatus.Text = files.Count > 0 ? "Select a file" : "Ready to add files";
                    lblStatus.ForeColor = infoColor;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in UpdateButtons: {ex.Message}", LogType.Error);
            }
        }

        private void UpdateStatusBar()
        {
            try
            {
                int runningCount = 0;
                int totalHits = 0;

                foreach (var f in files)
                {
                    if (f.IsRunning) runningCount++;
                    totalHits += f.Hits;
                }

                lblServerCount.Text = $"Servers: {runningCount}/{files.Count}";
                lblTotalHits.Text = $"Total Hits: {totalHits}";
                toolStripStatusLabel.Text = runningCount > 0 ? $"{runningCount} active" : "Ready";
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in UpdateStatusBar: {ex.Message}", LogType.Error);
            }
        }

        enum LogType { Info, Error, Success, Warning }

        private void LogMessage(string message, LogType type = LogType.Info)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string, LogType>(LogMessage), message, type);
                return;
            }

            try
            {
                Color color = type switch
                {
                    LogType.Error => errorColor,
                    LogType.Success => successColor,
                    LogType.Warning => warningColor,
                    _ => infoColor
                };

                int start = txtLog.TextLength;
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                int end = txtLog.TextLength;

                txtLog.Select(start, end - start);
                txtLog.SelectionColor = color;
                txtLog.SelectionLength = 0;
                txtLog.ScrollToCaret();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"CRITICAL: Failed to log message: {message}");
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        private string GetContentType(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext switch
            {
                ".html" or ".htm" => "text/html",
                ".txt" => "text/plain",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".pdf" => "application/pdf",
                ".zip" => "application/zip",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".mp3" => "audio/mpeg",
                ".mp4" => "video/mp4",
                _ => "application/octet-stream",
            };
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                LogMessage("Application closing...", LogType.Info);
                foreach (var file in files)
                {
                    if (file.IsRunning)
                        StopFileServer(file);
                }
                LogMessage("All servers stopped", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR during shutdown: {ex.Message}", LogType.Error);
            }

            base.OnFormClosing(e);
        }

        private void numPort_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                var file = GetSelectedFile();
                if (file != null && !file.IsRunning)
                {
                    file.Port = (int)numPort.Value;
                    RefreshGrid();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in numPort_ValueChanged: {ex.Message}", LogType.Error);
            }
        }

        private void comboHostIP_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (comboHostIP.SelectedItem == null) return;

                var file = GetSelectedFile();
                if (file != null && !file.IsRunning)
                {
                    string selected = comboHostIP.SelectedItem.ToString();
                    string hostValue = selected.Contains("(") ?
                        selected.Split('(')[0].Trim() : selected;

                    file.Host = hostValue;
                    RefreshGrid();
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in comboHostIP_SelectedIndexChanged: {ex.Message}", LogType.Error);
            }
        }

        private void dgvFiles_CellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            try
            {
                if (e.RowIndex >= 0)
                {
                    var file = GetSelectedFile();
                    if (file != null && file.IsRunning)
                    {
                        btnOpenInBrowser_Click(sender, e);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in dgvFiles_CellDoubleClick: {ex.Message}", LogType.Error);
            }
        }

        // Puste metody dla designera
        private void lblHostIP_Click(object sender, EventArgs e) { }
        private void toolStripStatusLabel_Click(object sender, EventArgs e) { }
        private void groupLog_Enter(object sender, EventArgs e) { }
    }
}