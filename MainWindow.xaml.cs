using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace RocketLeagueCounter;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer clockTimer;
    private readonly ObservableCollection<PlayerOption> players = new();

    private TcpClient? tcpClient;
    private CancellationTokenSource? connectionCts;

    private int wins;
    private int losses;
private int currentStreak;
private bool isCompact;
private const double NormalWidth = 260;
private const double NormalHeight = 220;
private const double CompactWidth = 150;
private const double CompactHeight = 85;

    private string currentMatchGuid = "";
    private string countedMatchGuid = "";
    private string selectedPlayerId = "";
    private string lastPlayersSignature = "";
    private string playerTeamName = "";

    private int playerTeam = -1;

    private bool updatingPlayers;
    private bool isClosing;
    private LobbyRanksView? lobbyRanksWindow;

    public MainWindow()
    {
        InitializeComponent();

        PlayerComboBox.ItemsSource = players;

        clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        clockTimer.Tick += UpdateClock;
        clockTimer.Start();

        UpdateClock(null, EventArgs.Empty);

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;

        SetStatus("Seleziona il giocatore", "#F0B84A");
    }

    // OROLOGIO

    private void UpdateClock(object? sender, EventArgs e)
    {
        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
    }

    private async void MainWindow_Loaded(
    object sender, RoutedEventArgs e)
{
    Left = SystemParameters.WorkArea.Right - Width - 20;
    Top = SystemParameters.WorkArea.Top + 20;

