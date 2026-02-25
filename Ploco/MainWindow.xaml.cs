using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using Ploco.Data;
using Ploco.Dialogs;
using Ploco.Helpers;
using Ploco.Models;

namespace Ploco
{
    public partial class MainWindow : Window
    {
        public static readonly RoutedUICommand LocomotiveHsCommand = new("Loc HS", "LocomotiveHsCommand", typeof(MainWindow));
        private readonly IPlocoRepository _repository;
        private readonly ViewModels.MainViewModel _viewModel;
        
        private const string LayoutPresetFileName = "layout_presets.json";
        private const double MinTileWidth = 260;
        private const double MinTileHeight = 180;
        private const double CanvasPadding = 80;
        private const int RollingLineStartNumber = 1101;
        private const int DefaultRollingLineCount = 23;
        private bool _isDarkMode;
        private TileModel? _draggedTile;
        private Point _tileDragStart;
        private bool _isResizingTile;
        private readonly Dictionary<Type, Window> _modelessWindows = new();

        public MainWindow()
        {
            InitializeComponent();
            _viewModel = App.ServiceProvider.GetRequiredService<ViewModels.MainViewModel>();
            DataContext = _viewModel;
            
            _repository = App.ServiceProvider.GetRequiredService<IPlocoRepository>();
            _viewModel.InitializeEvents(PersistState, async () => await LoadStateAsync());
            _viewModel.RequestLocomotiveListRefresh += RefreshLocomotivesDisplay;
            _viewModel.OnWorkspaceChanged += RefreshTapisT13;
            
            InputBindings.Add(new KeyBinding(LocomotiveHsCommand, new KeyGesture(Key.H, ModifierKeys.Control)));
            CommandBindings.Add(new CommandBinding(LocomotiveHsCommand, LocomotiveHsCommand_Executed, LocomotiveHsCommand_CanExecute));
            
            // Initialize logging system
            Logger.Initialize();
            Logger.Info("Application starting", "Application");
        }

        public async Task InitializeAppAsync(SplashWindow splash)
        {
            Logger.Info("Initializing application from Splash Screen", "Application");
            
            // Lancement du VRAI chargement en arrière-plan sans bloquer la façade
            var backgroundLoadTask = Task.Run(async () =>
            {
                await _repository.InitializeAsync();
                await _repository.SeedDefaultDataIfNeededAsync();

                await Application.Current.Dispatcher.InvokeAsync(async () => 
                {
                    await LoadStateAsync();
                });
                
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    LoadLayoutPresets();
                    RefreshPresetMenu();
                    ApplyTheme(false);
                });
            });

            // Tableau des phrases fictives (Easter Eggs)
            var easterEggs = new[]
            {
                "Remplissage des sablières",
                "Graissage des boudins",
                "Chargement des lanternes",
                "Réception d'un appel du SUD de MUN",
                "VAP du cœur du programme",
                "Formule magique anti-DDS",
                "Enclenchement du DJ",
                "... Appel du SUD du MUN ..."
            };

            // Boucle d'allumage fictif
            int stepLength = 100 / easterEggs.Length;
            for (int i = 0; i < easterEggs.Length; i++)
            {
                int currentPercent = (i + 1) * stepLength;
                splash.UpdateProgress(currentPercent, easterEggs[i] + "...");
                
                // Attente stricte de 1 seconde (1000ms) par phrase
                await Task.Delay(1000); 
            }

            // Attente de sécurité si le vrai chargement derrière était plus long que prévu
            await backgroundLoadTask;
            
            splash.UpdateProgress(100, "100%");
            await Task.Delay(200);
            
            Logger.Info($"Loaded {_viewModel.Locomotives.Count} locomotives and {_viewModel.Tiles.Count} tiles", "Application");
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Logger.Info("Main window loaded", "Application");
            
            WindowSettingsHelper.RestoreWindowSettings(this, "MainWindow");
            
