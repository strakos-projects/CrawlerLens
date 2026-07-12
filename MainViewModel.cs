using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HtmlAgilityPack;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
namespace CrawlerLens // Změněn namespace na hlavní (kořenový)
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly HttpClient _httpClient;
        [ObservableProperty] private string _canonicalUrl = string.Empty;
        [ObservableProperty] private int _totalWordCount;
        public ObservableCollection<KeywordStat> TopKeywords { get; } = new();
        public MainViewModel()
        {
            // 1. Nastavíme handler, který automaticky dekomprimuje GZip, Deflate i moderní Brotli
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = System.Net.DecompressionMethods.All
            };

            _httpClient = new HttpClient(handler);

            // 2. Nastavíme User-Agent hlavičku, abychom se tvářili jako legitimní prohlížeč
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