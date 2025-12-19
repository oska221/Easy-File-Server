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
            public int Hits;
            public string Url
            {
                get
                {
                    if (string.IsNullOrEmpty(Host) || Host == "+")
                        return $"http://{GetLocalIP()}:{Port}/{System.IO.Path.GetFileName(Path)}";
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

        // Jeden serwer dla wszystkich plików
        private HttpListener server;
        private Thread serverThread;
        private bool serverRunning = false;
        private int currentPort = 8080;
        private string currentHost = "+";

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

        private static string GetLocalIP()
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
                return "localhost";
            }
            catch
            {
                return "localhost";
            }
        }

        private void SetupToolTips()
        {
            try
            {
                var toolTip = new ToolTip();
                toolTip.SetToolTip(btnAddFile, "Add files to host");
                toolTip.SetToolTip(btnStartAll, "Start hosting server");
                toolTip.SetToolTip(btnStopAll, "Stop server");
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
                            // Sprawdź rozmiar pliku
                            FileInfo fileInfo = new FileInfo(fileName);
                            if (fileInfo.Length > 200L * 1024 * 1024 * 1024) // 200GB
                            {
                                LogMessage($"File too large (max 200GB): {Path.GetFileName(fileName)}", LogType.Warning);
                                continue;
                            }

                            HostedFile hf = new HostedFile();
                            hf.Path = fileName;
                            hf.Port = (int)numPort.Value;
                            string selectedHost = comboHostIP.SelectedItem.ToString();
                            hf.Host = selectedHost.Contains("(") ?
                                selectedHost.Split('(')[0].Trim() : selectedHost;
                            files.Add(hf);
                            addedCount++;

                            LogMessage($"Added: {Path.GetFileName(fileName)} ({(fileInfo.Length / (1024f * 1024f)):F2} MB)", LogType.Info);
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
                    FileInfo fileInfo = new FileInfo(f.Path);
                    string fileSize = FormatFileSize(fileInfo.Length);

                    dgvFiles.Rows.Add(
                        serverRunning ? "● Running" : "○ Stopped",
                        Path.GetFileName(f.Path),
                        f.Port,
                        f.Host == "+" ? "All" : f.Host,
                        f.Hits,
                        serverRunning ? f.Url : "Not running",
                        fileSize,
                        f.Path // Hidden column for full path
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

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }

        private void dgvFiles_SelectionChanged(object sender, EventArgs e)
        {
            try
            {
                UpdateButtons();
                if (dgvFiles.SelectedRows.Count > 0)
                {
                    var selectedRow = dgvFiles.SelectedRows[0];
                    if (selectedRow.Cells[7].Value != null)
                    {
                        string filePath = selectedRow.Cells[7].Value.ToString();
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
                if (selectedRow.Cells[7].Value != null)
                {
                    string filePath = selectedRow.Cells[7].Value.ToString();
                    return files.Find(f => f.Path == filePath);
                }
            }
            return null;
        }

        private void btnStartAll_Click(object sender, EventArgs e)
        {
            try
            {
                if (serverRunning) return;
                if (files.Count == 0)
                {
                    LogMessage("No files to host", LogType.Warning);
                    return;
                }

                // Sprawdź czy port jest wolny
                if (IsPortInUse((int)numPort.Value))
                {
                    LogMessage($"Port {numPort.Value} is already in use!", LogType.Error);
                    return;
                }

                currentPort = (int)numPort.Value;
                string selectedHost = comboHostIP.SelectedItem.ToString();
                currentHost = selectedHost.Contains("(") ?
                    selectedHost.Split('(')[0].Trim() : selectedHost;

                // Zaktualizuj wszystkie pliki na ten sam port i host
                foreach (var file in files)
                {
                    file.Port = currentPort;
                    file.Host = currentHost;
                }

                // Uruchom jeden serwer
                if (StartServer())
                {
                    RefreshGrid();
                    LogMessage($"Server started on port {currentPort}", LogType.Success);
                    LogMessage($"Access URLs:", LogType.Info);

                    // Pokaż wszystkie dostępne URL
                    foreach (var file in files)
                    {
                        LogMessage($"  - {file.Url}", LogType.Info);
                    }

                    // Pokaż IP dla połączenia z telefonu
                    if (currentHost == "+" || currentHost == "*")
                    {
                        string localIP = GetLocalIP();
                        LogMessage($"For phone access use: http://{localIP}:{currentPort}/", LogType.Success);
                        LogMessage($"Make sure both devices are on the same network!", LogType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnStartAll_Click: {ex.Message}", LogType.Error);
            }
        }

        private bool IsPortInUse(int port)
        {
            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect("localhost", port);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private bool StartServer()
        {
            try
            {
                server = new HttpListener();
                string host = currentHost == "+" ? "*" : currentHost;

                // Dla połączenia z telefonu musimy nasłuchiwać na wszystkich interfejsach
                if (host == "*")
                {
                    server.Prefixes.Add($"http://*:{currentPort}/");
                    LogMessage($"Listening on ALL interfaces (port {currentPort})", LogType.Info);
                }
                else
                {
                    server.Prefixes.Add($"http://{host}:{currentPort}/");
                    LogMessage($"Listening on {host}:{currentPort}", LogType.Info);
                }

                // Optymalizacja dla szybkiego transferu
                server.TimeoutManager.EntityBody = TimeSpan.FromMinutes(60);
                server.TimeoutManager.DrainEntityBody = TimeSpan.FromMinutes(60);
                server.TimeoutManager.RequestQueue = TimeSpan.FromMinutes(60);
                server.TimeoutManager.IdleConnection = TimeSpan.FromMinutes(60);
                server.TimeoutManager.HeaderWait = TimeSpan.FromMinutes(60);

                // Wymagane uprawnienia administratora dla hosta "*" w Windows
                if (host == "*" && Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    LogMessage("NOTE: On Windows, hosting on '*' may require administrator rights", LogType.Warning);
                    LogMessage("If connection fails, try running as Administrator", LogType.Warning);
                }

                server.Start();
                serverRunning = true;

                serverThread = new Thread(() => RunServer());
                serverThread.IsBackground = true;
                serverThread.Start();

                return true;
            }
            catch (HttpListenerException ex)
            {
                if (ex.ErrorCode == 5) // Access denied
                {
                    LogMessage($"ACCESS DENIED: Need administrator rights to host on port {currentPort}", LogType.Error);
                    LogMessage("Please run this application as Administrator", LogType.Error);
                }
                LogMessage($"Cannot start server on port {currentPort}: {ex.Message}", LogType.Error);
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
                if (!serverRunning) return;

                StopServer();
                RefreshGrid();
                LogMessage($"Server stopped on port {currentPort}", LogType.Info);
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in btnStopAll_Click: {ex.Message}", LogType.Error);
            }
        }

        private void StopServer()
        {
            try
            {
                serverRunning = false;

                if (server != null)
                {
                    server.Stop();
                    server.Close();
                    server = null;
                }

                if (serverThread != null && serverThread.IsAlive)
                {
                    serverThread.Join(1000);
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

                if (serverRunning)
                {
                    StopServer();
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
                if (file != null && serverRunning)
                {
                    Clipboard.SetText(file.Url);
                    LogMessage($"Copied to clipboard: {file.Url}", LogType.Success);
                }
                else if (file != null && !serverRunning)
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
                if (file != null && serverRunning)
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

        private void RunServer()
        {
            try
            {
                while (serverRunning && server != null && server.IsListening)
                {
                    try
                    {
                        var context = server.GetContext();

                        ThreadPool.QueueUserWorkItem((ctx) =>
                        {
                            try
                            {
                                HttpListenerContext httpContext = (HttpListenerContext)ctx;
                                string requestedFile = httpContext.Request.Url.Segments[httpContext.Request.Url.Segments.Length - 1];

                                // Loguj połączenie
                                string clientIP = httpContext.Request.RemoteEndPoint?.Address?.ToString() ?? "unknown";
                                this.Invoke(new Action(() =>
                                {
                                    LogMessage($"Connection from: {clientIP} requesting: {requestedFile}", LogType.Info);
                                }));

                                // Znajdź plik po nazwie
                                var file = files.Find(f =>
                                    Path.GetFileName(f.Path).Equals(requestedFile, StringComparison.OrdinalIgnoreCase));

                                if (file != null)
                                {
                                    file.Hits++;

                                    this.Invoke(new Action(() =>
                                    {
                                        LogMessage($"Serving: {requestedFile} to {clientIP}", LogType.Success);
                                        RefreshGrid();
                                    }));

                                    // OPTYMALIZACJA DLA SZYBKIEGO TRANSFERU
                                    using (FileStream fileStream = File.OpenRead(file.Path))
                                    {
                                        httpContext.Response.ContentType = GetContentType(file.Path);
                                        httpContext.Response.ContentLength64 = fileStream.Length;

                                        // Duży bufor 1MB dla maksymalnej przepustowości
                                        byte[] buffer = new byte[1024 * 1024]; // 1MB buffer
                                        int bytesRead;

                                        // Ustaw wysoki priorytet I/O
                                        if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                                        {
                                            Thread.CurrentThread.Priority = ThreadPriority.AboveNormal;
                                        }

                                        Stopwatch sw = Stopwatch.StartNew();
                                        long totalBytes = 0;

                                        while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                                        {
                                            httpContext.Response.OutputStream.Write(buffer, 0, bytesRead);
                                            totalBytes += bytesRead;

                                            // Flush co 10MB dla utrzymania przepływu
                                            if (totalBytes % (10 * 1024 * 1024) == 0)
                                            {
                                                httpContext.Response.OutputStream.Flush();
                                            }

                                            // Przerwij jeśli serwer został zatrzymany
                                            if (!serverRunning) break;
                                        }
                                        sw.Stop();

                                        // Loguj prędkość transferu
                                        double speedMBps = (totalBytes / (1024.0 * 1024.0)) / (sw.ElapsedMilliseconds / 1000.0);
                                        this.Invoke(new Action(() =>
                                        {
                                            LogMessage($"Transfer complete: {FormatFileSize(totalBytes)} in {sw.ElapsedMilliseconds}ms ({speedMBps:F2} MB/s) to {clientIP}", LogType.Success);
                                        }));
                                    }
                                }
                                else
                                {
                                    // Plik nie znaleziony
                                    httpContext.Response.StatusCode = 404;
                                    string notFound = $"File '{requestedFile}' not found. Available files: ";
                                    foreach (var f in files)
                                    {
                                        notFound += $"\n- {Path.GetFileName(f.Path)}";
                                    }
                                    byte[] buffer = System.Text.Encoding.UTF8.GetBytes(notFound);
                                    httpContext.Response.ContentLength64 = buffer.Length;
                                    httpContext.Response.OutputStream.Write(buffer, 0, buffer.Length);

                                    this.Invoke(new Action(() =>
                                    {
                                        LogMessage($"File not found: {requestedFile} requested by {clientIP}", LogType.Warning);
                                    }));
                                }

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
                    if (serverRunning)
                    {
                        serverRunning = false;
                        RefreshGrid();
                        LogMessage($"Server stopped", LogType.Info);
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

                btnAddFile.Enabled = true;
                btnStartAll.Enabled = !serverRunning && files.Count > 0;
                btnStopAll.Enabled = serverRunning;
                btnRemoveFile.Enabled = hasSelection;
                btnRemoveAll.Enabled = files.Count > 0;
                btnCopy.Enabled = hasSelection && serverRunning;
                btnRefreshIP.Enabled = true;
                btnOpenInBrowser.Enabled = hasSelection && serverRunning;
                btnClearLog.Enabled = true;
                numPort.Enabled = !serverRunning;
                comboHostIP.Enabled = !serverRunning;

                if (serverRunning)
                {
                    string hostDisplay = currentHost == "+" ? "*" : currentHost;
                    lblStatus.Text = $"● RUNNING: http://{hostDisplay}:{currentPort}/";
                    lblStatus.ForeColor = successColor;
                }
                else
                {
                    lblStatus.Text = files.Count > 0 ? "○ STOPPED - Ready to start" : "Ready to add files";
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
                int totalHits = 0;
                long totalSize = 0;

                foreach (var f in files)
                {
                    totalHits += f.Hits;

                    try
                    {
                        FileInfo fileInfo = new FileInfo(f.Path);
                        totalSize += fileInfo.Length;
                    }
                    catch { }
                }

                lblServerCount.Text = $"Files: {files.Count}";
                lblTotalHits.Text = $"Total Hits: {totalHits}";
                lblTotalSize.Text = $"Total Size: {FormatFileSize(totalSize)}";

                if (serverRunning)
                {
                    string ipInfo = currentHost == "+" ? GetLocalIP() : currentHost;
                    toolStripStatusLabel.Text = $"Server: {ipInfo}:{currentPort}";
                }
                else
                {
                    toolStripStatusLabel.Text = "Ready";
                }
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR in UpdateStatusBar: {ex.Message}", LogType.Error);
            }
        }

        enum LogType { Info, Error, Success, Warning }

        private void LogMessage(string message, LogType type)
        {
            if (txtLog.InvokeRequired)
            {
                txtLog.Invoke(new Action<string, LogType>(LogMessage), message, type);
                return;
            }

            try
            {
                Color color;
                if (type == LogType.Error)
                    color = errorColor;
                else if (type == LogType.Success)
                    color = successColor;
                else if (type == LogType.Warning)
                    color = warningColor;
                else
                    color = infoColor;

                int start = txtLog.TextLength;
                txtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
                int end = txtLog.TextLength;

                txtLog.Select(start, end - start);
                txtLog.SelectionColor = color;
                txtLog.SelectionLength = 0; // TU BYŁA LITERÓWKA: txtTrLog → txtLog
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
            string ext = System.IO.Path.GetExtension(path).ToLower();
            if (string.IsNullOrEmpty(ext))
                return "application/octet-stream";

            if (ext == ".html" || ext == ".htm")
                return "text/html";
            else if (ext == ".txt")
                return "text/plain";
            else if (ext == ".css")
                return "text/css";
            else if (ext == ".js")
                return "application/javascript";
            else if (ext == ".json")
                return "application/json";
            else if (ext == ".xml")
                return "application/xml";
            else if (ext == ".pdf")
                return "application/pdf";
            else if (ext == ".zip")
                return "application/zip";
            else if (ext == ".rar")
                return "application/x-rar-compressed";
            else if (ext == ".7z")
                return "application/x-7z-compressed";
            else if (ext == ".png")
                return "image/png";
            else if (ext == ".jpg" || ext == ".jpeg")
                return "image/jpeg";
            else if (ext == ".gif")
                return "image/gif";
            else if (ext == ".bmp")
                return "image/bmp";
            else if (ext == ".svg")
                return "image/svg+xml";
            else if (ext == ".mp3")
                return "audio/mpeg";
            else if (ext == ".mp4")
                return "video/mp4";
            else if (ext == ".avi")
                return "video/x-msvideo";
            else if (ext == ".mkv")
                return "video/x-matroska";
            else if (ext == ".mov")
                return "video/quicktime";
            else if (ext == ".wmv")
                return "video/x-ms-wmv";
            else if (ext == ".flv")
                return "video/x-flv";
            else if (ext == ".webm")
                return "video/webm";
            else if (ext == ".m4v")
                return "video/x-m4v";
            else if (ext == ".m4a")
                return "audio/mp4";
            else if (ext == ".wav")
                return "audio/wav";
            else if (ext == ".ogg")
                return "audio/ogg";
            else if (ext == ".flac")
                return "audio/flac";
            else if (ext == ".aac")
                return "audio/aac";
            else if (ext == ".exe")
                return "application/octet-stream";
            else if (ext == ".msi")
                return "application/x-msdownload";
            else if (ext == ".iso")
                return "application/x-iso9660-image";
            else if (ext == ".doc" || ext == ".docx")
                return "application/msword";
            else if (ext == ".xls" || ext == ".xlsx")
                return "application/vnd.ms-excel";
            else if (ext == ".ppt" || ext == ".pptx")
                return "application/vnd.ms-powerpoint";
            else
                return "application/octet-stream";
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            try
            {
                LogMessage("Application closing...", LogType.Info);
                if (serverRunning)
                    StopServer();
                LogMessage("Server stopped", LogType.Info);
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
                if (!serverRunning)
                {
                    // Zaktualizuj port dla wszystkich plików
                    foreach (var file in files)
                    {
                        file.Port = (int)numPort.Value;
                    }
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

                if (!serverRunning)
                {
                    string selected = comboHostIP.SelectedItem.ToString();
                    string hostValue = selected.Contains("(") ?
                        selected.Split('(')[0].Trim() : selected;

                    // Zaktualizuj host dla wszystkich plików
                    foreach (var file in files)
                    {
                        file.Host = hostValue;
                    }
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
                    if (file != null && serverRunning)
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

        private void lblStatus_Click(object sender, EventArgs e)
        {
            // Pusta metoda - tylko do obsługi eventu z designera
        }

        // Puste metody dla designera
        private void lblHostIP_Click(object sender, EventArgs e) { }
        private void toolStripStatusLabel_Click(object sender, EventArgs e) { }
        private void groupLog_Enter(object sender, EventArgs e) { }
    }
}
