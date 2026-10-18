using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace RocketLeagueCounter;

public static class UpdateService
{
    private const string VersionUrl =
        "https://raw.githubusercontent.com/BryTop00/RocketLeagueCounter/main/version.json";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "RocketLeagueCounter/1.0");
        client.Timeout = TimeSpan.FromSeconds(20);
        return client;
    }

    public static async Task CheckForUpdatesAsync(Window owner)
    {
        try
        {
            using var response = await Http.GetAsync(VersionUrl);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var json = await JsonDocument.ParseAsync(stream);

            var root = json.RootElement;

            string versionText =
                root.GetProperty("version").GetString() ?? "";

            string downloadUrl =
                root.GetProperty("downloadUrl").GetString() ?? "";

            string sha256 =
                root.GetProperty("sha256").GetString() ?? "";

            if (!Version.TryParse(versionText, out var remoteVersion))
                return;

            var currentVersion =
                Assembly.GetExecutingAssembly().GetName().Version
                ?? new Version(1, 0, 0, 0);

            if (remoteVersion <= currentVersion)
                return;

            if (!Uri.TryCreate(
                    downloadUrl, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps ||
                !string.Equals(
                    uri.Host, "github.com",
                    StringComparison.OrdinalIgnoreCase) ||
                !uri.AbsolutePath.Contains(
                    "/BryTop00/RocketLeagueCounter/releases/download/",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Il link del pacchetto non è valido.");
            }

            if (sha256.Length != 64 ||
                !System.Text.RegularExpressions.Regex.IsMatch(
                    sha256, @"\A[0-9a-fA-F]{64}\z"))
            {
                throw new InvalidDataException(
                    "L'hash SHA-256 non è valido.");
            }

            string? exePath = Environment.ProcessPath;

            if (string.IsNullOrWhiteSpace(exePath) ||
                !File.Exists(exePath))
            {
                throw new InvalidOperationException(
                    "Non riesco a trovare l'eseguibile.");
            }

            string updaterPath = Path.Combine(
                AppContext.BaseDirectory,
                "RocketLeagueUpdater.ps1");

            if (!File.Exists(updaterPath))
            {
                MessageBox.Show(
                    owner,
                    "Manca RocketLeagueUpdater.ps1. " +
                    "Questa versione non può aggiornarsi automaticamente.",
                    "Aggiornamento non disponibile",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                owner,
                $"È disponibile RocketLeagueCounter {remoteVersion}.\n\n" +
                $"Versione attuale: {currentVersion}\n" +
                $"Nuova versione: {remoteVersion}\n\n" +
                "Vuoi aggiornare ora? Il programma verrà chiuso, " +
                "aggiornato e riaperto automaticamente.",
                "Aggiornamento disponibile",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
                return;

            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            start.ArgumentList.Add("-NoProfile");
            start.ArgumentList.Add("-ExecutionPolicy");
            start.ArgumentList.Add("Bypass");
            start.ArgumentList.Add("-File");
            start.ArgumentList.Add(updaterPath);
            start.ArgumentList.Add("-Url");
            start.ArgumentList.Add(downloadUrl);
            start.ArgumentList.Add("-Hash");
            start.ArgumentList.Add(sha256);
            start.ArgumentList.Add("-TargetExe");
            start.ArgumentList.Add(exePath);
            start.ArgumentList.Add("-ProcessId");
            start.ArgumentList.Add(
                Environment.ProcessId.ToString());

            Process.Start(start);

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            // Un problema di rete non deve bloccare il contatore.
            Debug.WriteLine("Controllo aggiornamenti: " + ex.Message);
        }
    }
}