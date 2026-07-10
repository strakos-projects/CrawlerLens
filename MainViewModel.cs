using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HtmlAgilityPack;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace CrawlerLens // Změněn namespace na hlavní (kořenový)
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly HttpClient _httpClient;

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

                // 3. Parse Open Graph (og:*)
                var ogNodes = doc.DocumentNode.SelectNodes("//meta[starts-with(@property, 'og:')]");
                if (ogNodes != null)
                {
                    foreach (var node in ogNodes)
                    {
                        var prop = node.GetAttributeValue("property", string.Empty) ?? string.Empty;
                        var content = node.GetAttributeValue("content", string.Empty) ?? string.Empty;
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
        private void ClearData()
        {
            PageTitle = MetaDescription = MetaRobots = RobotsTxtContent = string.Empty;
            OpenGraphTags.Clear();
            TwitterTags.Clear();
            JsonLdSchemas.Clear();
        }
    }

    public record MetaTagItem(string Key, string Value);
}