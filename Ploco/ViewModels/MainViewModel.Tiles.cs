using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Ploco.Dialogs;
using Ploco.Models;

namespace Ploco.ViewModels
{
    public partial class MainViewModel
    {
        private const int DefaultRollingLineCount = 10;

        public event Action? OnWorkspaceChanged;

        [RelayCommand]
        public async Task AddTileAsync()
        {
            var (success, type, name) = _dialogService.ShowPlaceDialog();
            if (!success || type == null)
            {
                return;
            }

            if (type == TileType.ArretLigne)
            {
                var (lineSuccess, lineResult) = _dialogService.ShowLinePlaceDialog(name);
                if (lineSuccess && lineResult != null)
                {
                    await AddLineTileAsync(lineResult);
                }
                return;
            }

            if (type == TileType.RollingLine)
            {
                await AddRollingLineTileAsync(name);
                return;
            }

            await AddTileInternalAsync(type.Value, name);
        }

        [RelayCommand]
        public async Task RenameTileAsync(TileModel tile)
        {
            if (tile == null) return;

            var (success, result) = _dialogService.ShowSimpleTextDialog("Renommer la tuile", "Nom :", tile.Name);
            if (success)
            {
                tile.Name = result;
                await _repository.AddHistoryAsync("TileRenamed", $"Tuile renommée en {tile.Name}.");
                OnStatePersisted?.Invoke();
            }
        }

        [RelayCommand]
        public async Task DeleteTileAsync(TileModel tile)
        {
            if (tile == null) return;

            foreach (var track in tile.Tracks.ToList())
            {
                foreach (var loco in track.Locomotives.ToList())
                {
                    if (loco.IsForecastGhost)
                    {
                        var origin = Locomotives.FirstOrDefault(l => l.Id == loco.ForecastSourceLocomotiveId || (l.Number == loco.Number && l.IsForecastOrigin));
                        if (origin != null)
                        {
                            origin.IsForecastOrigin = false;
                            origin.ForecastTargetRollingLineTrackId = null;
                        }
                    }
                    else if (loco.IsForecastOrigin)
                    {
                        Helpers.PrevisionnelLogicHelper.RemoveForecastGhostsFor(loco, Tiles);
                        loco.IsForecastOrigin = false;
                        loco.ForecastTargetRollingLineTrackId = null;
                    }

                    track.Locomotives.Remove(loco);
                    loco.AssignedTrackId = null;
                }
            }
            Tiles.Remove(tile);
            await _repository.AddHistoryAsync("TileDeleted", $"Suppression du lieu {tile.DisplayTitle}.");
            UpdatePoolVisibility();
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task EmptyTileAsync(TileModel tile)
        {
            if (tile == null) return;
            
            var locoCount = tile.Tracks.Sum(t => t.Locomotives.Count);
            if (locoCount == 0) return;

            var result = System.Windows.MessageBox.Show($"Toutes les {locoCount} locomotives vont quitter ce lieu pour retourner dans le parc.\nÊtes-vous sûr(e) de le vider complètement ?", "Vider le lieu", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);
            
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                foreach (var track in tile.Tracks.ToList())
                {
                    foreach (var loco in track.Locomotives.ToList())
                    {
                        if (loco.IsForecastGhost)
                        {
                            var origin = Locomotives.FirstOrDefault(l => l.Id == loco.ForecastSourceLocomotiveId || (l.Number == loco.Number && l.IsForecastOrigin));
                            if (origin != null)
                            {
                                origin.IsForecastOrigin = false;
                                origin.ForecastTargetRollingLineTrackId = null;
                            }
                        }
                        else if (loco.IsForecastOrigin)
                        {
                            Helpers.PrevisionnelLogicHelper.RemoveForecastGhostsFor(loco, Tiles);
                            loco.IsForecastOrigin = false;
                            loco.ForecastTargetRollingLineTrackId = null;
                        }

                        track.Locomotives.Remove(loco);
                        loco.AssignedTrackId = null;
                        loco.AssignedTrackOffsetX = null;
                    }
                }
                
                await _repository.AddHistoryAsync("TileEmptied", $"Le lieu {tile.DisplayTitle} a été vidé de ses locomotives.");
                UpdatePoolVisibility();
                OnStatePersisted?.Invoke();
                OnWorkspaceChanged?.Invoke();
            }
        }