    await UpdateService.CheckForUpdatesAsync(this);
}
    // CONNESSIONE

    private async void ConnectButton_Click(
        object sender, RoutedEventArgs e)
    {
        if (connectionCts != null)
        {
            connectionCts.Cancel();

            try
            {
                tcpClient?.Close();
            }
            catch
            {
                // Connessione già chiusa.
            }

            return;
        }

        CancellationTokenSource cts = new();
        connectionCts = cts;

        ConnectButton.Content = "Disconnect";
        ConnectButton.Background =
            new SolidColorBrush(Color.FromRgb(160, 55, 55));

        SetStatus("Connessione in corso...", "#F0B84A");

        try
        {
            await ConnectionLoop(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Disconnessione richiesta.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                "Errore connessione: " + ex);
        }
        finally
        {
            if (ReferenceEquals(connectionCts, cts))
            {
                connectionCts = null;

                try
                {
                    cts.Dispose();
                }
                catch
                {
                }

                tcpClient = null;

                if (!isClosing)
                {
                    ConnectButton.Content = "Connect";
                    ConnectButton.Background =
                        new SolidColorBrush(
                            Color.FromRgb(39, 125, 69));

                    SetStatus("Non connesso", "#FF4D5A");
                }
            }
        }
    }

    private async Task ConnectionLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient? client = null;

            try
            {
                client = new TcpClient();
                tcpClient = client;

                await client.ConnectAsync(
                    "127.0.0.1", 49123, token);

                SetStatus("Connesso, attesa dati...", "#F0B84A");

                using NetworkStream stream = client.GetStream();

                await ReceiveMessages(stream, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Errore TCP: " + ex.Message);

                SetStatus(
                    "Rocket League non raggiungibile",
                    "#FF4D5A");
            }
            catch (ObjectDisposedException)
            {
                if (token.IsCancellationRequested)
                    break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Errore connessione: " + ex.Message);

                SetStatus("Errore di connessione", "#FF4D5A");
            }
            finally
            {
                try
                {
                    client?.Close();
                }
                catch
                {
                }

                if (ReferenceEquals(tcpClient, client))
                    tcpClient = null;
            }

            if (token.IsCancellationRequested)
                break;

            SetStatus("Riconnessione in corso...", "#F0B84A");

            try
            {
                await Task.Delay(2000, token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    // RICEZIONE DATI

    private async Task ReceiveMessages(
        NetworkStream stream, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        StringBuilder pending = new();

        while (!token.IsCancellationRequested)
        {
            int bytesRead = await stream.ReadAsync(
                buffer.AsMemory(0, buffer.Length), token);

            if (bytesRead == 0)
                break;

            pending.Append(
                Encoding.UTF8.GetString(buffer, 0, bytesRead));

            ProcessJsonMessages(pending);
        }
    }

    private void ProcessJsonMessages(StringBuilder pending)
    {
        while (pending.Length > 0)
        {
            string text = pending.ToString();

            int start = text.IndexOf('{');

            if (start < 0)
            {
                pending.Clear();
                return;
            }

            if (start > 0)
            {
                pending.Remove(0, start);
                continue;
            }

            int length = FindJsonObjectLength(text);

            if (length == 0)
                return;

            string json = text.Substring(0, length);
            pending.Remove(0, length);

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(json);

                JsonElement root = document.RootElement;

                if (!TryGetPropertyIgnoreCase(
                    root, "Event", out JsonElement eventElement))
                {
                    continue;
                }

                string eventName = GetStringValue(eventElement);

                if (eventName == "MatchDestroyed")
                {
                    ResetMatchState();
                    continue;
                }

                if (eventName != "UpdateState")
                    continue;

                if (!TryGetPropertyIgnoreCase(
                    root, "Data", out JsonElement dataElement))
                {
                    continue;
                }

                string dataString =
                    dataElement.ValueKind == JsonValueKind.String
                        ? dataElement.GetString() ?? ""
                        : dataElement.GetRawText();

                if (string.IsNullOrWhiteSpace(dataString))
                    continue;

                using JsonDocument dataDocument =
                    JsonDocument.Parse(dataString);

                HandleUpdateState(dataDocument.RootElement);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    "Errore JSON: " + ex.Message);
            }
        }
    }

    // AGGIORNAMENTO PARTITA

    private void HandleUpdateState(JsonElement data)
    {
// Debug disattivato: non salva i dati su file.
        // IDENTIFICAZIONE PARTITA

        if (TryGetPropertyIgnoreCase(
            data, "MatchGuid", out JsonElement guidElement))
        {
            string guid = GetStringValue(guidElement);

            if (!string.IsNullOrWhiteSpace(guid) &&
                guid != currentMatchGuid)
            {
                currentMatchGuid = guid;
                countedMatchGuid = "";
                playerTeam = -1;
                playerTeamName = "";
            }
        }

        // LETTURA GIOCATORI

        if (TryGetPropertyIgnoreCase(
                data, "Players", out JsonElement array) &&
            array.ValueKind == JsonValueKind.Array)
        {
            List<PlayerOption> newPlayers = new();

            foreach (JsonElement player in array.EnumerateArray())
            {
                if (player.ValueKind != JsonValueKind.Object)
                    continue;

                string name = GetStringProperty(player, "Name");

                string primaryId =
                    GetStringProperty(player, "PrimaryId");

                int team = GetIntProperty(player, "TeamNum", -1);

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                string id = !string.IsNullOrWhiteSpace(primaryId)
                    ? primaryId
                    : "NAME:" + name;

                if (newPlayers.Any(p =>
                    string.Equals(
                        p.Id, id, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                newPlayers.Add(new PlayerOption
                {
                    Id = id,
                    Name = name,
                    TeamNum = team,
                    DisplayName = name + " (" +
                        (team == 0 ? "Blu" :
                         team == 1 ? "Arancione" : "Team ?") + ")"
                });
            }

            string signature = string.Join(";",
                newPlayers
                    .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(p => p.Id + ":" +
                        p.DisplayName + ":" + p.TeamNum));

            if (signature != lastPlayersSignature)
            {
                lastPlayersSignature = signature;

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    updatingPlayers = true;

                    try
                    {
                        // Salva l'identificativo selezionato.
                        string idToRestore = selectedPlayerId;

                        players.Clear();

                        foreach (PlayerOption p in newPlayers)
                            players.Add(p);

                        PlayerOption? selected = players.FirstOrDefault(
                            p => string.Equals(
                                p.Id,
                                idToRestore,
                                StringComparison.OrdinalIgnoreCase));

                        PlayerComboBox.SelectedItem = selected;

                        if (selected != null)
                        {
                            playerTeam = selected.TeamNum;

                            SetStatus(
                                "Connesso - " + selected.Name,
                                "#35E06F");
                        }
                        else
                        {
                            playerTeam = -1;

                            SetStatus(
                                string.IsNullOrWhiteSpace(idToRestore)
                                    ? (players.Count == 0
                                        ? "Nessun giocatore ricevuto"
                                        : "Connesso - scegli giocatore")
                                    : "In attesa del giocatore selezionato",
                                "#F0B84A");
                        }
                    }
                    finally
                    {
                        updatingPlayers = false;
                    }
                }));
            }
        }

        // RECUPERO GIOCATORE SELEZIONATO

        PlayerOption? currentPlayer = players.FirstOrDefault(
            p => string.Equals(
                p.Id,
                selectedPlayerId,
                StringComparison.OrdinalIgnoreCase));

        if (currentPlayer != null)
            playerTeam = currentPlayer.TeamNum;

        // SENZA UNA SQUADRA VALIDA NON SI PUÒ CALCOLARE IL RISULTATO.

        if (playerTeam != 0 && playerTeam != 1)
            return;

        if (!TryGetPropertyIgnoreCase(
            data, "Game", out JsonElement game) ||
            game.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        // NOME SQUADRA

        if (TryGetPropertyIgnoreCase(
                game, "Teams", out JsonElement teams) &&
            teams.ValueKind == JsonValueKind.Array &&
            teams.GetArrayLength() > playerTeam)
        {
            playerTeamName = GetStringProperty(
                teams[playerTeam], "Name");
        }

        // VERIFICA FINE PARTITA

        if (!TryGetPropertyIgnoreCase(
                game, "bHasWinner", out JsonElement hasWinner) ||
            hasWinner.ValueKind != JsonValueKind.True)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(currentMatchGuid))
        {
            System.Diagnostics.Debug.WriteLine(
                "Risultato ignorato: MatchGuid assente.");
            return;
        }

        if (countedMatchGuid == currentMatchGuid)
            return;

        if (!TryGetPropertyIgnoreCase(
            game, "Winner", out JsonElement winnerElement))
        {
            return;
        }

        string winnerName = GetStringValue(winnerElement);

        if (string.IsNullOrWhiteSpace(winnerName) ||
            string.IsNullOrWhiteSpace(playerTeamName))
        {
            System.Diagnostics.Debug.WriteLine(
                "Risultato non registrato: vincitore o squadra assente.");
            return;
        }

        bool playerWon = string.Equals(
            winnerName.Trim(),
            playerTeamName.Trim(),
            StringComparison.OrdinalIgnoreCase);

        // REGISTRA UNA SOLA VOLTA IL RISULTATO.

        countedMatchGuid = currentMatchGuid;

        if (playerWon)
            wins++;
        else
            losses++;

        bool streakWon = playerWon;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateCounter();
            UpdateStreak(streakWon);

            SetStatus(
                streakWon
                    ? "VITTORIA registrata"
                    : "SCONFITTA registrata",
                streakWon ? "#35E06F" : "#FF4D5A");
        }));
    }

    // AGGIORNAMENTO STREAK

    private void UpdateStreak(bool won)
    {
        if (won)
        {
            currentStreak = currentStreak > 0
                ? currentStreak + 1
                : 1;
        }
        else
        {
            currentStreak = currentStreak < 0
                ? currentStreak - 1
                : -1;
        }

        StreakText.Text = Math.Abs(currentStreak).ToString();

        StreakText.Foreground = new SolidColorBrush(
            currentStreak > 0
                ? Color.FromRgb(53, 224, 111)
                : Color.FromRgb(255, 77, 90));
    }

    // SELEZIONE GIOCATORE

    private void PlayerComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (updatingPlayers)
            return;

        if (PlayerComboBox.SelectedItem is PlayerOption player)
        {
            selectedPlayerId = player.Id;
            playerTeam = player.TeamNum;
            playerTeamName = "";

            SetStatus(
                "Giocatore selezionato: " + player.Name,
                "#35E06F");
        }
    }

    // RESET STATO PARTITA

    private void ResetMatchState()
    {
        currentMatchGuid = "";
        countedMatchGuid = "";
        playerTeam = -1;
        playerTeamName = "";
        lastPlayersSignature = "";

        Dispatcher.BeginInvoke(new Action(() =>
        {
            updatingPlayers = true;

            try
            {
                players.Clear();

                // Conserva selectedPlayerId per la partita successiva.
                PlayerComboBox.SelectedItem = null;
            }
            finally
            {
                updatingPlayers = false;
            }

            SetStatus(
                string.IsNullOrWhiteSpace(selectedPlayerId)
                    ? "Seleziona il giocatore"
                    : "In attesa della prossima partita",
                "#F0B84A");
        }));
    }

    // CONTATORE

    private void UpdateCounter()
    {
        WinsText.Text = $"W {wins}";
        LossesText.Text = $"L {losses}";
    }
