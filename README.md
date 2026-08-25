# Teachable Course Downloader

A dedicated Windows WPF application designed to download and locally archive video lectures from Teachable-based platforms (with specialized support for Hotmart player integration, such as Metigator courses).

## ⚠️ Disclaimer
**This tool is for PERSONAL EDUCATIONAL USE ONLY.**
This application was created strictly to allow users to download and archive courses they have legally purchased for offline viewing. 
I do **NOT** condone, support, or accept the use of this tool for downloading and redistributing paid or private courses. Please respect course creators and their intellectual property.

## Features
- **Auto-Scraping & Navigation:** The embedded Chromium browser automatically detects the active video, extracts the current module and lecture name, and clicks the "Complete and Continue" button to seamlessly jump to the next video.
- **Smart Categorization:** Automatically organizes downloaded videos into local folders based on the course's exact Module names.
- **Resilient Retry & Timeout System:** Intelligently handles non-video text/PDF lectures by automatically skipping them after a timeout. Infinitely retries downloads if your internet connection drops.
- **Hotmart DRM Bypass:** Silently intercepts `.m3u8` video streams from the network tab and automatically forwards the correct Authentication, Origin, and Referer headers to `yt-dlp`.
- **Modern Fluent UI:** Built using `WPF-UI` to provide a beautiful, native Windows 11 Fluent interface with Mica backdrops.

## How It Works
The application uses Microsoft's `WebView2` to provide an embedded browser where you can log in to your course platform. It then intercepts web traffic to capture hidden `.m3u8` video streams. 

When a video is found:
1. It injects a script to extract the Lecture and Module name.
2. It spins up an embedded version of `yt-dlp` in the background, passing your session headers to bypass 403 Forbidden errors.
3. Once downloaded, it executes a JavaScript script to click the "Next" button and repeats the process.

## Advanced Headers (403 Forbidden Fix)
By default, the application is configured to bypass DRM for the **Hotmart** player. If you are downloading a course from a different provider and receive a `403 Forbidden` error, you can customize the **Origin** and **Referer** headers:
1. Open your course in the embedded browser or a normal Chrome window.
2. Press `F12` to open Developer Tools and go to the **Network** tab.
3. Play the video and filter by `m3u8`.
4. Click on the `m3u8` request and scroll down to **Request Headers**.
5. Copy the exact `Origin` and `Referer` values and paste them into the application's "Advanced Settings" expander before starting the auto-download.

## Requirements
- Windows OS
- .NET 10 Runtime (or rely on the self-contained executable)

## Usage
1. Open the application.
2. Navigate to your course in the embedded browser tab.
3. Log in to your account.
4. Click **"Start Auto-Download"**.
5. The application will take over, watching videos, downloading them, and advancing to the next lesson automatically.

## Built With
- [WPF (Windows Presentation Foundation)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/)
- [WPF-UI (Fluent Design System)](https://wpfui.lepo.co/)
- [WebView2](https://learn.microsoft.com/en-us/microsoft-edge/webview2/)
- [yt-dlp](https://github.com/yt-dlp/yt-dlp)