        [RelayCommand]
        public async Task AddDepotOutputTrackAsync(TileModel tile)
        {
            if (tile == null) return;
            if (tile.OutputTracks.Any())
            {
                _dialogService.ShowMessage("Action impossible", "Une seule voie de sortie est autorisée.");
                return;
            }
            var track = new TrackModel { Name = "Voie de sortie", Kind = TrackKind.Output };
            tile.Tracks.Add(track);
            tile.RefreshTrackCollections();
            await _repository.AddHistoryAsync("TrackAdded", $"Ajout de la voie de sortie dans {tile.DisplayTitle}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task AddZoneTrackAsync(TileModel tile)
        {
            if (tile == null) return;
            var (success, name) = _dialogService.ShowSimpleTextDialog("Ajouter une zone", "Nom de zone :", "Zone 1");
            if (success)
            {
                var track = new TrackModel { Name = name, Kind = TrackKind.Zone };
                tile.Tracks.Add(track);
                tile.RefreshTrackCollections();
                await _repository.AddHistoryAsync("ZoneAdded", $"Ajout de la zone {track.Name} dans {tile.DisplayTitle}.");
                OnStatePersisted?.Invoke();
                OnWorkspaceChanged?.Invoke();
            }
        }

        [RelayCommand]
        public async Task AddLineTrackAsync(TileModel tile)
        {
            if (tile == null) return;
            var (success, track) = _dialogService.ShowLineTrackDialog();
            if (success && track != null)
            {
                track.Kind = TrackKind.Line;
                tile.Tracks.Add(track);
                tile.RefreshTrackCollections();
                await _repository.AddHistoryAsync("LineTrackAdded", $"Ajout de la voie {track.Name} dans {tile.DisplayTitle}.");
                OnStatePersisted?.Invoke();
                OnWorkspaceChanged?.Invoke();
            }
        }

        [RelayCommand]
        public async Task ConfigureRollingLineTracksAsync(TileModel tile)
        {
            if (tile == null || tile.Type != TileType.RollingLine) return;
            var currentNumbers = ResolveRollingLineNumbers(tile);
            var numbers = _dialogService.ShowRollingLineRangeDialog(currentNumbers.Count);
            if (numbers == null) return;

            var adjustedNumbers = EnsureRollingLineNumbersWithinAssignments(tile, numbers);
            if (adjustedNumbers.Count != numbers.Count)
            {
                _dialogService.ShowMessage("Information", "La configuration a été ajustée pour conserver les locomotives déjà affectées.");
            }
            tile.RollingLineCount = adjustedNumbers.Count;
            NormalizeRollingLineTracks(tile, adjustedNumbers);
            await _repository.AddHistoryAsync("RollingLineAdded", $"Configuration des lignes ({FormatRollingLineRange(adjustedNumbers)}) dans {tile.DisplayTitle}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task RenameTrackAsync(TrackModel track)
        {
            if (track == null) return;
            var (success, name) = _dialogService.ShowSimpleTextDialog("Renommer la voie", "Nouveau nom :", track.Name);
            if (success)
            {
                track.Name = name;
                await _repository.AddHistoryAsync("TrackRenamed", $"Voie renommée en {track.Name}.");
                OnStatePersisted?.Invoke();
            }
        }

        [RelayCommand]
        public async Task DeleteTrackAsync(TrackModel track)
        {
            if (track == null) return;
            var tile = Tiles.FirstOrDefault(t => t.Tracks.Contains(track));
            if (tile == null) return;

            foreach (var loco in track.Locomotives.ToList())
            {
                if (loco.IsForecastGhost)
                {
                    var origin = Locomotives.FirstOrDefault(l => l.Id == loco.ForecastSourceLocomotiveId || (l.Number == loco.Number && l.IsForecastOrigin));
                    if (origin != null)
                    {
                        origin.IsForecastOrigin = false;
                        origin.ForecastTargetRollingLineTrackId = null;
                    }
                }
                else if (loco.IsForecastOrigin)
                {
                    Helpers.PrevisionnelLogicHelper.RemoveForecastGhostsFor(loco, Tiles);
                    loco.IsForecastOrigin = false;
                    loco.ForecastTargetRollingLineTrackId = null;
                }

                track.Locomotives.Remove(loco);
                loco.AssignedTrackId = null;
            }
            tile.Tracks.Remove(track);
            tile.RefreshTrackCollections();
            await _repository.AddHistoryAsync("TrackRemoved", $"Suppression de la voie {track.Name} dans {tile.DisplayTitle}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        // --- Helper Methods ---

        private async Task AddTileInternalAsync(TileType type, string name)
        {
            var tile = new TileModel
            {
                Name = name,
                Type = type,
                X = 20 + Tiles.Count * 30,
                Y = 20 + Tiles.Count * 30
            };
            EnsureDefaultTracks(tile);
            ApplyGaragePresets(tile);
            Tiles.Add(tile);
            await _repository.AddHistoryAsync("TileCreated", $"Création du lieu {tile.DisplayTitle}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        private async Task AddLineTileAsync(LinePlaceDialogResult dialog)
        {
            var tile = new TileModel
            {
                Name = dialog.PlaceName,
                Type = TileType.ArretLigne,
                X = 20 + Tiles.Count * 30,
                Y = 20 + Tiles.Count * 30
            };

            var track = new TrackModel
            {
                Name = dialog.TrackName,
                Kind = TrackKind.Line,
                IsOnTrain = dialog.IsOnTrain,
                TrainNumber = dialog.IsOnTrain ? dialog.TrainNumber : null,
                StopTime = dialog.IsOnTrain ? dialog.StopTime : null,
                IssueReason = dialog.IssueReason,
                IsLocomotiveHs = dialog.IsLocomotiveHs
            };

            tile.Tracks.Add(track);
            tile.RefreshTrackCollections();
            Tiles.Add(tile);
            await _repository.AddHistoryAsync("TileCreated", $"Création du lieu {tile.DisplayTitle}.");
            await _repository.AddHistoryAsync("LineTrackAdded", $"Ajout de la voie {track.Name} dans {tile.DisplayTitle}.");

            var missingLocos = new List<string>();
            var unavailableLocos = new List<string>();
            var invalidLocos = new List<string>();
            var locomotiveNumbers = ParseLocomotiveNumbers(dialog.LocomotiveNumbers, invalidLocos);
            foreach (var number in locomotiveNumbers)
            {
                var loco = Locomotives.FirstOrDefault(l => l.Number == number);
                if (loco == null)
                {
                    missingLocos.Add(number.ToString());
                    continue;
                }

                if (loco.AssignedTrackId != null)
                {
                    // Forcer le déplacement : on arrache la loco de son ancienne voie
                    var oldTrack = Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Locomotives.Contains(loco));
                    if (oldTrack != null)
                    {
                        oldTrack.Locomotives.Remove(loco);
                        UpdatePoolVisibility();
                    }
                }

                MoveLocomotiveToTrack(loco, track, track.Locomotives.Count);
            }

            var missingHsLocos = new List<string>();
            var invalidHsLocos = new List<string>();
            if (dialog.IsLocomotiveHs)
            {
                var hsNumbers = ParseLocomotiveNumbers(dialog.HsLocomotiveNumbers, invalidHsLocos);
                foreach (var number in hsNumbers)
                {
                    var loco = Locomotives.FirstOrDefault(l => l.Number == number);
                    if (loco == null)
                    {
                        missingHsLocos.Add(number.ToString());
                        continue;
                    }

                    if (loco.Status != LocomotiveStatus.HS)
                    {
                        loco.Status = LocomotiveStatus.HS;
                        await _repository.AddHistoryAsync("StatusChanged", $"Statut modifié pour {loco.Number} (HS).");
                    }
                }
            }

            if (invalidLocos.Any() || invalidHsLocos.Any())
            {
                _dialogService.ShowMessage("Information", $"Numéros invalides ignorés : {string.Join(", ", invalidLocos.Concat(invalidHsLocos))}.");
            }

            if (missingLocos.Any() || missingHsLocos.Any())
            {
                var missingMessage = new List<string>();
                if (missingLocos.Any())
                {
                    missingMessage.Add($"Locomotives introuvables pour l'ajout : {string.Join(", ", missingLocos)}.");
                }
                if (missingHsLocos.Any())
                {
                    missingMessage.Add($"Locomotives introuvables pour HS : {string.Join(", ", missingHsLocos)}.");
                }
                _dialogService.ShowMessage("Information", string.Join(Environment.NewLine, missingMessage));
            }

            if (invalidLocos.Any() || invalidHsLocos.Any())
            {
                _dialogService.ShowMessage("Information", $"Numéros invalides ignorés : {string.Join(", ", invalidLocos.Concat(invalidHsLocos))}.");
            }

            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        public void MoveLocomotiveToTrack(LocomotiveModel loco, TrackModel targetTrack, int insertIndex)
        {
            Helpers.Logger.Debug($"Moving locomotive Id={loco.Id} Number={loco.Number} to track {targetTrack.Name} at index {insertIndex}", "Movement");
            
            if (targetTrack.Kind == TrackKind.RollingLine)
            {
                var realLocosInTarget = targetTrack.Locomotives
                    .Where(l => !l.IsForecastGhost)
                    .ToList();
                
                if (realLocosInTarget.Any() && !targetTrack.Locomotives.Contains(loco))
                {
                    // L'UI a déjà fait le Swap via TryMoveLocomotiveToTrack si c'était éligible.
                    // Si on arrive ici, on bloque gentiment.
                    return;
                }
            }

            var currentTrack = Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Locomotives.Contains(loco));
            if (currentTrack != null)
            {
                var currentIndex = currentTrack.Locomotives.IndexOf(loco);
                if (currentTrack == targetTrack)
                {
                    if (currentIndex != insertIndex && insertIndex <= targetTrack.Locomotives.Count)
                    {
                        if (insertIndex > currentIndex) insertIndex--;
                        targetTrack.Locomotives.Move(currentIndex, insertIndex);
                        Helpers.Logger.Debug($"Locomotive moved within same track {targetTrack.Name} from {currentIndex} to {insertIndex}", "Movement");
                        OnStatePersisted?.Invoke();
                    }
                    return;
                }

                currentTrack.Locomotives.Remove(loco);
                Helpers.Logger.Debug($"Locomotive removed from source track {currentTrack.Name}", "Movement");
                
                if (!currentTrack.Locomotives.Any() && currentTrack.Kind == TrackKind.Line)
                {
                    var parentTile = Tiles.FirstOrDefault(t => t.Tracks.Contains(currentTrack));
                    if (parentTile != null && parentTile.Type == TileType.ArretLigne)
                    {
                         // We must bypass awaiting the RelayCommand here if it becomes async
                         // because MoveLocomotiveToTrack remains synchronous since it's fundamentally UI layout operations.
                         // But we should use the new Async name to trigger the Task cleanly.
                        _ = DeleteTileAsync(parentTile);
                        Helpers.Logger.Debug($"Auto-deleted empty line place {parentTile.Name}", "Movement");
                    }
                }
            }

            if (insertIndex >= 0 && insertIndex <= targetTrack.Locomotives.Count)
            {
                targetTrack.Locomotives.Insert(insertIndex, loco);
            }
            else
            {
                targetTrack.Locomotives.Add(loco);
            }
            
            loco.AssignedTrackId = targetTrack.Id;
            Helpers.Logger.Debug($"Locomotive added to target track {targetTrack.Name} at position {targetTrack.Locomotives.IndexOf(loco)}", "Movement");
            UpdatePoolVisibility();
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        private static List<int> ParseLocomotiveNumbers(string input, List<string> invalidTokens)
        {
            var numbers = new List<int>();
            if (string.IsNullOrWhiteSpace(input))
            {
                return numbers;
            }

            var tokens = input.Split(new[] { ' ', ',', ';', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var token in tokens)
            {
                if (int.TryParse(token, out var number))
                {
                    numbers.Add(number);
                }
                else
                {
                    invalidTokens.Add(token);
                }
            }

            return numbers;
        }

        private async Task AddRollingLineTileAsync(string name)
        {
            var numbers = _dialogService.ShowRollingLineRangeDialog(DefaultRollingLineCount);
            if (numbers == null) return;

            var tile = new TileModel
            {
                Name = name,
                Type = TileType.RollingLine,
                X = 20 + Tiles.Count * 30,
                Y = 20 + Tiles.Count * 30,
                RollingLineCount = numbers.Count
            };

            NormalizeRollingLineTracks(tile, numbers);
            Tiles.Add(tile);
            await _repository.AddHistoryAsync("TileCreated", $"Création du lieu {tile.DisplayTitle}.");
            await _repository.AddHistoryAsync("RollingLineAdded", $"Lignes {FormatRollingLineRange(numbers)} ajoutées dans {tile.DisplayTitle}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        internal void EnsureDefaultTracks(TileModel tile)
        {
            if (string.Equals(tile.Name, "Anvers Nord", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(tile.Name, "Zeebrugge", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (!tile.Tracks.Any(t => t.Kind == TrackKind.Main))
            {
                tile.Tracks.Add(CreateDefaultTrack(tile));
                tile.RefreshTrackCollections();
            }
        }

        private TrackModel CreateDefaultTrack(TileModel tile)
        {
            return tile.Type switch
            {
                TileType.Depot => new TrackModel { Name = "Locomotives", Kind = TrackKind.Main },
                TileType.VoieGarage => new TrackModel { Name = "Vrac", Kind = TrackKind.Main },
                _ => new TrackModel { Name = "Locomotives", Kind = TrackKind.Main }
            };
        }

        internal void ApplyGaragePresets(TileModel tile)
        {
            if (tile.Type == TileType.VoieGarage)
            {
                if (tile.Name.Equals("Sibelit", StringComparison.OrdinalIgnoreCase))
                {
                    if (!tile.Tracks.Any(t => t.Kind == TrackKind.Zone && t.Name == "Zone 1")) tile.Tracks.Add(new TrackModel { Name = "Zone 1", Kind = TrackKind.Zone });
                    if (!tile.Tracks.Any(t => t.Kind == TrackKind.Zone && t.Name == "Zone 2")) tile.Tracks.Add(new TrackModel { Name = "Zone 2", Kind = TrackKind.Zone });
                }
                else if (tile.Name.Equals("Lineas", StringComparison.OrdinalIgnoreCase))
                {
                    if (!tile.Tracks.Any(t => t.Kind == TrackKind.Zone && t.Name == "Prête Lineas")) tile.Tracks.Add(new TrackModel { Name = "Prête Lineas", Kind = TrackKind.Zone });
                }
            }

            if (tile.Name.Equals("Anvers Nord", StringComparison.OrdinalIgnoreCase))
            {
                if (!tile.Tracks.Any(t => t.Kind == TrackKind.Zone && t.Name == "917"))
                {
                    tile.Tracks.Add(new TrackModel { Name = "917", Kind = TrackKind.Zone, LeftLabel = "BLOCK", RightLabel = "BIF" });
                }
            }
            else if (tile.Name.Equals("Zeebrugge", StringComparison.OrdinalIgnoreCase))
            {
                if (!tile.Tracks.Any(t => t.Kind == TrackKind.Zone && t.Name == "BRAM"))
                {
                    tile.Tracks.Add(new TrackModel { Name = "BRAM", Kind = TrackKind.Zone });
                }
                if (!tile.Tracks.Any(t => t.Kind == TrackKind.Zone && t.Name == "ZWAN"))
                {
                    tile.Tracks.Add(new TrackModel { Name = "ZWAN", Kind = TrackKind.Zone });
                }
            }
            
            tile.RefreshTrackCollections();
        }

        private static List<int> ResolveRollingLineNumbers(TileModel tile)
        {
            var existingNumbers = tile.Tracks
                .Where(t => t.Kind == TrackKind.RollingLine)
                .Select(t => int.TryParse(t.Name, out var value) ? value : 0)
                .Where(value => value > 0)
                .OrderBy(n => n)
                .ToList();

            if (existingNumbers.Any())
            {
                return existingNumbers;
            }

            var count = tile.RollingLineCount ?? DefaultRollingLineCount;
            // Assuming RollingLineStartNumber is 1
            return Enumerable.Range(1, count).ToList();
        }

        private static List<int> EnsureRollingLineNumbersWithinAssignments(TileModel tile, List<int> requestedNumbers)
        {
            var assignedNumbers = tile.Tracks
                .Where(t => t.Kind == TrackKind.RollingLine && t.Locomotives.Any())
                .Select(t => int.TryParse(t.Name, out var value) ? value : 0)
                .Where(value => value > 0)
                .ToList();

            if (!assignedNumbers.Any())
            {
                return requestedNumbers;
            }

            var result = new HashSet<int>(requestedNumbers);
            foreach (var assigned in assignedNumbers)
            {
                result.Add(assigned);
            }

            return result.OrderBy(n => n).ToList();
        }

        private void NormalizeRollingLineTracks(TileModel tile, List<int> desiredNumbers)
        {
            var desiredNumbersSet = desiredNumbers.Select(n => n.ToString()).ToHashSet();
            var existing = tile.Tracks
                .Where(t => t.Kind == TrackKind.RollingLine)
                .ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

            var rollingTracks = new List<TrackModel>();
            foreach (var number in desiredNumbers.OrderBy(n => n))
            {
                var key = number.ToString();
                if (existing.TryGetValue(key, out var track))
                {
                    rollingTracks.Add(track);
                }
                else
                {
                    rollingTracks.Add(new TrackModel
                    {
                        Name = key,
                        Kind = TrackKind.RollingLine
                    });
                }
            }

            var otherTracks = tile.Tracks.Where(t => t.Kind != TrackKind.RollingLine).ToList();
            tile.Tracks.Clear();
            foreach (var track in otherTracks.Concat(rollingTracks))
            {
                tile.Tracks.Add(track);
            }
            tile.RefreshTrackCollections();
        }

        private string FormatRollingLineRange(List<int> numbers)
        {
            if (numbers == null || !numbers.Any()) return "Aucune";
            var result = new List<string>();
            int start = numbers[0];
            int end = numbers[0];

            for (int i = 1; i < numbers.Count; i++)
            {
                if (numbers[i] == end + 1)
                {
                    end = numbers[i];
                }
                else
                {
                    result.Add(start == end ? start.ToString() : $"{start}-{end}");
                    start = numbers[i];
                    end = numbers[i];
                }
            }
            result.Add(start == end ? start.ToString() : $"{start}-{end}");
            return string.Join(", ", result);
        }
    }
}