private void ResetCounterButton_Click(
    object sender, RoutedEventArgs e)
{
    wins = 0;
    losses = 0;
    currentStreak = 0;

    UpdateCounter();

    StreakText.Text = "0";
    StreakText.Foreground = new SolidColorBrush(
        Color.FromRgb(136, 136, 136));

    SetStatus("Contatore azzerato", "#F0B84A");
}
    // MODIFICA MANUALE DEL CONTATORE

     private void AddWinButton_Click(
    object sender, RoutedEventArgs e)
{
    wins++;
    UpdateCounter();
    UpdateStreak(true);
    SetStatus("Vittoria aggiunta manualmente", "#35E06F");
}

private void RemoveWinButton_Click(
    object sender, RoutedEventArgs e)
{
    if (wins > 0)
    {
        wins--;
        UpdateCounter();
        UpdateStreak(false);
        SetStatus("Vittoria rimossa manualmente", "#F0B84A");
    }
}

private void AddLossButton_Click(
    object sender, RoutedEventArgs e)
{
    losses++;
    UpdateCounter();
    UpdateStreak(false);
    SetStatus("Sconfitta aggiunta manualmente", "#FF4D5A");
}

private void RemoveLossButton_Click(
    object sender, RoutedEventArgs e)
{
    if (losses > 0)
    {
        losses--;
        UpdateCounter();
        UpdateStreak(true);
        SetStatus("Sconfitta rimossa manualmente", "#F0B84A");
    }
} 
  // STATO VISIVO

    private void SetStatus(string message, string color)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() => SetStatus(message, color)));
            return;
        }

        StatusText.Text = message;

        StatusDot.Fill = (Brush)new BrushConverter()
            .ConvertFromString(color)!;
    }

    // LETTURA SICURA PROPRIETÀ JSON

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (string.Equals(
                    property.Name,
                    propertyName,
                    StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string GetStringValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.GetRawText(),
            _ => ""
        };
    }

    private static string GetStringProperty(
        JsonElement element, string propertyName)
    {
        return TryGetPropertyIgnoreCase(
            element, propertyName, out JsonElement value)
                ? GetStringValue(value)
                : "";
    }

    private static int GetIntProperty(
        JsonElement element,
        string propertyName,
        int defaultValue)
    {
        if (!TryGetPropertyIgnoreCase(
            element, propertyName, out JsonElement value))
        {
            return defaultValue;
        }

        if (value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out int result))
        {
            return result;
        }

        if (value.ValueKind == JsonValueKind.String &&
            int.TryParse(value.GetString(), out result))
        {
            return result;
        }

        return defaultValue;
    }

    // LETTURA JSON IN STREAM

    private int FindJsonObjectLength(string text)
    {
        int depth = 0;
        bool insideString = false;
        bool escaped = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (insideString)
            {
                if (escaped)
                    escaped = false;
                else if (c == '\\')
                    escaped = true;
                else if (c == '"')
                    insideString = false;

                continue;
            }

            if (c == '"')
                insideString = true;
            else if (c == '{')
                depth++;
            else if (c == '}')
            {
                depth--;

                if (depth == 0)
                    return i + 1;
            }
        }

        return 0;
    }

    // SPOSTAMENTO FINESTRA

    private void Window_MouseLeftButtonDown(
        object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed &&
            e.OriginalSource is not Button &&
            e.OriginalSource is not ComboBox &&
            e.OriginalSource is not ComboBoxItem)
        {
            DragMove();
        }
    }

    // CHIUSURA

    // APERTURA FINESTRA RANK LOBBY

    private void OpenLobbyRanksButton_Click(
        object sender, RoutedEventArgs e)
    {
        if (lobbyRanksWindow == null ||
            !lobbyRanksWindow.IsVisible)
        {
            lobbyRanksWindow = new LobbyRanksView();

            lobbyRanksWindow.Closed += (s, args) =>
            {
                lobbyRanksWindow = null;
            };

            lobbyRanksWindow.Show();
        }
        else
        {
            lobbyRanksWindow.Activate();
        }
    }
