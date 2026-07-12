using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HtmlAgilityPack;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
namespace CrawlerLens // Změněn namespace na hlavní (kořenový)
{
    public partial class MainViewModel : ObservableObject
    {
        // --- AI PROMPT GENERATOR PROPERTIES ---

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
        // --- POMOCNÁ METODA PRO VÝPOČET STATISTIK ---
        private void CalculateStats()
        {
            if (string.IsNullOrEmpty(AiPromptOutput))
            {
                PromptCharCount = 0;
                PromptEstTokens = 0;
                PromptEstCost = "$0.000";
                return;
            }

            PromptCharCount = AiPromptOutput.Length;

            // Hrubý odhad: 1 token = cca 4 anglické znaky (standardní pravidlo pro OpenAI modely)
            PromptEstTokens = (int)Math.Ceiling(PromptCharCount / 4.0);

            // Odhad ceny: např. GPT-4o-mini aktuálně stojí cca $0.150 za 1 milion vstupních tokenů
            double costPerMillionTokens = 2.50;
            double cost = (PromptEstTokens / 1_000_000.0) * costPerMillionTokens;

            PromptEstCost = $"${cost:F4}";
        }

        // --- COMMAND: COPY TO CLIPBOARD ---
        [RelayCommand]
        private async Task CopyPromptAsync()
        {
            if (string.IsNullOrWhiteSpace(AiPromptOutput)) return;

            try
            {
                // Zkopírování do systémové schránky (vyžaduje System.Windows)
                System.Windows.Clipboard.SetText(AiPromptOutput);

                // Vizuální zpětná vazba pro uživatele
                CopyButtonText = "Copied!";
                CopyButtonIcon = Wpf.Ui.Controls.SymbolRegular.Checkmark24;

                // Počkáme 2 sekundy a vrátíme tlačítko do původního stavu
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
        // Statistiky
        [ObservableProperty] private int _promptCharCount;
        [ObservableProperty] private int _promptEstTokens;
        [ObservableProperty] private string _promptEstCost = "$0.000";

        // Tlačítko kopírování (stav)
        [ObservableProperty] private string _copyButtonText = "Copy to Clipboard";
        [ObservableProperty] private Wpf.Ui.Controls.SymbolRegular _copyButtonIcon = Wpf.Ui.Controls.SymbolRegular.Copy24;
        private readonly HttpClient _httpClient;
        [ObservableProperty] private string _canonicalUrl = string.Empty;
        [ObservableProperty] private int _totalWordCount;
        // --- AI PROMPT GENERATOR PROPERTIES ---
        [ObservableProperty] private string _urlBatchInput = string.Empty;
        [ObservableProperty] private bool _isGeneratingPrompt;

        // --- AI PROMPT COMMAND ---
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
            reportBuilder.AppendLine("\n=== CRAWLERLENS AUDIT PAYLOAD ===\n");

            foreach (var initialUrl in urls)
            {
                if (!Uri.TryCreate(initialUrl, UriKind.Absolute, out Uri? validUri))
                {
                    reportBuilder.AppendLine($"[URL] {initialUrl}");
                    reportBuilder.AppendLine("- Error: Invalid URL format\n");
                    continue;
                }

                reportBuilder.AppendLine($"[URL] {initialUrl}");
                await AppendUrlReportAsync(validUri, reportBuilder);
                reportBuilder.AppendLine(); // Empty line between URLs
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

                    // Zvládneme běžná přesměrování (301, 302, 307, 308)
                    if (statusCode >= 300 && statusCode <= 399)
                    {
                        Uri? location = response.Headers.Location;
                        if (location == null) break;

                        if (!location.IsAbsoluteUri)
                        {
                            location = new Uri(new Uri(currentUrl), location);
                        }
                        currentUrl = location.ToString();
                        hops++;
                    }
                    else
                    {
                        // Pokud projde 200 OK, nebo spadne na 404/403, stáhneme HTML
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

            if (hops >= 5)
            {
                reportBuilder.AppendLine("- Error: Redirect loop or too many hops detected.");
            }

            // Parsování kompletního HTML pro ultimátní AI Payload
            if (!string.IsNullOrEmpty(finalHtml))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(finalHtml);

                var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? "Missing";
                var canonical = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", "Missing") ?? "Missing";
                var metaDesc = doc.DocumentNode.SelectSingleNode("//meta[@name='description']")?.GetAttributeValue("content", "Missing") ?? "Missing";
                var metaRobots = doc.DocumentNode.SelectSingleNode("//meta[@name='robots']")?.GetAttributeValue("content", "Missing (Default: Index, Follow)") ?? "Missing";
                var h1 = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText?.Trim() ?? "Missing";
                var htmlLang = doc.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", "Missing") ?? "Missing";

                var metaRefresh = doc.DocumentNode.SelectSingleNode("//meta[@http-equiv='refresh']")?.GetAttributeValue("content", "");
                if (!string.IsNullOrEmpty(metaRefresh))
                {
                    reportBuilder.AppendLine($"- WARNING: Client-Side Meta Refresh Redirect detected: {metaRefresh}");
                }

                reportBuilder.AppendLine($"- Indexability (Meta Robots): {metaRobots}");
                reportBuilder.AppendLine($"- Title: {title}");
                reportBuilder.AppendLine($"- H1: {h1}");
                reportBuilder.AppendLine($"- Description: {metaDesc}");
                reportBuilder.AppendLine($"- HTML Lang: {htmlLang}");
                reportBuilder.AppendLine($"- Canonical: {canonical}");

                var hreflangs = doc.DocumentNode.SelectNodes("//link[@rel='alternate' and @hreflang]");
                if (hreflangs != null)
                {
                    var langList = hreflangs.Select(n => $"{n.GetAttributeValue("hreflang", "")} ({n.GetAttributeValue("href", "")})");
                    reportBuilder.AppendLine($"- Hreflang: {string.Join(", ", langList)}");
                }

                // --- DOPLNĚNO PRO AI: Open Graph a Twitter ---
                var ogNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@property, 'og:')]");
                if (ogNodes != null)
                {
                    var ogList = ogNodes.Select(n => $"{n.GetAttributeValue("property", "")}: {n.GetAttributeValue("content", "")}");
                    reportBuilder.AppendLine($"- Open Graph: {string.Join(" | ", ogList)}");
                }

                var twNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@name, 'twitter:')]");
                if (twNodes != null)
                {
                    var twList = twNodes.Select(n => $"{n.GetAttributeValue("name", "")}: {n.GetAttributeValue("content", "")}");
                    reportBuilder.AppendLine($"- Twitter Cards: {string.Join(" | ", twList)}");
                }

