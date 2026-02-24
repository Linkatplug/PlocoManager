using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using Ploco.Data;
using Ploco.Helpers;
using Ploco.Models;

namespace Ploco.Dialogs
{
    public partial class DatabaseManagementWindow : Window, IRefreshableWindow
    {
        private readonly IPlocoRepository _repository;
        private readonly IEnumerable<LocomotiveModel> _locomotives;
        private readonly IEnumerable<TileModel> _tiles;
        private readonly ObservableCollection<TableSummary> _summaries = new();
        private readonly ObservableCollection<HistoryEntry> _history = new();
        private readonly ObservableCollection<LocomotiveStateRow> _locomotiveStates = new();

        public DatabaseManagementWindow(IPlocoRepository repository, IEnumerable<LocomotiveModel> locomotives, IEnumerable<TileModel> tiles)
        {
            InitializeComponent();
            _repository = repository;
            _locomotives = locomotives;
            _tiles = tiles;
            SummaryGrid.ItemsSource = _summaries;
            HistoryGrid.ItemsSource = _history;
            LocomotiveGrid.ItemsSource = _locomotiveStates;
            RefreshData();
        }

        public async void RefreshData()
        {
            await LoadSummaryAsync();
            await LoadHistoryAsync();
            LoadLocomotiveStates();
        }

        private async Task LoadSummaryAsync()
        {
            _summaries.Clear();
            var counts = await _repository.GetTableCountsAsync();
            var trackCounts = await _repository.GetTrackKindCountsAsync();

            AddSummary("Séries", counts.GetValueOrDefault("series"));
            AddSummary("Locomotives", counts.GetValueOrDefault("locomotives"));
            AddSummary("Lieux (tuiles)", counts.GetValueOrDefault("tiles"));
            AddSummary("Voies", counts.GetValueOrDefault("tracks"));
            AddSummary("Assignations", counts.GetValueOrDefault("track_locomotives"));
            AddSummary("Historique", counts.GetValueOrDefault("history"));
            AddSummary("Lieux enregistrés", counts.GetValueOrDefault("places"));

            if (trackCounts.Any())
            {
                foreach (var entry in trackCounts.OrderBy(k => k.Key))
                {
                    AddSummary($"Voies ({entry.Key})", entry.Value);
                }
            }
        }

        private void AddSummary(string name, int count)
        {
            _summaries.Add(new TableSummary
            {
                Name = name,
                Count = count
            });
        }

        private async Task LoadHistoryAsync()
        {
            _history.Clear();
            var historyData = await _repository.LoadHistoryAsync();
            foreach (var entry in historyData)
            {
                _history.Add(entry);
            }
        }

        private void LoadLocomotiveStates()
        {
            _locomotiveStates.Clear();
            foreach (var loco in _locomotives.OrderBy(l => l.Number))
            {
                _locomotiveStates.Add(new LocomotiveStateRow
                {
                    Number = loco.Number,
                    Pool = loco.Pool,
                    Status = loco.Status.ToString(),
                    TractionPercent = loco.TractionPercent?.ToString() ?? string.Empty,
                    HsReason = loco.HsReason ?? string.Empty
                });
            }
        }

        private async void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Voulez-vous vraiment vider l'historique ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _repository.ClearHistoryAsync();
                MessageBox.Show("Nettoyage terminé : historique vidé.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du nettoyage de l'historique : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshData();
        }

        private void ClearTempData_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Voulez-vous vraiment supprimer les données temporaires ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            var deleted = new List<string>();
            try
            {
                var basePath = AppDomain.CurrentDomain.BaseDirectory;
                deleted.AddRange(DeleteIfExists(Path.Combine(basePath, "SwapLog.txt")));
                deleted.AddRange(DeleteIfExists(Path.Combine(basePath, "StatutModificationLog.txt")));

                if (!deleted.Any())
                {
                    MessageBox.Show("Aucune donnée temporaire à supprimer.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Nettoyage terminé : données temporaires supprimées.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du nettoyage des données temporaires : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshData();
        }

        private static IEnumerable<string> DeleteIfExists(string path)
        {
            if (!File.Exists(path))
            {
                return Array.Empty<string>();
            }

            File.Delete(path);
            return new[] { path };
        }

        private async void ResetOperationalState_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Voulez-vous vraiment réinitialiser les données d'état (traction, raisons HS, incidents en ligne) ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                await _repository.ResetOperationalStateAsync();
                foreach (var loco in _locomotives)
                {
                    loco.TractionPercent = null;
                    loco.HsReason = null;
                }
                foreach (var track in _tiles.SelectMany(tile => tile.Tracks))
                {
                    if (track.Kind != TrackKind.Line)
                    {
                        continue;
                    }

                    track.IsOnTrain = false;
                    track.TrainNumber = null;
                    track.StopTime = null;
                    track.IssueReason = null;
                    track.IsLocomotiveHs = false;
                }
                MessageBox.Show("Nettoyage terminé : données d'état réinitialisées.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la réinitialisation : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            RefreshData();
        }

        private sealed class TableSummary
        {
            public string Name { get; set; } = string.Empty;
            public int Count { get; set; }
        }

        private sealed class LocomotiveStateRow
        {
            public int Number { get; set; }
            public string Pool { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public string TractionPercent { get; set; } = string.Empty;
            public string HsReason { get; set; } = string.Empty;
        }
    }
}
