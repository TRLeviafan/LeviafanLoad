using Microsoft.Web.WebView2.Core;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression; // Для ZIP
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
namespace LeviafanLoad
{
    public partial class MainWindow : Window
    {
  
        private const string CurrentVersion = "1.0.0"; 
        private const string GithubOwner = "TRLeviafan"; 
        private const string GithubRepo = "LeviafanLoad";
        private string _updateDownloadUrl = "";

        private bool _isDownloading = false;
        private bool _cancelRequested = false;

        private string _defaultDownloadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "LeviafanDownloads");
    
        private static readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();
     
        private string _currentChapterFolder = "";
        private string _currentPagePrefix = "Page_"; 

   
        private bool _isBatchMode = false;
        private bool _isBatchAuto = false;
        private int _batchCurrentIndex = 0;
        private int _batchEndIndex = 0;
        private string _batchUrlPattern = "";

        public MainWindow()
        {
            InitializeComponent();
        
            txtFolder.Text = _defaultDownloadPath;
      
            btnGo.Click += BtnGo_Click;
            btnBack.Click += (s, e) => { if (webView.CanGoBack) webView.GoBack(); };
            btnForward.Click += (s, e) => { if (webView.CanGoForward) webView.GoForward(); };
            btnRefresh.Click += (s, e) => webView.Reload();
            btnFolder.Click += btnFolder_Click;
            btnUpdate.Click += btnUpdate_Click;

            InitializeBrowserAsync();
            CheckForUpdatesAsync();
        }