            LocomotiveList.ItemsSource = _viewModel.Locomotives;
            TileCanvas.ItemsSource = _viewModel.Tiles;
            InitializeLocomotiveView();
            UpdateTileCanvasExtent();
        }

        private void TileScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Do not call UpdateTileCanvasExtent() here to prevent infinite layout cycles
            // resulting from ScrollBar toggles adjusting Viewport Width/Height.
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var result = MessageBox.Show("Êtes-vous sûr de vouloir quitter ?", "Quitter", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                Logger.Info("Application close cancelled by user", "Application");
                return;
            }

            Logger.Info("Saving state before closing", "Application");
            PersistState();
            
            // Save window settings
            WindowSettingsHelper.SaveWindowSettings(this, "MainWindow");
            
            Logger.Shutdown();
        }

        private async Task LoadStateAsync()
        {
            _viewModel.Locomotives.Clear();
            _viewModel.Tiles.Clear();

            var state = await _repository.LoadStateAsync();
            foreach (var loco in state.Locomotives.OrderBy(l => l.SeriesName).ThenBy(l => l.Number))
            {
                if (!loco.IsForecastGhost)
                {
                    _viewModel.Locomotives.Add(loco);
                }
            }

            foreach (var tile in state.Tiles)
            {
                _viewModel.EnsureDefaultTracks(tile);
                foreach (var track in tile.Tracks)
                {
                    EnsureTrackOffsets(track);
                }
                _viewModel.Tiles.Add(tile);
            }

            // Restore Ghost links
            var realLocos = _viewModel.Locomotives.ToList();
            var ghosts = state.Locomotives.Where(l => l.IsForecastGhost).ToList();
            
            foreach (var ghost in ghosts)
            {
                var origin = realLocos.FirstOrDefault(l => l.Number == ghost.Number && l.IsForecastOrigin);
                if (origin != null)
                {
                    ghost.ForecastSourceLocomotiveId = origin.Id;
                    origin.ForecastTargetRollingLineTrackId = ghost.AssignedTrackId;
                }
            }

            _viewModel.UpdatePoolVisibility();
            UpdateTileCanvasExtent();
        }

        private void PersistState()
        {
            // Collect all locomotives including ghosts
            var locomotivesToSave = _viewModel.Locomotives.ToList();
            var ghosts = _viewModel.Tiles.SelectMany(t => t.Tracks)
                                         .SelectMany(t => t.Locomotives)
                                         .Where(l => l.IsForecastGhost)
                                         .ToList();
            locomotivesToSave.AddRange(ghosts);
            
            // Create a clean copy of tiles without ghosts
            var tilesToSave = new List<TileModel>();
            foreach (var tile in _viewModel.Tiles)
            {
                var tileCopy = new TileModel
                {
                    Id = tile.Id,
                    Type = tile.Type,
                    Name = tile.Name,
                    X = tile.X,
                    Y = tile.Y,
                    Width = tile.Width,
                    Height = tile.Height,
                    LocationPreset = tile.LocationPreset,
                    GarageTrackNumber = tile.GarageTrackNumber,
                    RollingLineCount = tile.RollingLineCount
                };

                foreach (var track in tile.Tracks)
                {
                    var trackCopy = new TrackModel
                    {
                        Id = track.Id,
                        TileId = track.TileId,
                        Position = track.Position,
                        Kind = track.Kind,
                        Name = track.Name,
                        IsOnTrain = track.IsOnTrain,
                        StopTime = track.StopTime,
                        IssueReason = track.IssueReason,
                        IsLocomotiveHs = track.IsLocomotiveHs,
                        LeftLabel = track.LeftLabel,
                        RightLabel = track.RightLabel,
                        IsLeftBlocked = track.IsLeftBlocked,
                        IsRightBlocked = track.IsRightBlocked,
                        TrainNumber = track.TrainNumber
                    };

                    // Add all locomotives (including ghosts) to the track
                    foreach (var loco in track.Locomotives)
                    {
                        trackCopy.Locomotives.Add(loco);
                    }

                    tileCopy.Tracks.Add(trackCopy);
                }

                tileCopy.RefreshTrackCollections();
                tilesToSave.Add(tileCopy);
            }

            var state = new AppState
            {
                Series = BuildSeriesState(),
                Locomotives = locomotivesToSave,
                Tiles = tilesToSave
            };
            // PersistState est synchrone historiquement (événement de fermeture/autre), on le lance sans attendre si on ne peut pas l'attendre.
            _ = _repository.SaveStateAsync(state);
        }

        private void InitializeLocomotiveView()
        {
            var view = CollectionViewSource.GetDefaultView(_viewModel.Locomotives);
            view.SortDescriptions.Clear();
            view.SortDescriptions.Add(new System.ComponentModel.SortDescription(nameof(LocomotiveModel.Number), System.ComponentModel.ListSortDirection.Ascending));
        }

        private List<RollingStockSeries> BuildSeriesState()
        {
            var series = new Dictionary<int, RollingStockSeries>();
            foreach (var loco in _viewModel.Locomotives)
            {
                if (!series.ContainsKey(loco.SeriesId))
                {
                    series[loco.SeriesId] = new RollingStockSeries
                    {
                        Id = loco.SeriesId,
                        Name = loco.SeriesName,
                        StartNumber = loco.Number,
                        EndNumber = loco.Number
                    };
                }
                var item = series[loco.SeriesId];
                item.StartNumber = Math.Min(item.StartNumber, loco.Number);
                item.EndNumber = Math.Max(item.EndNumber, loco.Number);
            }

            return series.Values.ToList();
        }

        private async void ToggleLeftBlocked_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TrackModel track)
            {
                track.IsLeftBlocked = !track.IsLeftBlocked;
                await _repository.AddHistoryAsync("ZoneBlockedUpdated", $"Mise à jour du remplissage BLOCK pour {track.Name}.");
                PersistState();
            }
        }

        private async void ToggleRightBlocked_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is TrackModel track)
            {
                track.IsRightBlocked = !track.IsRightBlocked;
                await _repository.AddHistoryAsync("ZoneBlockedUpdated", $"Mise à jour du remplissage BIF pour {track.Name}.");
                PersistState();
            }
        }

        private async Task RemoveLocomotiveFromTrackAsync(LocomotiveModel loco)
        {
            var track = _viewModel.Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Locomotives.Contains(loco));
            if (track != null)
            {
                track.Locomotives.Remove(loco);
                loco.AssignedTrackId = null;
                loco.AssignedTrackOffsetX = null;
                await _repository.AddHistoryAsync("LocomotiveRemoved", $"Loco {loco.Number} retirée de {track.Name}.");
            }
        }

        private static void EnsureTrackOffsets(TrackModel track)
        {
            PlacementLogicHelper.EnsureTrackOffsets(track);
        }

        private void MenuItem_ModifierStatut_Click(object sender, RoutedEventArgs e)
        {
            var loco = GetLocomotiveFromMenuItem(sender);
            if (loco != null)
            {
                HandleLocomotiveStatusChange(loco);
            }
        }

        private async void HandleLocomotiveStatusChange(LocomotiveModel loco)
        {
            var oldStatus = loco.Status;
            Logger.Debug($"Opening status dialog for loco {loco.Number} (current status: {oldStatus})", "Status");
            
            var dialog = new StatusDialog(loco) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                Logger.Info($"Status changed for loco {loco.Number}: {oldStatus} -> {loco.Status}", "Status");
                await _repository.AddHistoryAsync("StatusChanged", $"Statut modifié pour {loco.Number}.");
                PersistState();
                RefreshTapisT13();
            }
            else
            {
                Logger.Debug($"Status change cancelled for loco {loco.Number}", "Status");
            }
        }

        private void MenuItem_SwapPool_Click(object sender, RoutedEventArgs e)
        {
            var loco = GetLocomotiveFromMenuItem(sender);
            if (loco != null)
            {
                OpenSwapDialog(loco);
            }
        }

        private void MenuItem_LocHs_Click(object sender, RoutedEventArgs e)
        {
            var loco = GetLocomotiveFromMenuItem(sender);
            if (loco != null)
            {
                MarkLocomotiveHs(loco);
            }
        }

        private async void MenuItem_RemoveFromTile_Click(object sender, RoutedEventArgs e)
        {
            var loco = GetLocomotiveFromMenuItem(sender);
            if (loco != null)
            {
                await RemoveLocomotiveFromTrackAsync(loco);
                _viewModel.UpdatePoolVisibility();
                PersistState();
                RefreshTapisT13();
            }
        }

        private void LocomotiveTileContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu contextMenu)
            {
                return;
            }

            // Get the locomotive from the context menu's placement target
            LocomotiveModel? loco = null;
            if (contextMenu.PlacementTarget is FrameworkElement element && element.DataContext is LocomotiveModel l)
            {
                loco = l;
            }
            else if (contextMenu.DataContext is LocomotiveModel l2)
            {
                loco = l2;
            }

            if (loco == null)
            {
                return;
            }

            // Log context menu opened on this locomotive
            Logger.Debug($"Opened context menu on loco Id={loco.Id} Number={loco.Number} IsForecastOrigin={loco.IsForecastOrigin} IsForecastGhost={loco.IsForecastGhost}", "ContextMenu");

            // Find menu items by name - we need to search through the Items collection
            MenuItem? placementItem = null;
            MenuItem? annulerItem = null;
            MenuItem? validerItem = null;

            foreach (var item in contextMenu.Items)
            {
                if (item is MenuItem menuItem)
                {
                    if (menuItem.Header?.ToString() == "Placement prévisionnel")
                        placementItem = menuItem;
                    else if (menuItem.Header?.ToString() == "Annuler le placement prévisionnel")
                        annulerItem = menuItem;
                    else if (menuItem.Header?.ToString() == "Valider le placement prévisionnel")
                        validerItem = menuItem;
                }
            }

            // Show/hide based on forecast state
            if (placementItem != null)
            {
                placementItem.Visibility = loco.IsForecastOrigin ? Visibility.Collapsed : Visibility.Visible;
            }

            if (annulerItem != null)
            {
                annulerItem.Visibility = loco.IsForecastOrigin ? Visibility.Visible : Visibility.Collapsed;
            }

            if (validerItem != null)
            {
                validerItem.Visibility = loco.IsForecastOrigin ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void OpenSwapDialog(LocomotiveModel loco)
        {
            if (!string.Equals(loco.Pool, "Sibelit", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Le swap est réservé aux locomotives de la pool Sibelit.", "Swap", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var lineasCandidates = _viewModel.Locomotives
                .Where(item => LocomotiveStateHelper.IsEligibleForSwap(loco, item))
                .OrderBy(item => item.Number)
                .ToList();
            if (!lineasCandidates.Any())
            {
                MessageBox.Show("Aucune locomotive Lineas disponible (ou éligible) pour le swap.", "Swap", MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var dialog = new SwapDialog(loco, new ObservableCollection<LocomotiveModel>(lineasCandidates))
            {
                Owner = this
            };
            if (dialog.ShowDialog() == true && dialog.SelectedLoco != null)
            {
                ApplySwap(loco, dialog.SelectedLoco);
            }
        }

        private void ApplySwap(LocomotiveModel sibelitLoco, LocomotiveModel lineasLoco)
        {
            var track = _viewModel.Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Locomotives.Contains(sibelitLoco));
            var trackIndex = track?.Locomotives.IndexOf(sibelitLoco) ?? -1;
            var trackOffset = sibelitLoco.AssignedTrackOffsetX;

            if (track != null)
            {
                track.Locomotives.Remove(sibelitLoco);
            }

            sibelitLoco.AssignedTrackId = null;
            sibelitLoco.AssignedTrackOffsetX = null;
            sibelitLoco.Pool = "Lineas";

            lineasLoco.Pool = "Sibelit";
            if (track != null)
            {
                if (trackIndex >= 0 && trackIndex <= track.Locomotives.Count)
                {
                    track.Locomotives.Insert(trackIndex, lineasLoco);
                }
                else
                {
                    track.Locomotives.Add(lineasLoco);
                }
                lineasLoco.AssignedTrackId = track.Id;
                lineasLoco.AssignedTrackOffsetX = trackOffset;
                EnsureTrackOffsets(track);
            }
            else
            {
                lineasLoco.AssignedTrackId = null;
                lineasLoco.AssignedTrackOffsetX = null;
            }

            _ = _repository.AddHistoryAsync("LocomotiveSwapped",
                $"Swap Sibelit {sibelitLoco.Number} ↔ Lineas {lineasLoco.Number}.");
            _viewModel.UpdatePoolVisibility();
            PersistState();
            RefreshTapisT13();
        }

        private static LocomotiveModel? GetLocomotiveFromMenuItem(object sender)
        {
            if (sender is not MenuItem menuItem)
            {
                return null;
            }

            // PRIORITY 1: Get from ContextMenu's DataContext or PlacementTarget
            // LocomotiveItem_PreviewMouseRightButtonDown sets these correctly
            var contextMenu = menuItem.Parent as ContextMenu
                ?? ItemsControl.ItemsControlFromItemContainer(menuItem) as ContextMenu;
            
            if (contextMenu != null)
            {
                // First try ContextMenu's DataContext (set by PreviewMouseRightButtonDown)
                if (contextMenu.DataContext is LocomotiveModel contextLoco)
                {
                    return contextLoco;
                }
                
                // Then try PlacementTarget's DataContext
                if (contextMenu.PlacementTarget is FrameworkElement element && element.DataContext is LocomotiveModel placementLoco)
                {
                    return placementLoco;
                }
            }

            // PRIORITY 2: CommandParameter (if explicitly set)
            if (menuItem.CommandParameter is LocomotiveModel parameter)
            {
                return parameter;
            }

            // PRIORITY 3: MenuItem's own DataContext (can be ambiguous, use as last resort)
            if (menuItem.DataContext is LocomotiveModel dataContext)
            {
                return dataContext;
            }

            return null;
        }

        private async void MarkLocomotiveHs(LocomotiveModel loco)
        {
            var dialog = new StatusDialog(loco, LocomotiveStatus.HS) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                await _repository.AddHistoryAsync("StatusChanged", $"Statut modifié pour {loco.Number} (HS).");
                PersistState();
                RefreshTapisT13();
            }
        }

        private void LocomotiveHsCommand_Executed(object sender, ExecutedRoutedEventArgs e)
        {
            var loco = GetFocusedLocomotive();
            if (loco != null)
            {
                MarkLocomotiveHs(loco);
            }
        }

        private void LocomotiveHsCommand_CanExecute(object sender, CanExecuteRoutedEventArgs e)
        {
            e.CanExecute = GetFocusedLocomotive() != null;
        }

        private LocomotiveModel? GetFocusedLocomotive()
        {
            if (Keyboard.FocusedElement is DependencyObject element)
            {
                var loco = GetLocomotiveFromElement(element);
                if (loco != null)
                {
                    return loco;
                }
            }

            return LocomotiveList.SelectedItem as LocomotiveModel;
        }

        private static LocomotiveModel? GetLocomotiveFromElement(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is FrameworkElement frameworkElement && frameworkElement.DataContext is LocomotiveModel loco)
                {
                    return loco;
                }

                element = VisualTreeHelper.GetParent(element);
            }

            return null;
        }

        private void Tile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject source
                && (FindAncestor<Button>(source) != null
                    || FindAncestor<MenuItem>(source) != null
                    || FindAncestor<Menu>(source) != null
                    || FindAncestor<Thumb>(source) != null))
            {
                return;
            }

            if (_isResizingTile)
            {
                return;
            }

            if (sender is Border border && border.DataContext is TileModel tile)
            {
                _draggedTile = tile;
                _tileDragStart = GetDropPositionInWorkspace(e);
                border.CaptureMouse();
            }
        }

        private void Tile_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggedTile == null || e.LeftButton != MouseButtonState.Pressed || _isResizingTile)
            {
                return;
            }

            var currentPosition = GetDropPositionInWorkspace(e);
            var offset = currentPosition - _tileDragStart;
            _draggedTile.X = Math.Max(0, _draggedTile.X + offset.X);
            _draggedTile.Y = Math.Max(0, _draggedTile.Y + offset.Y);
            _tileDragStart = currentPosition;
            UpdateTileCanvasExtent();
        }

        private async void Tile_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggedTile != null)
            {
                ResolveTileOverlap(_draggedTile);
                await _repository.AddHistoryAsync("TileMoved", $"Tuile {_draggedTile.Name} déplacée.");
                PersistState();
                UpdateTileCanvasExtent();
            }
            if (sender is Border border)
            {
                border.ReleaseMouseCapture();
            }
            _draggedTile = null;
        }

        private void TileResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb && thumb.DataContext is TileModel tile)
            {
                _isResizingTile = true;
                tile.Width = Math.Max(MinTileWidth, tile.Width + e.HorizontalChange);
                tile.Height = Math.Max(MinTileHeight, tile.Height + e.VerticalChange);
                UpdateTileCanvasExtent();
            }
        }

        private async void TileResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isResizingTile = false;
            if (sender is Thumb thumb && thumb.DataContext is TileModel tile)
            {
                ResolveTileOverlap(tile);
                await _repository.AddHistoryAsync("TileResized", $"Tuile {tile.Name} redimensionnée.");
                PersistState();
                UpdateTileCanvasExtent();
            }
        }

        private static T? FindAncestor<T>(DependencyObject child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T match)
                {
                    return match;
                }
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }





        private void MenuItem_PoolManagement_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new PoolTransferWindow(_viewModel.Locomotives)
            {
                Owner = this
            };
            dialog.ShowDialog();
            _viewModel.UpdatePoolVisibility();
            PersistState();
            RefreshTapisT13();
        }

        private async void MenuItem_History_Click(object sender, RoutedEventArgs e)
        {
            var history = await _repository.LoadHistoryAsync();
            var dialog = new HistoriqueWindow(history) { Owner = this };
            dialog.ShowDialog();
        }

        private void MenuItem_TapisT13_Click(object sender, RoutedEventArgs e)
        {
            OpenModelessWindow(() => new TapisT13Window(_viewModel.Locomotives, _viewModel.Tiles));
        }

        private void MenuItem_PlanningPdf_Click(object sender, RoutedEventArgs e)
        {
            OpenModelessWindow(() => new PlanningPdfWindow(_repository));
        }

        private void MenuItem_DatabaseManagement_Click(object sender, RoutedEventArgs e)
        {
            OpenModelessWindow(() => new DatabaseManagementWindow(_repository, _viewModel.Locomotives, _viewModel.Tiles));
        }

        private async void MenuItem_ResetLocomotives_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Réinitialiser toutes les locomotives ?", "Réinitialisation des locomotives", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            Logger.Warning("User initiated locomotive reset", "Reset");

            foreach (var track in _viewModel.Tiles.SelectMany(tile => tile.Tracks))
            {
                track.Locomotives.Clear();
            }

            foreach (var loco in _viewModel.Locomotives)
            {
                loco.IsForecastOrigin = false;
                loco.ForecastTargetRollingLineTrackId = null;
                loco.IsForecastGhost = false;
                loco.ForecastSourceLocomotiveId = null;

                loco.AssignedTrackId = null;
                loco.AssignedTrackOffsetX = null;
                loco.Status = LocomotiveStatus.Ok;
                loco.TractionPercent = null;
                loco.HsReason = null;
                loco.Pool = "Lineas";
            }

            await _repository.AddHistoryAsync("ResetLocomotives", "Réinitialisation des locomotives.");
            _viewModel.UpdatePoolVisibility();
            PersistState();
            RefreshTapisT13();
            
            Logger.Info($"All locomotives reset successfully ({_viewModel.Locomotives.Count} locomotives)", "Reset");
        }

        private async void MenuItem_ResetTiles_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Supprimer toutes les tuiles ?", "Réinitialisation des tuiles", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            Logger.Warning("User initiated tile reset", "Reset");
            
            foreach (var tile in _viewModel.Tiles.ToList())
            {
                foreach (var track in tile.Tracks)
                {
                    foreach (var loco in track.Locomotives.ToList())
                    {
                        track.Locomotives.Remove(loco);
                        loco.AssignedTrackId = null;
                        loco.AssignedTrackOffsetX = null;
                    }
                }
            }

            _viewModel.Tiles.Clear();
            await _repository.AddHistoryAsync("ResetTiles", "Suppression de toutes les tuiles.");
            _viewModel.UpdatePoolVisibility();
            PersistState();
            RefreshTapisT13();
            
            Logger.Info("All tiles reset successfully", "Reset");
        }

        private void MenuItem_Import_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Logger.Info("Opening Import window", "Menu");
                
                var importWindow = new ImportWindow(_viewModel.Locomotives, () =>
                {
                    Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Callback when import is complete
                        // Refresh the UI to show updated pools
                        _viewModel.UpdatePoolVisibility();
                        PersistState();
                        RefreshLocomotivesDisplay();
                    });
                });
                
                importWindow.Owner = this;
                importWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Logger.Error("Failed to open Import window", ex, "Menu");
                MessageBox.Show($"Impossible d'ouvrir la fenêtre d'import.\n\nErreur: {ex.Message}", 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        private void ToggleDarkMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem)
            {
                _isDarkMode = menuItem.IsChecked;
                ApplyTheme(_isDarkMode);
            }
        }

        private void OpenModelessWindow<TWindow>(Func<TWindow> factory) where TWindow : Window
        {
            var windowType = typeof(TWindow);
            if (_modelessWindows.TryGetValue(windowType, out var existing) && existing.IsVisible)
            {
                if (existing is IRefreshableWindow refreshable)
                {
                    refreshable.RefreshData();
                }

                existing.Activate();
                return;
            }

            var window = factory();
            window.Owner = this;
            window.Closed += (_, _) => _modelessWindows.Remove(windowType);
            _modelessWindows[windowType] = window;

            if (window is IRefreshableWindow newRefreshable)
            {
                newRefreshable.RefreshData();
            }

            window.Show();
            window.Activate();
        }

        private void RefreshTapisT13()
        {
            if (_modelessWindows.TryGetValue(typeof(TapisT13Window), out var window)
                && window is IRefreshableWindow refreshable
                && window.IsVisible)
            {
                refreshable.RefreshData();
            }
        }

        private void LocomotiveItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement element)
            {
                return;
            }

            var contextMenu = element.ContextMenu;
            if (contextMenu == null)
            {
                return;
            }

            if (FindAncestor<ListBoxItem>(element) is ListBoxItem listBoxItem)
            {
                listBoxItem.IsSelected = true;
            }

            contextMenu.DataContext = element.DataContext;
            contextMenu.PlacementTarget = element;
            contextMenu.IsOpen = true;
            e.Handled = true;
        }

        private async void SaveLayoutPreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SimpleTextDialog("Enregistrer un preset", "Nom du preset :", "Nouveau preset") { Owner = this };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var name = dialog.ResponseText.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Veuillez saisir un nom valide.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var preset = BuildLayoutPreset(name);
            var existing = _viewModel.LayoutPresets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                _viewModel.LayoutPresets.Remove(existing);
            }
            _viewModel.LayoutPresets.Add(preset);
            SaveLayoutPresets();
            RefreshPresetMenu();
            await _repository.AddHistoryAsync("LayoutPresetSaved", $"Preset enregistré : {name}.");
        }

        private async void LoadLayoutPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not LayoutPreset preset)
            {
                return;
            }

            ApplyLayoutPreset(preset);
            await _repository.AddHistoryAsync("LayoutPresetLoaded", $"Preset chargé : {preset.Name}.");
            _viewModel.UpdatePoolVisibility();
            PersistState();
        }

        private async void DeleteLayoutPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not LayoutPreset preset)
            {
                return;
            }

            var result = MessageBox.Show($"Supprimer le preset \"{preset.Name}\" ?", "Supprimer preset",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            _viewModel.LayoutPresets.Remove(preset);
            SaveLayoutPresets();
            RefreshPresetMenu();
            await _repository.AddHistoryAsync("LayoutPresetDeleted", $"Preset supprimé : {preset.Name}.");
        }

        private void ApplyGaragePresets(TileModel tile)
        {
            if (tile.Type != TileType.VoieGarage)
            {
                return;
            }

            if (string.Equals(tile.Name, "Zeebrugge", StringComparison.OrdinalIgnoreCase))
            {
                RemoveMainTrack(tile);
                AddGarageZone(tile, "BRAM");
                AddGarageZone(tile, "ZWAN");
            }

            if (string.Equals(tile.Name, "Anvers Nord", StringComparison.OrdinalIgnoreCase))
            {
                RemoveMainTrack(tile);
                var blockTrack = new TrackModel
                {
                    Name = "917",
                    Kind = TrackKind.Zone,
                    LeftLabel = "BLOCK",
                    RightLabel = "BIF"
                };
                tile.Tracks.Add(blockTrack);
                tile.RefreshTrackCollections();
            }
        }

        private void AddGarageZone(TileModel tile, string name)
        {
            var track = new TrackModel
            {
                Name = name,
                Kind = TrackKind.Zone
            };
            tile.Tracks.Add(track);
            tile.RefreshTrackCollections();
        }

        private static void RemoveMainTrack(TileModel tile)
        {
            var main = tile.MainTrack;
            if (main != null)
            {
                tile.Tracks.Remove(main);
                tile.RefreshTrackCollections();
            }
        }

        private static TrackModel? GetTrackFromSender(object sender)
        {
            if (sender is FrameworkElement element)
            {
                if (element.Tag is TrackModel trackFromTag)
                {
                    return trackFromTag;
                }

                if (element.DataContext is TrackModel trackFromContext)
                {
                    return trackFromContext;
                }
            }

            return null;
        }

        private TileModel? GetTileFromSender(object sender)
        {
            if (sender is FrameworkElement element)
            {
                if (element.Tag is TileModel tileFromTag)
                {
                    return tileFromTag;
                }

                if (element.DataContext is TileModel tileFromContext)
                {
                    return tileFromContext;
                }
            }

            return null;
        }

        private void ResolveTileOverlap(TileModel tile)
        {
            const double step = 20;

            var hasOverlap = true;
            while (hasOverlap)
            {
                hasOverlap = false;
                foreach (var other in _viewModel.Tiles)
                {
                    if (ReferenceEquals(other, tile))
                    {
                        continue;
                    }

                    if (IsOverlapping(tile, other))
                    {
                        tile.X += step;
                        tile.Y += step;
                        hasOverlap = true;
                        break;
                    }
                }
            }
        }

        private static bool IsOverlapping(TileModel first, TileModel second)
        {
            return first.X < second.X + second.Width
                   && first.X + first.Width > second.X
                   && first.Y < second.Y + second.Height
                   && first.Y + first.Height > second.Y;
        }

        private void ApplyTheme(bool darkMode)
        {
            if (darkMode)
            {
                Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(34, 34, 34));
                Resources["PanelBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                Resources["CanvasBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(40, 40, 40));
                Resources["TileBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(55, 55, 55));
                Resources["TileBorderBrush"] = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                Resources["TrackBorderBrush"] = new SolidColorBrush(Color.FromRgb(80, 80, 80));
                Resources["ListBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(50, 50, 50));
                Resources["ListBorderBrush"] = new SolidColorBrush(Color.FromRgb(90, 90, 90));
                Resources["AppForegroundBrush"] = new SolidColorBrush(Colors.WhiteSmoke);
                Resources["MenuBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                Resources["MenuBorderBrush"] = new SolidColorBrush(Color.FromRgb(70, 70, 70));
                Resources["ToolBarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(45, 45, 45));
                Resources["ToolBarBorderBrush"] = new SolidColorBrush(Color.FromRgb(70, 70, 70));
            }
            else
            {
                Resources["AppBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(242, 242, 242));
                Resources["PanelBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(239, 244, 255));
                Resources["CanvasBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(247, 247, 247));
                Resources["TileBackgroundBrush"] = new SolidColorBrush(Colors.White);
                Resources["TileBorderBrush"] = new SolidColorBrush(Color.FromRgb(176, 176, 176));
                Resources["TrackBorderBrush"] = new SolidColorBrush(Color.FromRgb(224, 224, 224));
                Resources["ListBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(245, 245, 245));
                Resources["ListBorderBrush"] = new SolidColorBrush(Color.FromRgb(221, 221, 221));
                Resources["AppForegroundBrush"] = new SolidColorBrush(Color.FromRgb(17, 17, 17));
                Resources["MenuBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(242, 242, 242));
                Resources["MenuBorderBrush"] = new SolidColorBrush(Color.FromRgb(221, 221, 221));
                Resources["ToolBarBackgroundBrush"] = new SolidColorBrush(Color.FromRgb(242, 242, 242));
                Resources["ToolBarBorderBrush"] = new SolidColorBrush(Color.FromRgb(221, 221, 221));
            }
        }

        private void LoadLayoutPresets()
        {
            _viewModel.LayoutPresets.Clear();
            if (File.Exists(LayoutPresetFileName))
            {
                try
                {
                    var json = File.ReadAllText(LayoutPresetFileName);
                    var presets = JsonSerializer.Deserialize<List<LayoutPreset>>(json, GetPresetSerializerOptions());
                    if (presets != null)
                    {
                        foreach (var preset in presets)
                        {
                            _viewModel.LayoutPresets.Add(preset);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Erreur lors de la lecture du fichier LayoutPreset", ex, "Layout");
                    _viewModel.LayoutPresets.Clear();
                }
            }

            if (_viewModel.LayoutPresets.All(p => !string.Equals(p.Name, "Défaut", StringComparison.OrdinalIgnoreCase)))
            {
                _viewModel.LayoutPresets.Add(BuildDefaultPreset());
            }

            SaveLayoutPresets();
        }

        private void SaveLayoutPresets()
        {
            var json = JsonSerializer.Serialize(_viewModel.LayoutPresets, GetPresetSerializerOptions());
            File.WriteAllText(LayoutPresetFileName, json);
        }

        private void RefreshPresetMenu()
        {
            if (ViewPresetsMenu == null || ViewPresetsDeleteMenu == null)
            {
                return;
            }

            ViewPresetsMenu.Items.Clear();
            ViewPresetsDeleteMenu.Items.Clear();
            foreach (var preset in _viewModel.LayoutPresets.OrderBy(p => p.Name))
            {
                var item = new MenuItem
                {
                    Header = preset.Name,
                    Tag = preset
                };
                item.Click += LoadLayoutPreset_Click;
                ViewPresetsMenu.Items.Add(item);

                if (!string.Equals(preset.Name, "Défaut", StringComparison.OrdinalIgnoreCase))
                {
                    var deleteItem = new MenuItem
                    {
                        Header = preset.Name,
                        Tag = preset
                    };
                    deleteItem.Click += DeleteLayoutPreset_Click;
                    ViewPresetsDeleteMenu.Items.Add(deleteItem);
                }
            }
        }

        private LayoutPreset BuildLayoutPreset(string name)
        {
            var tiles = _viewModel.Tiles
                .Where(tile => tile.Type != TileType.ArretLigne)
                .Select(tile => new LayoutTile
                {
                    Name = tile.Name,
                    Type = tile.Type,
                    X = tile.X,
                    Y = tile.Y,
                    Width = tile.Width,
                    Height = tile.Height,
                    Tracks = tile.Tracks.Select(track => new LayoutTrack
                    {
                        Name = track.Name,
                        Kind = track.Kind,
                        LeftLabel = track.LeftLabel,
                        RightLabel = track.RightLabel,
                        IsLeftBlocked = track.IsLeftBlocked,
                        IsRightBlocked = track.IsRightBlocked
                    }).ToList()
                }).ToList();

            return new LayoutPreset
            {
                Name = name,
                Tiles = tiles
            };
        }

        private LayoutPreset BuildDefaultPreset()
        {
            var preset = new LayoutPreset
            {
                Name = "Défaut",
                Tiles = new List<LayoutTile>()
            };

            var defaultTiles = new List<(TileType type, string name)>
            {
                (TileType.Depot, "Thionville"),
                (TileType.Depot, "Mulhouse Nord"),
                (TileType.VoieGarage, "Zeebrugge"),
                (TileType.VoieGarage, "Anvers Nord"),
                (TileType.VoieGarage, "Bale")
            };

            const double startX = 20;
            const double startY = 20;
            const double stepX = 380;
            const double stepY = 260;

            for (var index = 0; index < defaultTiles.Count; index++)
            {
                var (type, name) = defaultTiles[index];
                var tile = new TileModel
                {
                    Name = name,
                    Type = type,
                    X = startX + (index % 2) * stepX,
                    Y = startY + (index / 2) * stepY
                };
                _viewModel.EnsureDefaultTracks(tile);
                _viewModel.ApplyGaragePresets(tile);

                preset.Tiles.Add(new LayoutTile
                {
                    Name = tile.Name,
                    Type = tile.Type,
                    X = tile.X,
                    Y = tile.Y,
                    Width = tile.Width,
                    Height = tile.Height,
                    Tracks = tile.Tracks.Select(track => new LayoutTrack
                    {
                        Name = track.Name,
                        Kind = track.Kind,
                        LeftLabel = track.LeftLabel,
                        RightLabel = track.RightLabel,
                        IsLeftBlocked = track.IsLeftBlocked,
                        IsRightBlocked = track.IsRightBlocked
                    }).ToList()
                });
            }

            return preset;
        }

        private void ApplyLayoutPreset(LayoutPreset preset)
        {
            var removableTiles = _viewModel.Tiles.Where(tile => tile.Type != TileType.ArretLigne).ToList();
            foreach (var tile in removableTiles)
            {
                foreach (var track in tile.Tracks)
                {
                    foreach (var loco in track.Locomotives.ToList())
                    {
                        track.Locomotives.Remove(loco);
                        loco.AssignedTrackId = null;
                        loco.AssignedTrackOffsetX = null;
                    }
                }
                _viewModel.Tiles.Remove(tile);
            }

            foreach (var layoutTile in preset.Tiles.Where(t => t.Type != TileType.ArretLigne))
            {
                var tile = new TileModel
                {
                    Name = layoutTile.Name,
                    Type = layoutTile.Type,
                    X = layoutTile.X,
                    Y = layoutTile.Y,
                    Width = layoutTile.Width > 0 ? layoutTile.Width : MinTileWidth,
                    Height = layoutTile.Height > 0 ? layoutTile.Height : MinTileHeight
                };
                foreach (var layoutTrack in layoutTile.Tracks)
                {
                    tile.Tracks.Add(new TrackModel
                    {
                        Name = layoutTrack.Name,
                        Kind = layoutTrack.Kind,
                        LeftLabel = layoutTrack.LeftLabel,
                        RightLabel = layoutTrack.RightLabel,
                        IsLeftBlocked = layoutTrack.IsLeftBlocked,
                        IsRightBlocked = layoutTrack.IsRightBlocked
                    });
                }
                tile.RefreshTrackCollections();
                _viewModel.Tiles.Add(tile);
            }
            UpdateTileCanvasExtent();
        }

        private void UpdateTileCanvasExtent()
        {
            if (TileCanvas == null)
            {
                return;
            }

            if (!_viewModel.Tiles.Any())
            {
                TileCanvas.Width = 0;
                TileCanvas.Height = 0;
                return;
            }

            var maxX = _viewModel.Tiles.Max(tile => tile.X + tile.Width);
            var maxY = _viewModel.Tiles.Max(tile => tile.Y + tile.Height);
            
            TileCanvas.Width = maxX + CanvasPadding;
            TileCanvas.Height = maxY + CanvasPadding;
        }

        private Point GetDropPositionInWorkspace(MouseEventArgs e)
        {
            if (TileCanvas == null)
            {
                return e.GetPosition(null);
            }

            return e.GetPosition(TileCanvas);
        }

        private static JsonSerializerOptions GetPresetSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }


        private void RefreshLocomotivesDisplay()
        {
            // Force complete refresh by reassigning ItemsSource
            LocomotiveList.ItemsSource = null;
            LocomotiveList.ItemsSource = _viewModel.Locomotives;
            
            // Re-initialize the view (sort, etc.)
            InitializeLocomotiveView();
        }
    }
}
