using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Ploco.Models;

namespace Ploco.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        // Données d'état qui étaient auparavant dans le code-behind de MainWindow
        
        [ObservableProperty]
        private ObservableCollection<LocomotiveModel> _locomotives = new();

        [ObservableProperty]
        private ObservableCollection<TileModel> _tiles = new();

        [ObservableProperty]
        private ObservableCollection<LayoutPreset> _layoutPresets = new();

        // Référence au repository
        private readonly Ploco.Data.IPlocoRepository _repository;

        // Événements pour indiquer à la vue qu'elle doit mettre à jour son UI (semi-MVVM temporaire)
        public event Action? OnStateLoaded;
        public event Action? OnStatePersisted;

        public MainViewModel(Ploco.Data.IPlocoRepository repository)
        {
            _repository = repository;
        }

        public void InitializeEvents(Action persistStateCallback, Action loadStateCallback)
        {
            OnStatePersisted = persistStateCallback;
            OnStateLoaded = loadStateCallback;
        }

        // --- Commandes ---

        [RelayCommand]
        public async Task SaveDatabaseAsync()
        {
            OnStatePersisted?.Invoke();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Base de données Ploco (*.db)|*.db|Tous les fichiers (*.*)|*.*",
                FileName = "ploco.db"
            };

            if (dialog.ShowDialog() == true && _repository != null)
            {
                await _repository.CopyDatabaseToAsync(dialog.FileName);
            }
        }

        [RelayCommand]
        public async Task LoadDatabaseAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Base de données Ploco (*.db)|*.db|Tous les fichiers (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true && _repository != null)
            {
                if (!_repository.ReplaceDatabaseWith(dialog.FileName))
                {
                    System.Windows.MessageBox.Show("Le fichier sélectionné n'est pas une base SQLite valide.", "Chargement", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                await _repository.InitializeAsync();
                OnStateLoaded?.Invoke();
            }
        }

        [RelayCommand]
        public void OpenLogsFolder()
        {
            try
            {
                var logsDirectory = Helpers.Logger.LogsDirectory;
                Helpers.Logger.Info($"Opening logs folder: {logsDirectory}", "Menu");
                System.Diagnostics.Process.Start("explorer.exe", logsDirectory);
            }
            catch (Exception ex)
            {
                Helpers.Logger.Error("Failed to open logs folder", ex, "Menu");
                System.Windows.MessageBox.Show($"Impossible d'ouvrir le dossier de logs.\n\nChemin: {Helpers.Logger.LogsDirectory}\n\nErreur: {ex.Message}", 
                    "Erreur", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        public Action? RequestLocomotiveListRefresh { get; set; }

        public void UpdatePoolVisibility()
        {
            foreach (var tile in Tiles)
            {
                foreach (var track in tile.Tracks)
                {
                    var locomotivesToRemove = track.Locomotives
                        .Where(l => !l.IsForecastGhost && !string.Equals(l.Pool, "Sibelit", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    
                    foreach (var loco in locomotivesToRemove)
                    {
                        track.Locomotives.Remove(loco);
                        loco.AssignedTrackId = null;
                        loco.AssignedTrackOffsetX = null;
                        Helpers.Logger.Info($"Removed locomotive {loco.Number} from track {track.Name} (pool changed to {loco.Pool})", "RefreshDisplay");
                    }
                }
            }

            foreach (var loco in Locomotives)
            {
                loco.IsVisibleInActivePool = string.Equals(loco.Pool, "Sibelit", StringComparison.OrdinalIgnoreCase)
                                             && loco.AssignedTrackId == null;
            }

            RequestLocomotiveListRefresh?.Invoke();
        }
    }
}
