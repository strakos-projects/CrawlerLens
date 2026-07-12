using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HtmlAgilityPack;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace CrawlerLens
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly HttpClient _httpClient;
        private readonly string _settingsFilePath = "settings.json";
        private bool _isUpdatingFromCode = false;

        public MainViewModel()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = false // CRITICAL: Stop automatic redirects to track the chain
            };

            _httpClient = new HttpClient(handler);

            // Načtení identity robota a hlaviček při startu
            LoadSettings();
        }

        // ====================================================================
        // 1. ZÁLOŽKA: SETTINGS & IDENTITY (SPOOFING)
        // ====================================================================

        public ObservableCollection<string> AvailableBrowsers { get; } = new()
        {
            "Google Chrome (Windows)",
            "Google Chrome (macOS)",
            "Safari (iPhone / iOS)",
            "Chrome (Android Mobile)",
            "Googlebot (Standard SEO Crawler)",
            "Dumb Bot (Minimal Headers)",
            "Custom..."
        };

        public ObservableCollection<string> AvailableLocales { get; } = new()
        {
            "cs-CZ (Czech)",
            "en-US (English - United States)",
            "en-GB (English - United Kingdom)",
            "de-DE (German - Germany)",
            "Custom..."
        };

        private string _selectedBrowser = "Google Chrome (Windows)";
        public string SelectedBrowser
        {
            get => _selectedBrowser;
            set
            {
                if (SetProperty(ref _selectedBrowser, value) && !_isUpdatingFromCode && value != "Custom...")
                    UpdateRawHeadersPreview();
            }
        }

        private string _selectedLocale = "cs-CZ (Czech)";
        public string SelectedLocale
        {
            get => _selectedLocale;
            set
            {
                if (SetProperty(ref _selectedLocale, value) && !_isUpdatingFromCode && value != "Custom...")
                    UpdateRawHeadersPreview();
            }
        }

        private string _rawHeadersPreview = string.Empty;
        [ObservableProperty] private string _activeHeadersPreview = string.Empty;
        public string RawHeadersPreview
        {
            get => _rawHeadersPreview;
            set
            {
                if (SetProperty(ref _rawHeadersPreview, value))
                {
                    // Pokud uživatel sáhne do textového pole ručně, přepneme comboboxy na "Custom..."
                    if (!_isUpdatingFromCode)
                    {
                        _isUpdatingFromCode = true;
                        if (SelectedBrowser != "Custom...") SelectedBrowser = "Custom...";
                        if (SelectedLocale != "Custom...") SelectedLocale = "Custom...";
                        _isUpdatingFromCode = false;
                    }
                }
            }
        }

        private void UpdateRawHeadersPreview()
        {
            _isUpdatingFromCode = true;

            string userAgent = SelectedBrowser switch
            {
                "Google Chrome (Windows)" => "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Google Chrome (macOS)" => "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
                "Safari (iPhone / iOS)" => "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
                "Chrome (Android Mobile)" => "Mozilla/5.0 (Linux; Android 10; K) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Mobile Safari/537.36",
                "Googlebot (Standard SEO Crawler)" => "Mozilla/5.0 (compatible; Googlebot/2.1; +http://www.google.com/bot.html)",
                "Dumb Bot (Minimal Headers)" => "curl/7.81.0",
                _ => ""
            };

            string acceptLanguage = SelectedLocale switch
            {
                "cs-CZ (Czech)" => "cs-CZ,cs;q=0.9,en;q=0.8",
                "en-US (English - United States)" => "en-US,en;q=0.9",
                "en-GB (English - United Kingdom)" => "en-GB,en;q=0.9,en-US;q=0.8",
                "de-DE (German - Germany)" => "de-DE,de;q=0.9,en;q=0.8",
                _ => ""
            };

            if (SelectedBrowser != "Custom..." || SelectedLocale != "Custom...")
            {
                RawHeadersPreview = $"User-Agent: {userAgent}\r\nAccept-Language: {acceptLanguage}\r\nAccept-Encoding: gzip, deflate, br";
            }

            if (SelectedBrowser == "Dumb Bot (Minimal Headers)")
            {
                RawHeadersPreview = $"User-Agent: {userAgent}\r\nAccept: */*";
            }
            else if (SelectedBrowser != "Custom..." || SelectedLocale != "Custom...")
            {
                RawHeadersPreview = $"User-Agent: {userAgent}\r\nAccept-Language: {acceptLanguage}\r\nAccept-Encoding: gzip, deflate, br";
            }

            _isUpdatingFromCode = false;
        }

        private void LoadSettings()
        {
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        _isUpdatingFromCode = true;
                        SelectedBrowser = settings.SelectedBrowser;
                        SelectedLocale = settings.SelectedLocale;
                        RawHeadersPreview = settings.RawHeadersPreview;
                        _isUpdatingFromCode = false;
                    }
                }
                catch { /* Chyba čtení -> fallback na výchozí hodnoty */ }
            }

            if (string.IsNullOrWhiteSpace(RawHeadersPreview))
                UpdateRawHeadersPreview();

            ApplyHeadersToHttpClient();
        }

        [RelayCommand]
        private async Task SaveSettingsAsync()
        {
            try
            {
                var settings = new AppSettings
                {
                    SelectedBrowser = SelectedBrowser,
                    SelectedLocale = SelectedLocale,
                    RawHeadersPreview = RawHeadersPreview
                };

                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(_settingsFilePath, json);

                ApplyHeadersToHttpClient();
                MessageBox.Show("Settings saved and applied successfully. The crawler will now use the new identity.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ApplyHeadersToHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();

            // Parsování RawHeadersPreview po řádcích a aplikace na klienta
            var lines = RawHeadersPreview.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(new[] { ':' }, 2);
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(key, value);
                }
            }

            ActiveHeadersPreview = RawHeadersPreview;
        }


        // ====================================================================
        // 2. ZÁLOŽKA: VISUAL INSPECTOR (PŮVODNÍ FUNKCE)
        // ====================================================================

        [ObservableProperty] private string _targetUrl = "https://";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _pageTitle = string.Empty;
        [ObservableProperty] private string _metaDescription = string.Empty;
        [ObservableProperty] private string _metaRobots = string.Empty;
        [ObservableProperty] private string _robotsTxtContent = string.Empty;
        [ObservableProperty] private string _canonicalUrl = string.Empty;
        [ObservableProperty] private string _htmlLang = string.Empty;
        [ObservableProperty] private int _totalWordCount;

        public ObservableCollection<MetaTagItem> OpenGraphTags { get; } = new();
        public ObservableCollection<MetaTagItem> TwitterTags { get; } = new();
        public ObservableCollection<string> JsonLdSchemas { get; } = new();
        public ObservableCollection<MetaTagItem> HreflangTags { get; } = new();
        public ObservableCollection<KeywordStat> TopKeywords { get; } = new();

        [RelayCommand]
        private async Task AnalyzeUrlAsync()
        {
            if (!Uri.TryCreate(TargetUrl, UriKind.Absolute, out Uri? uri)) return;

            IsBusy = true;
            ClearData();

            try
            {
                var htmlContent = await _httpClient.GetStringAsync(uri);
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                PageTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
                MetaDescription = GetMetaContent(doc, "name", "description");
                MetaRobots = GetMetaContent(doc, "name", "robots");
                CanonicalUrl = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", string.Empty) ?? string.Empty;
                HtmlLang = doc.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", string.Empty) ?? "Nenastaveno!";

                var ogNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@property, 'og:')]");
                if (ogNodes != null)
                {
                    foreach (var node in ogNodes)
                    {
                        var prop = node.GetAttributeValue("property", string.Empty) ?? string.Empty;
                        var content = node.GetAttributeValue("content", string.Empty) ?? string.Empty;
                        if (prop.Equals("og:image", StringComparison.OrdinalIgnoreCase)) content = MakeAbsoluteUrl(uri, content);
                        OpenGraphTags.Add(new MetaTagItem(prop, content));
                    }
                }

                var twNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@name, 'twitter:')]");
                if (twNodes != null)
                {
                    foreach (var node in twNodes)
                    {
                        var name = node.GetAttributeValue("name", string.Empty) ?? string.Empty;
                        var content = node.GetAttributeValue("content", string.Empty) ?? string.Empty;
                        if (name.Equals("twitter:image", StringComparison.OrdinalIgnoreCase) || name.Equals("twitter:image:src", StringComparison.OrdinalIgnoreCase)) content = MakeAbsoluteUrl(uri, content);
                        TwitterTags.Add(new MetaTagItem(name, content));
                    }
                }

                var jsonNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
                if (jsonNodes != null)
                {
                    foreach (var node in jsonNodes)
                    {
                        JsonLdSchemas.Add(FormatJson(node.InnerText ?? string.Empty));
                    }
                }

                var hrefNodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate' and @hreflang]");
                if (hrefNodes != null)
                {
                    foreach (var node in hrefNodes)
                    {
                        HreflangTags.Add(new MetaTagItem(node.GetAttributeValue("hreflang", string.Empty), node.GetAttributeValue("href", string.Empty)));
                    }
                }

                AnalyzeContent(doc);

                var robotsUrl = new Uri(uri, "/robots.txt");
                var robotsResponse = await _httpClient.GetAsync(robotsUrl);
                if (robotsResponse.IsSuccessStatusCode)
                    RobotsTxtContent = await robotsResponse.Content.ReadAsStringAsync();
                else
                    RobotsTxtContent = $"No robots.txt found ({robotsResponse.StatusCode}).";
            }
            catch (Exception ex)
            {
                PageTitle = $"Error: {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ====================================================================
        // 3. ZÁLOŽKA: AI PROMPT GENERATOR
        // ====================================================================

        [ObservableProperty] private string _urlBatchInput = string.Empty;
        [ObservableProperty] private bool _isGeneratingPrompt;

        [ObservableProperty] private int _promptCharCount;
        [ObservableProperty] private int _promptEstTokens;
        [ObservableProperty] private string _promptEstCost = "$0.000";
        [ObservableProperty] private string _copyButtonText = "Copy to Clipboard";
        [ObservableProperty] private Wpf.Ui.Controls.SymbolRegular _copyButtonIcon = Wpf.Ui.Controls.SymbolRegular.Copy24;

        private string _aiPromptOutput = string.Empty;
        public string AiPromptOutput
        {
            get => _aiPromptOutput;
            set
            {
                SetProperty(ref _aiPromptOutput, value);
                CalculateStats();
            }
        }

        [RelayCommand]
        private async Task GenerateAiReportAsync()
        {
            if (string.IsNullOrWhiteSpace(UrlBatchInput)) return;

            IsGeneratingPrompt = true;
            AiPromptOutput = "Generating AI Context Payload... Please wait.";

            var urls = UrlBatchInput.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(u => u.Trim())
                                    .Distinct()
                                    .ToList();

            var reportBuilder = new StringBuilder();
            reportBuilder.AppendLine("System Instruction: You are a Senior Technical SEO Expert. Review the following \"CrawlerLens Audit Payload\". Analyze technical issues (redirect chains, status codes, missing tags, canonicals) and content quality. Provide an actionable SEO strategy.");

            // NOVÉ: Přibalíme do kontextu pro AI informace o tom, jaké hlavičky (identitu) crawler použil
            reportBuilder.AppendLine("\n=== CRAWLER HTTP REQUEST HEADERS ===");
            reportBuilder.AppendLine($"Simulated Identity: {SelectedBrowser} / Locale: {SelectedLocale}");
            reportBuilder.AppendLine("The requests were sent with the following headers:");
            reportBuilder.AppendLine("--------------------------------------------------");
            reportBuilder.AppendLine(RawHeadersPreview);
            reportBuilder.AppendLine("--------------------------------------------------");

            reportBuilder.AppendLine("\n=== CRAWLERLENS AUDIT PAYLOAD ===\n");

            foreach (var initialUrl in urls)
            {
                if (!Uri.TryCreate(initialUrl, UriKind.Absolute, out Uri? validUri))
                {
                    reportBuilder.AppendLine($"[URL] {initialUrl}\n- Error: Invalid URL format\n");
                    continue;
                }

                reportBuilder.AppendLine($"[URL] {initialUrl}");
                await AppendUrlReportAsync(validUri!, reportBuilder);
                reportBuilder.AppendLine();
            }

            AiPromptOutput = reportBuilder.ToString();
            IsGeneratingPrompt = false;
        }

        private async Task AppendUrlReportAsync(Uri initialUri, StringBuilder reportBuilder)
        {
            int hops = 0;
            string currentUrl = initialUri.ToString();
            string finalHtml = string.Empty;

            reportBuilder.AppendLine("- Redirect Chain:");

            while (hops < 5)
            {
                try
                {
                    var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);
                    var response = await _httpClient.SendAsync(request);
                    int statusCode = (int)response.StatusCode;

                    reportBuilder.AppendLine($"  -> [{statusCode} {response.StatusCode}] {currentUrl}");

                    if (statusCode >= 300 && statusCode <= 399)
                    {
                        Uri? location = response.Headers.Location;
                        if (location == null) break;

                        if (!location.IsAbsoluteUri) location = new Uri(new Uri(currentUrl), location);
                        currentUrl = location.ToString();
                        hops++;
                    }
                    else
                    {
                        finalHtml = await response.Content.ReadAsStringAsync();
                        break;
                    }
                }
                catch (Exception ex)
                {
                    reportBuilder.AppendLine($"  -> [Error] {ex.Message}");
                    break;
                }
            }

            if (hops >= 5) reportBuilder.AppendLine("- Error: Redirect loop or too many hops detected.");

            if (!string.IsNullOrEmpty(finalHtml))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(finalHtml);

                reportBuilder.AppendLine($"- Title: {doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "Missing"}");
                reportBuilder.AppendLine($"- Canonical: {doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", "Missing") ?? "Missing"}");
                reportBuilder.AppendLine($"- HTML Lang: {doc.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", "Missing") ?? "Missing"}");

                var metaRefresh = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='refresh']")?.GetAttributeValue("content", "");
                if (!string.IsNullOrEmpty(metaRefresh)) reportBuilder.AppendLine($"- WARNING: Meta Refresh Redirect detected: {metaRefresh}");

                AnalyzeContent(doc);
                reportBuilder.AppendLine($"- Total Word Count: {TotalWordCount}");
                var keywords = string.Join(", ", TopKeywords.Take(5).Select(k => $"{k.Word} ({k.Density}%)"));
                reportBuilder.AppendLine($"- Top Keywords: {keywords}");
            }
        }

        private void CalculateStats()
        {
            if (string.IsNullOrEmpty(AiPromptOutput))
            {
                PromptCharCount = 0; PromptEstTokens = 0; PromptEstCost = "$0.000";
                return;
            }
            PromptCharCount = AiPromptOutput.Length;
            PromptEstTokens = (int)Math.Ceiling(PromptCharCount / 4.0);
            PromptEstCost = $"${(PromptEstTokens / 1_000_000.0) * 2.50:F4}";
        }

        [RelayCommand]
        private async Task CopyPromptAsync()
        {
            if (string.IsNullOrWhiteSpace(AiPromptOutput)) return;
            try
            {
                Clipboard.SetText(AiPromptOutput);
                CopyButtonText = "Copied!";
                CopyButtonIcon = Wpf.Ui.Controls.SymbolRegular.Checkmark24;
                await Task.Delay(2000);
                CopyButtonText = "Copy to Clipboard";
                CopyButtonIcon = Wpf.Ui.Controls.SymbolRegular.Copy24;
            }
            catch
            {
                CopyButtonText = "Error copying";
                await Task.Delay(2000);
                CopyButtonText = "Copy to Clipboard";
            }
        }

        // ====================================================================
        // POMOCNÉ METODY A FUNKCE
        // ====================================================================

        private string GetMetaContent(HtmlDocument doc, string attribute, string value)
        {
            var node = doc.DocumentNode.SelectSingleNode($"//meta[@{attribute}='{value}']");
            return node?.GetAttributeValue("content", string.Empty) ?? string.Empty;
        }

        private string FormatJson(string unformattedJson)
        {
            if (string.IsNullOrWhiteSpace(unformattedJson)) return string.Empty;
            try
            {
                var parsedJson = JsonSerializer.Deserialize<JsonElement>(unformattedJson);
                return JsonSerializer.Serialize(parsedJson, new JsonSerializerOptions { WriteIndented = true });
            }
            catch { return unformattedJson; }
        }

        private void AnalyzeContent(HtmlDocument doc)
        {
            var nodesToRemove = doc.DocumentNode.SelectNodes("//script | //style");
            if (nodesToRemove != null) foreach (var node in nodesToRemove) node.Remove();

            var innerText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? string.Empty;
            innerText = System.Net.WebUtility.HtmlDecode(innerText);

            var wordRegex = new Regex(@"\b[\p{L}]+\b");
            var matches = wordRegex.Matches(innerText);
            TotalWordCount = matches.Count;

            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "a", "i", "o", "u", "s", "z", "k", "v", "do", "na", "pro", "za", "že", "to", "se", "si", "je", "od", "ze", "ve",
                "the", "of", "and", "in", "to", "is", "for", "on", "with", "as", "by", "at", "it", "this", "that", "are", "or", "be"
            };

            var wordGroups = matches.Select(m => m.Value.ToLowerInvariant())
                                    .Where(w => w.Length > 2 && !stopWords.Contains(w))
                                    .GroupBy(w => w).OrderByDescending(g => g.Count()).Take(15).ToList();

            TopKeywords.Clear();
            foreach (var group in wordGroups)
            {
                double density = TotalWordCount > 0 ? (double)group.Count() / TotalWordCount * 100 : 0;
                TopKeywords.Add(new KeywordStat(group.Key, group.Count(), Math.Round(density, 2)));
            }
        }

        private void ClearData()
        {
            PageTitle = MetaDescription = MetaRobots = RobotsTxtContent = CanonicalUrl = HtmlLang = string.Empty;
            OpenGraphTags.Clear(); TwitterTags.Clear(); JsonLdSchemas.Clear(); HreflangTags.Clear();
            TotalWordCount = 0; TopKeywords.Clear();
        }

        private string MakeAbsoluteUrl(Uri baseUri, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (Uri.TryCreate(baseUri, url, out Uri? absoluteUri)) return absoluteUri.ToString();
            return url;
        }
    }

    // ====================================================================
    // DATOVÉ MODELY A STRUKTURY
    // ====================================================================

    public record KeywordStat(string Word, int Count, double Density);

    public record MetaTagItem(string Key, string Value)
    {
        public bool IsImage => Key.Equals("og:image", StringComparison.OrdinalIgnoreCase) ||
                               Key.Equals("twitter:image", StringComparison.OrdinalIgnoreCase) ||
                               Key.Equals("twitter:image:src", StringComparison.OrdinalIgnoreCase);
    }

    public class AppSettings
    {
        public string SelectedBrowser { get; set; } = "Google Chrome (Windows)";
        public string SelectedLocale { get; set; } = "cs-CZ (Czech)";
        public string RawHeadersPreview { get; set; } = "";
    }
}