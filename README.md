# CrawlerLens 👁️🔍

**CrawlerLens** is a modern Windows desktop application built with C# and .NET 8 that instantly analyzes how search engines and social media crawlers see a website.

With a beautiful Windows 11 Fluent Design interface, it extracts and visualizes core SEO metadata, Open Graph tags, Twitter Cards, Schema.org structured data, and `robots.txt` configuration from any given URL.

![CrawlerLens Screenshot](screenshot.png)

## ✨ Features

- **Core SEO Metadata:** Instantly fetch Page Title, Meta Description, and Meta Robots.
- **Social Media Cards:** Parse and read all Open Graph (`og:*`) and Twitter (`twitter:*`) meta tags to see how the link previews on social platforms.
- **JSON-LD / Schema.org:** Automatically format and display hidden structured data scripts.
- **Robots.txt parsing:** Quickly check the site's crawling rules.
- **Supermodern UX:** Built with `WPF-UI` to provide a native Windows 11 experience, complete with Mica backdrop effects, smooth animations, and a dark mode interface.

## 🛠️ Tech Stack

- **Framework:** [.NET 8.0](https://dotnet.microsoft.com/)
- **Architecture:** WPF + MVVM
- **UI Library:** [WPF-UI (lepoco)](https://github.com/lepoco/wpfui) for Fluent Design
- **HTML Parsing:** [HtmlAgilityPack](https://html-agility-pack.net/) for robust, XPath-based DOM querying
- **State Management:** `CommunityToolkit.Mvvm`

## 🚀 Getting Started

### Prerequisites

- Visual Studio 2022 (or newer)
- .NET 8.0 SDK
