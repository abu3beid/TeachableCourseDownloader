using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace VideoDownloader
{
    public partial class MainWindow : Wpf.Ui.Controls.FluentWindow
    {
        private const string YtDlpFileName = "yt-dlp.exe";
        private string _ytDlpPath = string.Empty;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        private string _latestM3u8Url = string.Empty;
        private bool _isScraping = false;

        public MainWindow()
        {
            InitializeComponent();
            LoadHistory();
            
            // Clear placeholder text on first focus
            var textBox = BrowserAddressBox.Template.FindName("PART_EditableTextBox", BrowserAddressBox) as System.Windows.Controls.TextBox;
            if (textBox != null)
            {
                textBox.GotFocus += (s, e) =>
                {
                    if (BrowserAddressBox.Text == "Paste course URL here and click Go...")
                        BrowserAddressBox.Text = "";
                };
            }
            else
            {
                BrowserAddressBox.GotFocus += (s, e) =>
                {
                    if (BrowserAddressBox.Text == "Paste course URL here and click Go...")
                        BrowserAddressBox.Text = "";
                };
            }
            
            InitializeBrowser();
        }

        private void LoadHistory()
        {
            try
            {
                if (File.Exists(HistoryFilePath))
                {
                    var lines = File.ReadAllLines(HistoryFilePath);
                    foreach (var line in lines)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                            BrowserAddressBox.Items.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                LogScraper($"Could not load history: {ex.Message}");
            }
        }

        private async void InitializeBrowser()
        {
            try
            {
                string dataFolder = Path.Combine(Path.GetTempPath(), "VideoDownloaderBrowserData");
                string flagFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "clear_cookies.flag");

                if (File.Exists(flagFile))
                {
                    try
                    {
                        if (Directory.Exists(dataFolder))
                        {
                            Directory.Delete(dataFolder, true);
                        }
                        File.Delete(flagFile);
                        LogScraper("Browser profile was completely hard-reset. All cookies and local storage are gone.");
                    }
                    catch (Exception ex)
                    {
                        LogScraper($"Could not delete browser data folder: {ex.Message}");
                    }
                }

                var env = await CoreWebView2Environment.CreateAsync(null, dataFolder);
                await webView.EnsureCoreWebView2Async(env);
                
                // Mute the browser so auto-playing videos don't make noise
                webView.CoreWebView2.IsMuted = true;
                
                webView.CoreWebView2.WebResourceResponseReceived += CoreWebView2_WebResourceResponseReceived;
                
                LogScraper("Browser engine initialized. Please paste your course URL in the address bar and click Go.");
            }
            catch (Exception ex)
            {
                LogScraper($"Error initializing browser: {ex.Message}");
            }
        }

        private void CoreWebView2_WebResourceResponseReceived(object sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
        {
            if (e.Request.Uri.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
            {
                _latestM3u8Url = e.Request.Uri;
                Dispatcher.Invoke(() => LogScraper($"[Detected Stream]: {e.Request.Uri}"));
            }
        }

        private void BrowserAddressBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BrowserGoButton_Click(this, new RoutedEventArgs());
            }
        }

        private string HistoryFilePath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "url_history.txt");

        private void BrowserGoButton_Click(object sender, RoutedEventArgs e)
        {
            string url = BrowserAddressBox.Text.Trim();
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri result))
            {
                webView.Source = result;
                
                if (!BrowserAddressBox.Items.Contains(url))
                {
                    BrowserAddressBox.Items.Insert(0, url);
                    SaveHistory();
                }
            }
        }

        private void SaveHistory()
        {
            try
            {
                var urls = new System.Collections.Generic.List<string>();
                foreach (var item in BrowserAddressBox.Items)
                {
                    if (item is string s) urls.Add(s);
                }
                File.WriteAllLines(HistoryFilePath, urls);
            }
            catch (Exception ex)
            {
                LogScraper($"Could not save history: {ex.Message}");
            }
        }

        private void LogScraper(string message)
        {
            ScraperLogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            ScraperLogTextBox.ScrollToEnd();
        }

        private async void StartScrapingButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isScraping)
            {
                _isScraping = false;
                StartScrapingButton.Content = "Start Auto-Download";
                StartScrapingButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#28A745"));
                LogScraper("Auto-Scraping stopped.");
                return;
            }

            _isScraping = true;
            StartScrapingButton.Content = "Stop Auto-Download";
            StartScrapingButton.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DC3545"));
            
            await EnsureYtDlpExists();

            LogScraper("Starting Auto-Scrape loop...");
            await ScrapeLoop();
        }

        private async Task<bool> ClickNextButtonAsync()
        {
            LogScraper("Looking for 'Next' button to advance to next lecture...");
            string clickNextJs = @"
                (function() {
                    var exactBtn = document.getElementById('lecture_content_complete_button');
                    if (exactBtn) {
                        exactBtn.click();
                        return 'true';
                    }
                    
                    var elements = document.querySelectorAll('a, button, span, div');
                    for (var i = 0; i < elements.length; i++) {
                        var text = elements[i].innerText;
                        if (text) {
                            text = text.toLowerCase().trim();
                            if (text === 'next' || text.includes('complete and continue')) {
                                elements[i].click();
                                return 'true';
                            }
                        }
                    }
                    return 'false';
                })();
            ";
            var clickedRaw = await webView.ExecuteScriptAsync(clickNextJs);
            
            if (clickedRaw != null && clickedRaw.Contains("true"))
            {
                LogScraper("Clicked Next button. Waiting 5 seconds for page load...");
                await Task.Delay(5000);
                return true;
            }
            return false;
        }

        private async Task ScrapeLoop()
        {
            int waitCount = 0;
            int reloadCount = 0;

            while (_isScraping)
            {
                // Check if internet is connected
                string isOnlineRaw = await webView.ExecuteScriptAsync("navigator.onLine");
                bool isOnline = isOnlineRaw != null && isOnlineRaw.Contains("true");
                
                if (!isOnline)
                {
                    LogScraper("Internet disconnected! Retrying infinitely until back...");
                    await Task.Delay(5000);
                    continue;
                }

                if (string.IsNullOrEmpty(_latestM3u8Url))
                {
                    LogScraper("No .m3u8 stream detected yet. Waiting 3 seconds...");
                    await Task.Delay(3000);
                    waitCount++;

                    if (waitCount >= 5) 
                    {
                        if (reloadCount >= 3)
                        {
                            LogScraper("Reloaded 3 times and still no video found. Assuming non-video lecture. Skipping to next...");
                            waitCount = 0;
                            reloadCount = 0;
                            
                            bool clickedNext = await ClickNextButtonAsync();
                            if (!clickedNext)
                            {
                                LogScraper("Could not find 'Next' button. Reached end of course or scraper needs tweaking.");
                                _isScraping = false;
                                StartScrapingButton.Content = "Start Auto-Download";
                                break;
                            }
                            continue;
                        }
                        
                        LogScraper($"Timeout reached (Reload {reloadCount + 1}/3). Reloading page...");
                        waitCount = 0;
                        reloadCount++;
                        webView.Reload();
                    }
                    continue;
                }

                // If we found a video, reset counts
                waitCount = 0;
                reloadCount = 0;

                string currentM3u8 = _latestM3u8Url;
                
                // Get Title via JS
                string titleRaw = await webView.ExecuteScriptAsync("document.title");
                string title = titleRaw.Trim('"').Replace("\\u0027", "'").Replace("\\\"", "\"");
                title = string.Join("_", title.Split(Path.GetInvalidFileNameChars()));
                if (string.IsNullOrWhiteSpace(title)) title = "Lecture_" + DateTime.Now.Ticks;

                string moduleJs = @"
                    (function() {
                        try {
                            var activeEl = document.querySelector('.section-item.active, li.active, a.active, .item.active, .active, .current, .selected');
                            
                            if (!activeEl) {
                                var match = window.location.href.match(/lectures\/(\d+)/);
                                if (match && match[1]) {
                                    activeEl = document.querySelector('[data-lecture-id=""' + match[1] + '""]');
                                }
                            }
                            
                            if (!activeEl) {
                                var links = document.querySelectorAll('a');
                                for(var i=0; i<links.length; i++) {
                                    var href = links[i].getAttribute('href');
                                    if (href && href.length > 5 && (window.location.pathname.endsWith(href) || window.location.href.includes(href))) {
                                        activeEl = links[i];
                                        break;
                                    }
                                }
                            }
                            
                            if (activeEl) {
                                var curr = activeEl;
                                while(curr && curr !== document.body) {
                                    var prev = curr.previousElementSibling;
                                    while (prev) {
                                        if (prev.classList && prev.classList.contains('section-title')) {
                                            return prev.innerText.replace(/\n/g, ' ').trim();
                                        }
                                        var childTitle = prev.querySelector('.section-title');
                                        if (childTitle) {
                                            return childTitle.innerText.replace(/\n/g, ' ').trim();
                                        }
                                        prev = prev.previousElementSibling;
                                    }
                                    curr = curr.parentElement;
                                }
                            }
                        } catch(e) {}
                        return 'Uncategorized';
                    })();
                ";
                string moduleRaw = await webView.ExecuteScriptAsync(moduleJs);
                string moduleName = moduleRaw.Trim('"').Replace("\\u0027", "'").Replace("\\\"", "\"");
                moduleName = string.Join("_", moduleName.Split(Path.GetInvalidFileNameChars())).Trim();
                if (string.IsNullOrWhiteSpace(moduleName)) moduleName = "Uncategorized";

                LogScraper($"Extracted Module: {moduleName}");
                LogScraper($"Starting download for: {title}");
                
                // Construct yt-dlp arguments
                string downloadsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, moduleName);
                if (!Directory.Exists(downloadsPath))
                {
                    Directory.CreateDirectory(downloadsPath);
                }

                string refererUrl = webView.Source.ToString();
                
                string extraArgs = $"--add-header \"Referer: {refererUrl}\" ";
                
                if (currentM3u8.Contains("hotmart.com", StringComparison.OrdinalIgnoreCase))
                {
                    extraArgs += "--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36 Edg/151.0.0.0\" ";
                    extraArgs += "--add-header \"Origin: https://player.hotmart.com\" ";
                    extraArgs += "--add-header \"Referer: https://player.hotmart.com/\" ";
                }

                string fileOutput = Path.Combine(downloadsPath, $"{title}.mp4");
                
                var process = new Process();
                process.StartInfo.FileName = _ytDlpPath;
                process.StartInfo.Arguments = $"{extraArgs}-o \"{fileOutput}\" \"{currentM3u8}\"";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;

                var tcs = new TaskCompletionSource<bool>();
                process.Exited += (s, e) => { tcs.TrySetResult(true); };
                process.EnableRaisingEvents = true;

                process.OutputDataReceived += (s, e) => { if (e.Data != null) Dispatcher.Invoke(() => LogScraper($"[yt-dlp] {e.Data}")); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) Dispatcher.Invoke(() => LogScraper($"[yt-dlp ERROR] {e.Data}")); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await tcs.Task;
                
                int exitCode = process.ExitCode;
                process.Dispose();

                if (exitCode != 0)
                {
                    LogScraper($"yt-dlp failed (Exit Code {exitCode}). Retrying infinitely in 5 seconds...");
                    await Task.Delay(5000);
                    continue; // Skip clicking next, we will retry the exact same m3u8!
                }

                LogScraper($"Download finished for: {title}");

                if (!_isScraping) break;

                // Clear latest m3u8 so we wait for the next lecture's network request to trigger
                _latestM3u8Url = string.Empty;

                bool clicked = await ClickNextButtonAsync();
                if (!clicked)
                {
                    LogScraper("Could not find a 'Next' button. Reached end of course or scraper needs tweaking.");
                    _isScraping = false;
                    StartScrapingButton.Content = "Start Auto-Download";
                    break;
                }
            }
        }

        private async Task EnsureYtDlpExists()
        {
            if (File.Exists(_ytDlpPath)) return;
            await ExtractYtDlpAsync();
        }

        private Task ExtractYtDlpAsync()
        {
            return Task.Run(() =>
            {
                string tempFolder = Path.GetTempPath();
                _ytDlpPath = Path.Combine(tempFolder, YtDlpFileName);

                var assembly = Assembly.GetExecutingAssembly();
                string resourceName = "VideoDownloader.yt-dlp.exe";

                using (Stream? resourceStream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (resourceStream == null)
                    {
                        throw new Exception($"Embedded resource '{resourceName}' not found.");
                    }

                    using (FileStream fileStream = new FileStream(_ytDlpPath, FileMode.Create, FileAccess.Write))
                    {
                        resourceStream.CopyTo(fileStream);
                    }
                }
            });
        }


    }
}