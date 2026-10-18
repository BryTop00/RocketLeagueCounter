using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace RocketLeagueCounter
{
    public partial class LobbyRanksView : Window
    {
        public ObservableCollection<LobbyPlayer> BluePlayers { get; }
            = new();

        public ObservableCollection<LobbyPlayer> OrangePlayers { get; }
            = new();

        public LobbyRanksView()
        {
            InitializeComponent();

            DataContext = this;

            LoadExamplePlayers();
            SelectMode("Doubles");
        }

        private void LoadExamplePlayers()
        {
            BluePlayers.Clear();
            OrangePlayers.Clear();

            BluePlayers.Add(new LobbyPlayer
            {
                Name = "BluePlayer1",
                RankText = "Diamante III Div. II",
                MmrText = "1045",
                RankColor = Brushes.DeepSkyBlue
            });

            BluePlayers.Add(new LobbyPlayer
            {
                Name = "BluePlayer2",
                RankText = "Campione I Div. I",
                MmrText = "1210",
                RankColor = Brushes.MediumPurple
            });

            OrangePlayers.Add(new LobbyPlayer
            {
                Name = "OrangePlayer1",
                RankText = "Diamante II Div. III",
                MmrText = "990",
                RankColor = Brushes.DeepSkyBlue
            });

            OrangePlayers.Add(new LobbyPlayer
            {
                Name = "OrangePlayer2",
                RankText = "Campione II Div. I",
                MmrText = "1320",
                RankColor = Brushes.MediumPurple
            });
        }

        private void ModeButton_Click(
            object sender, RoutedEventArgs e)
        {
            if (sender is Button button &&
                button.Tag is string mode)
            {
                SelectMode(mode);
            }
        }

        private void SelectMode(string mode)
        {
            SoloButton.Background = InactiveBrush();
            DoublesButton.Background = InactiveBrush();
            StandardButton.Background = InactiveBrush();
            TournamentButton.Background = InactiveBrush();

            Button selectedButton = mode switch
            {
                "Solo" => SoloButton,
                "Standard" => StandardButton,
                "Tournament" => TournamentButton,
                _ => DoublesButton
            };

            selectedButton.Background = ActiveBrush();

            ModeDescription.Text = mode switch
            {
                "Solo" => "1v1 — Duel",
                "Standard" => "3v3 — Standard",
                "Tournament" => "Tornei competitivi",
                _ => "2v2 — Doubles"
            };
        }

        private static Brush ActiveBrush()
        {
            return new SolidColorBrush(
                Color.FromRgb(39, 125, 69));
        }

        private static Brush InactiveBrush()
        {
            return new SolidColorBrush(
                Color.FromRgb(51, 51, 51));
        }

        private void CloseButton_Click(
            object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void Window_MouseLeftButtonDown(
            object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed &&
                e.OriginalSource is not Button)
            {
                DragMove();
            }
        }
    }

    public class LobbyPlayer
    {
        public string Name { get; set; } = "";
        public string RankText { get; set; } = "";
        public string MmrText { get; set; } = "";
        public Brush RankColor { get; set; } = Brushes.White;
    }
}