                // --- DOPLNĚNO PRO AI: Schema.org JSON-LD ---
                var jsonNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
                if (jsonNodes != null)
                {
                    reportBuilder.AppendLine($"- Schema.org (JSON-LD): Found {jsonNodes.Count} script(s)");
                    int schemaIdx = 1;
                    foreach (var node in jsonNodes)
                    {
                        var rawJson = node.InnerText?.Trim() ?? string.Empty;
                        var formatted = FormatJson(rawJson); // Využíváme tvoji stávající formátovací metodu
                        reportBuilder.AppendLine($"  [Schema {schemaIdx++}]:\n{formatted}");
                    }
                }

                // Analýza textu
                AnalyzeContent(doc);
                reportBuilder.AppendLine($"- Total Word Count: {TotalWordCount}");

                var keywords = string.Join(", ", TopKeywords.Take(5).Select(k => $"{k.Word} ({k.Density}%)"));
                reportBuilder.AppendLine($"- Top Keywords: {keywords}");

                // --- DOPLNĚNO PRO AI: Stažení robots.txt pro danou doménu ---
                try
                {
                    var robotsUrl = new Uri(new Uri(currentUrl), "/robots.txt");
                    var robotsResponse = await _httpClient.GetAsync(robotsUrl);
                    if (robotsResponse.IsSuccessStatusCode)
                    {
                        var robotsTxt = await robotsResponse.Content.ReadAsStringAsync();
                        reportBuilder.AppendLine($"- robots.txt: Found");
                        reportBuilder.AppendLine($"\n=== ROBOTS.TXT CONTENT ===\n{robotsTxt}\n==========================");
                    }
                    else
                    {
                        reportBuilder.AppendLine($"- robots.txt: Not Found ({robotsResponse.StatusCode})");
                    }
                }
                catch
                {
                    reportBuilder.AppendLine($"- robots.txt: Error fetching");
                }
            }
        }
        public ObservableCollection<KeywordStat> TopKeywords { get; } = new();
        public MainViewModel()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                AllowAutoRedirect = false // CRITICAL: Stop automatic redirects to track the chain
            };

            _httpClient = new HttpClient(handler);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 CrawlerLens/1.0");
        }

        [ObservableProperty]
        private string _targetUrl = "https://";

        [ObservableProperty] private string _htmlLang = string.Empty;
        public ObservableCollection<MetaTagItem> HreflangTags { get; } = new();

        [ObservableProperty]
        private bool _isBusy;

        // Základní SEO (přidáno inicializování string.Empty pro vyřešení Nullable warnings)
        [ObservableProperty] private string _pageTitle = string.Empty;
        [ObservableProperty] private string _metaDescription = string.Empty;
        [ObservableProperty] private string _metaRobots = string.Empty;
        [ObservableProperty] private string _robotsTxtContent = string.Empty;

        // Rozšířená data
        public ObservableCollection<MetaTagItem> OpenGraphTags { get; } = new();
        public ObservableCollection<MetaTagItem> TwitterTags { get; } = new();
        public ObservableCollection<string> JsonLdSchemas { get; } = new();

        [RelayCommand]
        private async Task AnalyzeUrlAsync()
        {
            if (!Uri.TryCreate(TargetUrl, UriKind.Absolute, out Uri uri)) return;

            IsBusy = true;
            ClearData();

            try
            {
                // 1. Fetch main HTML
                var htmlContent = await _httpClient.GetStringAsync(uri);
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                // 2. Parse Basic SEO
                PageTitle = doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim() ?? string.Empty;
                MetaDescription = GetMetaContent(doc, "name", "description");
                MetaRobots = GetMetaContent(doc, "name", "robots");

                // NOVÉ: Získání Canonical URL
                CanonicalUrl = doc.DocumentNode.SelectSingleNode("//link[@rel='canonical']")?.GetAttributeValue("href", string.Empty) ?? string.Empty;

                // 3. Parse Open Graph (og:*)
                var ogNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@property, 'og:')]");
                if (ogNodes != null)
                {
                    foreach (var node in ogNodes)
                    {
                        var prop = node.GetAttributeValue("property", string.Empty) ?? string.Empty;
                        var content = node.GetAttributeValue("content", string.Empty) ?? string.Empty;

                        // Pokud je to obrázek, pokusíme se vytvořit absolutní URL pro vykreslení ve WPF
                        if (prop.Equals("og:image", StringComparison.OrdinalIgnoreCase))
                            content = MakeAbsoluteUrl(uri, content);

                        OpenGraphTags.Add(new MetaTagItem(prop, content));
                    }
                }

                // 4. Parse Twitter Cards (twitter:*)
                var twNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@name, 'twitter:')]");
                if (twNodes != null)
                {
                    foreach (var node in twNodes)
                    {
                        var name = node.GetAttributeValue("name", string.Empty) ?? string.Empty;
                        var content = node.GetAttributeValue("content", string.Empty) ?? string.Empty;

                        if (name.Equals("twitter:image", StringComparison.OrdinalIgnoreCase) || name.Equals("twitter:image:src", StringComparison.OrdinalIgnoreCase))
                            content = MakeAbsoluteUrl(uri, content);

                        TwitterTags.Add(new MetaTagItem(name, content));
                    }
                }

                // 5. Parse JSON-LD Schema.org
                var jsonNodes = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
                if (jsonNodes != null)
                {
                    foreach (var node in jsonNodes)
                    {
                        var rawJson = node.InnerText ?? string.Empty;
                        JsonLdSchemas.Add(FormatJson(rawJson));
                    }
                }
                HtmlLang = doc.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", string.Empty) ?? "Nenastaveno!";

                var hrefNodes = doc.DocumentNode.SelectNodes("//link[@rel='alternate' and @hreflang]");
                if (hrefNodes != null)
                {
                    foreach (var node in hrefNodes)
                    {
                        var langCode = node.GetAttributeValue("hreflang", string.Empty);
                        var alternateUrl = node.GetAttributeValue("href", string.Empty);
                        HreflangTags.Add(new MetaTagItem(langCode, alternateUrl));
                    }
                }
                // 5.5 Content & Keyword Analysis
                AnalyzeContent(doc);
                // 6. Fetch robots.txt
                var robotsUrl = new Uri(uri, "/robots.txt");
                var robotsResponse = await _httpClient.GetAsync(robotsUrl);
                if (robotsResponse.IsSuccessStatusCode)
                {
                    RobotsTxtContent = await robotsResponse.Content.ReadAsStringAsync();
                }
                else
                {
                    RobotsTxtContent = "No robots.txt found (404).";
                }
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

        private string GetMetaContent(HtmlDocument doc, string attribute, string value)
        {
            var node = doc.DocumentNode.SelectSingleNode($"//meta[@{attribute}='{value}']");
            return node?.GetAttributeValue("content", string.Empty) ?? string.Empty; // Bezpečné ošetření null
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
            // 1. Ochrana: Odstraníme skripty a styly, abychom nečetli kód
            var nodesToRemove = doc.DocumentNode.SelectNodes("//script | //style");
            if (nodesToRemove != null)
            {
                foreach (var node in nodesToRemove) node.Remove();
            }

            // 2. Vytáhneme čistý text z body a dekódujeme HTML entity (např. &nbsp;)
            var innerText = doc.DocumentNode.SelectSingleNode("//body")?.InnerText ?? string.Empty;
            innerText = System.Net.WebUtility.HtmlDecode(innerText);

            // 3. Najdeme všechna skutečná slova (využíváme \p{L} pro podporu diakritiky - háčky/čárky)
            var wordRegex = new Regex(@"\b[\p{L}]+\b");
            var matches = wordRegex.Matches(innerText);

            TotalWordCount = matches.Count; // Celkový počet slov VČETNĚ spojek (pro výpočet density)

            // 4. Seznam tzv. Stop Words (slova ignorovaná pro SEO) - základní CZ/EN
            var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "a", "i", "o", "u", "s", "z", "k", "v", "do", "na", "pro", "za", "že", "to", "se", "si", "je", "od", "ze", "ve",
                "the", "of", "and", "in", "to", "is", "for", "on", "with", "as", "by", "at", "it", "this", "that", "are", "or", "be"
            };

            // 5. Filtrace, seskupení a výpočet
            var wordGroups = matches.Select(m => m.Value.ToLowerInvariant())
                                    .Where(w => w.Length > 2 && !stopWords.Contains(w)) // Ignorujeme krátká slova a stop words
                                    .GroupBy(w => w)
                                    .OrderByDescending(g => g.Count())
                                    .Take(15) // Vezmeme TOP 15 nejčastějších slov
                                    .ToList();

            TopKeywords.Clear();
            foreach (var group in wordGroups)
            {
                double density = TotalWordCount > 0 ? (double)group.Count() / TotalWordCount * 100 : 0;
                TopKeywords.Add(new KeywordStat(group.Key, group.Count(), Math.Round(density, 2)));
            }
        }
        private void ClearData()
        {
            PageTitle = MetaDescription = MetaRobots = RobotsTxtContent = CanonicalUrl = HtmlLang =  string.Empty;
            OpenGraphTags.Clear();
            TwitterTags.Clear();
            JsonLdSchemas.Clear();
            HreflangTags.Clear();
            TotalWordCount = 0;
            TopKeywords.Clear();
        }
        private string MakeAbsoluteUrl(Uri baseUri, string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return url;
            if (Uri.TryCreate(baseUri, url, out Uri? absoluteUri))
                return absoluteUri.ToString();
            return url;
        }

    }
    public record KeywordStat(string Word, int Count, double Density);
    public record MetaTagItem(string Key, string Value)
    {
        public bool IsImage => Key.Equals("og:image", StringComparison.OrdinalIgnoreCase) ||
                               Key.Equals("twitter:image", StringComparison.OrdinalIgnoreCase) ||
                               Key.Equals("twitter:image:src", StringComparison.OrdinalIgnoreCase);
    }
}