private void CompactButton_Click(
    object sender, RoutedEventArgs e)
{
    isCompact = !isCompact;

    if (isCompact)
    {
        // Nasconde gli elementi non necessari.
        AuthorText.Visibility = Visibility.Collapsed;
        PlayerPanel.Visibility = Visibility.Collapsed;
        StatusPanel.Visibility = Visibility.Collapsed;
        ConnectionPanel.Visibility = Visibility.Collapsed;

        AddWinButton.Visibility = Visibility.Collapsed;
        RemoveWinButton.Visibility = Visibility.Collapsed;
        ResetButton.Visibility = Visibility.Collapsed;
        AddLossButton.Visibility = Visibility.Collapsed;
        RemoveLossButton.Visibility = Visibility.Collapsed;

        // Compatta il contatore.
        CounterPanel.Margin = new Thickness(0);
        WinsText.FontSize = 21;
        LossesText.FontSize = 21;

        Width = CompactWidth;
        Height = CompactHeight;

        CompactButton.Content = "↗";
        CompactButton.ToolTip = "Ripristina finestra";
    }
    else
    {
        // Ripristina la schermata completa.
        AuthorText.Visibility = Visibility.Visible;
        PlayerPanel.Visibility = Visibility.Visible;
        StatusPanel.Visibility = Visibility.Visible;
        ConnectionPanel.Visibility = Visibility.Visible;

        AddWinButton.Visibility = Visibility.Visible;
        RemoveWinButton.Visibility = Visibility.Visible;
        ResetButton.Visibility = Visibility.Visible;
        AddLossButton.Visibility = Visibility.Visible;
        RemoveLossButton.Visibility = Visibility.Visible;

        CounterPanel.Margin = new Thickness(0, 0, 0, 7);
        WinsText.FontSize = 25;
        LossesText.FontSize = 25;

        Width = NormalWidth;
        Height = NormalHeight;

        CompactButton.Content = "−";
        CompactButton.ToolTip = "Riduci finestra";
    }
}
    private void CloseButton_Click(
        object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        isClosing = true;

        try
        {
            connectionCts?.Cancel();
            tcpClient?.Close();
            clockTimer.Stop();
        }
        catch
        {
        }
    }

    // MODELLO GIOCATORE

    public class PlayerOption
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public int TeamNum { get; set; }
    }
}