        private async void InitializeBrowserAsync()
        {
            Log("Инициализация ядра WebView2...");
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "LeviafanBrowser"));
                await webView.EnsureCoreWebView2Async(env);
              
                webView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;
           
                webView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                Log("Браузер успешно загружен и готов к работе.");
              
                NavigateToUrl(txtUrl.Text);
            }
            catch (Exception ex)
            {
                Log($"ОШИБКА инициализации: {ex.Message}");
            }
        }

        private async void CheckForUpdatesAsync()
        {
            try
            {
                Log("Проверка обновлений...");

                // GitHub API требует User-Agent
                if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "LeviafanLoad-Updater");
                }

                string apiUrl = $"https://api.github.com/repos/{GithubOwner}/{GithubRepo}/releases/latest";
                string json = await _httpClient.GetStringAsync(apiUrl);

                using (var doc = System.Text.Json.JsonDocument.Parse(json))
                {
                    var root = doc.RootElement;
                    string latestVersion = root.GetProperty("tag_name").GetString().Replace("v", ""); // Убираем 'v' если версия записана как 'v1.0.1'

                    // Если версия в сети отличается от нашей
                    if (latestVersion != CurrentVersion)
                    {
                        // Ищем ссылку на ZIP архив в массиве прикрепленных файлов (assets)
                        var assets = root.GetProperty("assets");
                        foreach (var asset in assets.EnumerateArray())
                        {
                            string fileName = asset.GetProperty("name").GetString();
                            if (fileName.EndsWith(".zip"))
                            {
                                _updateDownloadUrl = asset.GetProperty("browser_download_url").GetString();

                                // Показываем кнопку в UI
                                Dispatcher.Invoke(() =>
                                {
                                    btnUpdate.Visibility = Visibility.Visible;
                                    btnUpdate.Content = $"Обновить до v{latestVersion}!";
                                });

                                Log($"Найдена новая версия: v{latestVersion}. Готов к загрузке.");
                                break;
                            }
                        }
                    }
                    else
                    {
                        Log("У вас установлена последняя версия.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Ошибка проверки обновлений: {ex.Message}");
            }
        }
        private void Hyperlink_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri)
                {
                    UseShellExecute = true
                });
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Log($"Не удалось открыть ссылку: {ex.Message}");
            }
        }

        private async void btnUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_updateDownloadUrl)) return;

            btnUpdate.IsEnabled = false;
            btnUpdate.Content = "Скачивание...";
            Log("Загрузка обновления начата...");

            await Task.Run(async () =>
            {
                try
                {
                    string tempZipPath = Path.Combine(Path.GetTempPath(), "LeviafanUpdate.zip");
                    string extractPath = Path.Combine(Path.GetTempPath(), "LeviafanUpdateExtract");
                  
                    var updateBytes = await _httpClient.GetByteArrayAsync(_updateDownloadUrl);
                    File.WriteAllBytes(tempZipPath, updateBytes);

                    if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);
                    Directory.CreateDirectory(extractPath);

                    System.IO.Compression.ZipFile.ExtractToDirectory(tempZipPath, extractPath);

                    string currentAppFolder = AppDomain.CurrentDomain.BaseDirectory;
                    string batCode = $@"
                                        @echo off
                                        timeout /t 2 /nobreak > NUL
                                        xcopy /s /y ""{extractPath}\*"" ""{currentAppFolder}""
                                        start """" ""{Path.Combine(currentAppFolder, "LeviafanLoad.exe")}""
                                        del ""{tempZipPath}""
                                        rmdir /s /q ""{extractPath}""
                                        del ""%~f0""
                                        ";
                    string batPath = Path.Combine(currentAppFolder, "update.bat");
                    File.WriteAllText(batPath, batCode);

                    Log("Обновление готово! Перезапуск...");

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo()
                    {
                        FileName = batPath,
                        UseShellExecute = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    });

                    Dispatcher.Invoke(() => Application.Current.Shutdown());
                }
                catch (Exception ex)
                {
                    Log($"Ошибка при установке обновления: {ex.Message}");
                    Dispatcher.Invoke(() =>
                    {
                        btnUpdate.IsEnabled = true;
                        btnUpdate.Content = "Ошибка. Повторить?";
                    });
                }
            });
        }
        private void btnFolder_Click(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Выберите папку для сохранения глав";
                dialog.SelectedPath = txtFolder.Text;
               
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    txtFolder.Text = dialog.SelectedPath;
                }
            }
        }
        private void BtnGo_Click(object sender, RoutedEventArgs e)
        {
            NavigateToUrl(txtUrl.Text);
        }

        private void ResetCaptureButton()
        {
            Dispatcher.Invoke(() =>
            {
                _isDownloading = false;
                _cancelRequested = false;
                _isBatchMode = false;
                btnCapture.IsEnabled = true;
                btnCapture.Content = "ЗАПУСТИТЬ ЗАГРУЗКУ";
                btnCapture.Background = (SolidColorBrush)FindResource("AccentColor");
                Log("--- СИСТЕМА ОСТАНОВЛЕНА И ГОТОВА К НОВОЙ ЗАДАЧЕ ---");
            });
        }

        private void NavigateToUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return;

            // Если пользователь забыл http://
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
            {
                url = "https://" + url;
                txtUrl.Text = url;
            }

            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                Log($"Переход по адресу: {uri.Host}...");
                webView.Source = uri;
            }
            else
            {
                Log("Неверный формат URL.");
            }
        }

        private async void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                txtUrl.Text = webView.Source.ToString();
                Log("Страница загружена.");

                if (_isBatchMode)
                {
                    await Task.Delay(1000);
                    StartPageCapture();
                }
            }
            else
            {
                Log($"Ошибка загрузки страницы: {e.WebErrorStatus}");
                if (_isBatchMode && _isBatchAuto)
                {
                    Log("Авто-загрузка остановлена из-за сетевой ошибки.");
                    _isBatchMode = false;
                }
            }
        }
      
        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string time = DateTime.Now.ToString("HH:mm:ss");
               
                lstLog.Items.Add($"[{time}] {message}");
                if (lstLog.Items.Count > 0)
                {
                    var lastItem = lstLog.Items[lstLog.Items.Count - 1];
                    lstLog.ScrollIntoView(lastItem);
                }

                txtMiniLog.Text = $"Статус: {message}";
            });
        }
        private async Task ProcessBase64Image(string dataUrl, int current)
        {
            try
            {
                var parts = dataUrl.Split(',');
                if (parts.Length != 2) return;

                string mimeType = parts[0];
                string base64Data = parts[1];
               
                byte[] imageBytes = Convert.FromBase64String(base64Data);
              
                string ext = mimeType.Contains("jpeg") || mimeType.Contains("jpg") ? ".jpg" : ".png";
                string fileName = $"{_currentPagePrefix}{current:D4}{ext}";
                string filePath = Path.Combine(_currentChapterFolder, fileName);

                await Task.Run(() => File.WriteAllBytes(filePath, imageBytes));
                Log($"Сохранено: {fileName}");
            }
            catch (Exception ex)
            {
                Log($"Ошибка сохранения Base64 [{current}]: {ex.Message}");
            }
        }

        private async Task ProcessDirectUrl(string url, int current)
        {
            try
            {
                var imageBytes = await _httpClient.GetByteArrayAsync(url);
                
                string ext = url.Contains(".jpg") || url.Contains(".jpeg") ? ".jpg" : ".png";
                string fileName = $"{_currentPagePrefix}{current:D4}{ext}";
                string filePath = Path.Combine(_currentChapterFolder, fileName);

                await Task.Run(() => File.WriteAllBytes(filePath, imageBytes));
                Log($"Сохранено (URL): {fileName}");
            }
            catch (Exception ex)
            {
                Log($"Ошибка скачивания URL [{current}]: {ex.Message}");
            }
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            try
            {
                using (var doc = System.Text.Json.JsonDocument.Parse(args.WebMessageAsJson))
                {
                    var root = doc.RootElement;

                    string type = root.GetProperty("type").GetString();
                    string data = root.TryGetProperty("data", out var d) ? d.GetString() : "";
                    int current = root.TryGetProperty("current", out var c) ? c.GetInt32() : 0;
                    int total = root.TryGetProperty("total", out var t) ? t.GetInt32() : 0;

                    switch (type)
                    {
                        case "log":
                            Log(data);
                            break;

                        case "dataurl":
                            Log($"Получение файла [{current}/{total}] (Base64)...");
                            await ProcessBase64Image(data, current);
                            break;

                        case "url":
                            Log($"Получение файла [{current}/{total}] (Direct URL)...");
                            await ProcessDirectUrl(data, current);
                            break;

                        case "done":
                            // Если страниц нет ИЛИ мы поймали авто-остановку (ошибка 404)
                            if (total == 0 || data == "404")
                            {
                                Log(data == "404" ? "Обнаружена страница 404. Похоже, главы закончились." : "Изображений не найдено.");

                                if (_isBatchMode && _isBatchAuto)
                                {
                                    Log("--- АВТО-ЗАГРУЗКА ЗАВЕРШЕНА ---");
                                }
                                ResetCaptureButton();
                                break;
                            }

                            Log($"Захват завершен. Найдено изображений: {total}");

                            // ЕСЛИ НАЖАЛИ СТОП ВО ВРЕМЯ СКАЧИВАНИЯ
                            if (_cancelRequested)
                            {
                                ResetCaptureButton();
                                break;
                            }

                            await PostProcessChapterAsync();

                            if (_isBatchMode)
                            {
                                _batchCurrentIndex++;
                                LoadNextBatchChapter();
                            }
                            else
                            {
                                ResetCaptureButton(); // Сбрасываем кнопку для одиночной загрузки
                            }
                            break;
                    }
                } 
            }
            catch (Exception ex)
            {
                Log($"Ошибка при обработке сообщения от браузера: {ex.Message}");
            }
        }


        private async Task PostProcessChapterAsync()
        {
            Log("Начало постобработки...");

            bool doZip = false;
            bool combineParts = false;
            bool combineHeight = false;
            int partsCount = 10;
            int sliceHeight = 4000;

            Dispatcher.Invoke(() =>
            {
                doZip = chkZip.IsChecked == true;
                combineParts = radCombineParts.IsChecked == true;
                combineHeight = radCombineHeight.IsChecked == true;
                int.TryParse(txtPartsCount.Text, out partsCount);
                int.TryParse(txtSliceHeight.Text, out sliceHeight);
            });

            await Task.Run(() =>
            {
                try
                {
                    var files = Directory.GetFiles(_currentChapterFolder)
                                         .Where(f => f.EndsWith(".jpg") || f.EndsWith(".png"))
                                         .OrderBy(f => f).ToList();

                    if (files.Count == 0)
                    {
                        Log("В папке нет изображений для обработки.");
                        return;
                    }
                   
                    if (combineParts || combineHeight)
                    {
                        Log("Запуск движка склейки...");

                        int targetWidth = 0;
                        int totalHeight = 0;
                        var fileHeights = new Dictionary<string, int>();
                      
                        foreach (var f in files)
                        {
                            
                            var info = SixLabors.ImageSharp.Image.Identify(f);
                            if (targetWidth == 0) targetWidth = info.Width;

                            int scaledHeight = info.Width != targetWidth ? (int)((double)info.Height * targetWidth / info.Width) : info.Height;
                            fileHeights[f] = scaledHeight;
                            totalHeight += scaledHeight;
                        }
                     
                        using (var bigCanvas = new Image<Rgba32>(targetWidth, totalHeight))
                        {
                            int currentY = 0;
                            foreach (var f in files)
                            {
                                using (var img = SixLabors.ImageSharp.Image.Load(f))
                                {
                                    if (img.Width != targetWidth)
                                    {
                                        img.Mutate(x => x.Resize(targetWidth, fileHeights[f]));
                                    }

                                    bigCanvas.Mutate(x => x.DrawImage(img, new SixLabors.ImageSharp.Point(0, currentY), 1f));
                                } 
                            }

                            Log("Рулон собран. Начинаю нарезку...");
                          
                            foreach (var f in files) File.Delete(f);
                        
                            int partHeight = combineHeight ? sliceHeight : (totalHeight / partsCount);
                            int currentSliceY = 0;
                            int pieceIndex = 1;
                       
                            while (currentSliceY < totalHeight)
                            {
                                int h = Math.Min(partHeight, totalHeight - currentSliceY);
                                using (var slice = bigCanvas.Clone(x => x.Crop(new SixLabors.ImageSharp.Rectangle(0, currentSliceY, targetWidth, h))))
                                {
                                    string outPath = Path.Combine(_currentChapterFolder, $"{_currentPagePrefix}{pieceIndex:D4}.png");
                                    slice.SaveAsPng(outPath);
                                }
                                currentSliceY += h;
                                pieceIndex++;
                            }
                        }
                        Log("Склейка и нарезка успешно завершены.");
                    }
                   
                    if (doZip)
                    {
                        Log("Создание ZIP-архива...");
                     
                        string zipPath = _currentChapterFolder + ".zip";

                        if (File.Exists(zipPath)) File.Delete(zipPath);

                        ZipFile.CreateFromDirectory(_currentChapterFolder, zipPath, CompressionLevel.Optimal, false);
                        Log($"Архив готов: {Path.GetFileName(zipPath)}");

                       
                        Directory.Delete(_currentChapterFolder, true);
                        Log("Временная папка удалена.");
                    }

                    Log("ПОЛНЫЙ ЦИКЛ ЗАВЕРШЕН!");
                }
                catch (Exception ex)
                {
                    Log($"Ошибка постобработки: {ex.Message}");
                }
            });
        }

        private void btnCapture_Click(object sender, RoutedEventArgs e)
        {
            
            if (_isDownloading)
            {
                Log("Запрошена остановка! Дожидаемся завершения текущей операции...");
                _cancelRequested = true;
                btnCapture.IsEnabled = false; 
                btnCapture.Content = "ОСТАНАВЛИВАЮ...";
                return;
            }

            _isDownloading = true;
            _cancelRequested = false;

            btnCapture.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38));
            btnCapture.Content = "ОСТАНОВИТЬ ЗАГРУЗКУ";

            if (chkBatchMode.IsChecked == true)
            {
                _isBatchMode = true;
                _isBatchAuto = chkBatchAuto.IsChecked == true;
                int.TryParse(txtBatchStart.Text, out _batchCurrentIndex);
                int.TryParse(txtBatchEnd.Text, out _batchEndIndex);
                _batchUrlPattern = txtBatchUrl.Text;

                Log($"--- ЗАПУСК ПАКЕТНОЙ ЗАГРУЗКИ ---");
                LoadNextBatchChapter();
            }
            else
            {
                _isBatchMode = false;
                StartPageCapture();
            }
        }

        private void LoadNextBatchChapter()
        {
            if (_cancelRequested)
            {
                ResetCaptureButton();
                return;
            }

            if (!_isBatchAuto && _batchCurrentIndex > _batchEndIndex)
            {
                Log("Пакетная загрузка успешно завершена!");
                ResetCaptureButton(); 
                return;
            }

            string targetUrl = _batchUrlPattern.Replace("{i}", _batchCurrentIndex.ToString());
            Log($"[{_batchCurrentIndex}] Переход к следующей главе...");
            NavigateToUrl(targetUrl);
        }


        private async void StartPageCapture()
        {
            Log("Запуск глубокого сканирования страницы...");
            
            string currentIndexStr = _isBatchMode ? _batchCurrentIndex.ToString() : txtBatchStart.Text;
            string folderName = txtOutputNamePattern.Text.Replace("{i}", currentIndexStr);

            _currentChapterFolder = Path.Combine(txtFolder.Text, folderName);
            Directory.CreateDirectory(_currentChapterFolder);
            _currentPagePrefix = txtPagePrefix.Text;
          
            string js = @"(async () => {
                    if (document.title.includes('404') || document.body.innerText.includes('Страница не найдена')) {
                        chrome.webview.postMessage({ type: 'done', data: '404', current: 0, total: 0 });
                        return; // Мгновенно прерываем скрипт
                    }
                    const sendMsg = (type, data, current, total) => chrome.webview.postMessage({ type, data, current, total });
        
                    sendMsg('log', 'Прокрутка страницы для загрузки всех элементов...', 0, 0);
                    let lastScroll = 0;
                    let scrollAttempts = 0;
                    while(true) {
                        window.scrollBy(0, 1000);
                        await new Promise(r => setTimeout(r, 150));
                        if (window.scrollY === lastScroll) {
                            scrollAttempts++;
                            if (scrollAttempts > 3) break;
                        } else { scrollAttempts = 0; }
                        lastScroll = window.scrollY;
                    }
        
                    await new Promise(r => setTimeout(r, 1000));
                    sendMsg('log', 'Сбор ссылок и обход защиты...', 0, 0);
                    let collectedUrls = new Set();
                    let canvasDataUrls = [];
        
                    document.querySelectorAll('img').forEach(img => {
                        let src = img.getAttribute('data-src') || img.getAttribute('data-lazy-src') || img.getAttribute('data-original') || img.dataset.src || img.src;
                        if (!src || src.includes('data:image/gif') || src.includes('data:image/svg')) return;
                        if (img.naturalWidth > 0 && img.naturalWidth < 300) return;
                        collectedUrls.add(src);
                    });
        
                    document.querySelectorAll('canvas').forEach(canvas => {
                        if (canvas.width > 300) canvasDataUrls.push(canvas.toDataURL('image/png'));
                    });
        
                    let urlsArray = Array.from(collectedUrls);
                    let total = urlsArray.length + canvasDataUrls.length;
        
                    if (total === 0) { sendMsg('done', '', 0, 0); return; }

                    let current = 0;
                    for(let b64 of canvasDataUrls) {
                        current++; sendMsg('dataurl', b64, current, total);
                    }

                    for(let url of urlsArray) {
                        current++;
                        if (url.startsWith('data:')) {
                            sendMsg('dataurl', url, current, total); continue;
                        }
                        try {
                            const resp = await fetch(url);
                            if (!resp.ok) throw new Error('Bad status');
                            const buffer = await resp.arrayBuffer();
                            const bytes = new Uint8Array(buffer);
                            let binary = '';
                            const chunkSize = 8192;
                            for (let i = 0; i < bytes.length; i += chunkSize) {
                                binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunkSize));
                            }
                            const base64 = btoa(binary);
                            const mimeType = resp.headers.get('Content-Type') || 'image/jpeg';
                            sendMsg('dataurl', `data:${mimeType};base64,${base64}`, current, total);
                        } catch(e) {
                            sendMsg('url', url, current, total);
                        }
                    }
                    sendMsg('done', 'Все изображения переданы', total, total);
                })();";

            await webView.ExecuteScriptAsync(js);
        }
    }
}