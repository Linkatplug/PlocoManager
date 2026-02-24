using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Ploco.Helpers;
using Ploco.Models;

namespace Ploco.ViewModels
{
    public partial class MainViewModel
    {
        [RelayCommand]
        public void DropLocomotive(LocomotiveDropArgs args)
        {
            if (args?.Loco == null || args.Target == null) return;

            var loco = args.Loco;

            if (args.IsRollingLineRow)
            {
                if (args.Target is TrackModel track)
                {
                    TryMoveLocomotiveToTrack(loco, track);
                }
                return;
            }

            if (args.Target is TrackModel targetTrack)
            {
                if (!LocomotiveStateHelper.CanDropLocomotiveOnTrack(loco, targetTrack))
                {
                    _dialogService.ShowMessage("Action impossible", "Le placement de cette locomotive sur cette voie est impossible (règles métier).");
                    return;
                }

                int insertIndex = args.InsertIndex >= 0 ? args.InsertIndex : targetTrack.Locomotives.Count;
                MoveLocomotiveToTrack(loco, targetTrack, insertIndex);
                UpdateLocomotiveDropOffset(loco, targetTrack, args.DropPosition.X, args.TargetActualWidth);
            }
            else if (args.Target is TileModel tile)
            {
                if (tile.Type == TileType.RollingLine) return;

                var defaultTrack = GetDefaultDropTrack(tile);
                if (defaultTrack != null)
                {
                    MoveLocomotiveToTrack(loco, defaultTrack, defaultTrack.Locomotives.Count);
                }
                else
                {
                    _dialogService.ShowWarning("Action impossible", "Aucune voie disponible pour déposer une locomotive.");
                }
            }
            else if (args.Target is MainViewModel) // Pool
            {
                RemoveLocomotiveFromTrack(loco);
            }

            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        private void RemoveLocomotiveFromTrack(LocomotiveModel loco)
        {
            var track = Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Locomotives.Contains(loco));
            if (track != null)
            {
                track.Locomotives.Remove(loco);
                loco.AssignedTrackId = null;
                loco.AssignedTrackOffsetX = null;
                _repository.AddHistory("LocomotiveRemoved", $"Loco {loco.Number} retirée de {track.Name}.");
                UpdatePoolVisibility();
            }
        }

        private TrackModel? GetDefaultDropTrack(TileModel tile)
        {
            if (tile.Type == TileType.ArretLigne)
            {
                return tile.LineTracks.FirstOrDefault();
            }

            if (tile.Type == TileType.RollingLine)
            {
                return tile.RollingLineTracks.FirstOrDefault(t => !t.Locomotives.Any())
                       ?? tile.RollingLineTracks.FirstOrDefault();
            }

            return tile.MainTrack;
        }

        private void UpdateLocomotiveDropOffset(LocomotiveModel loco, TrackModel track, double dropX, double actualWidth)
        {
            if (track.Kind != TrackKind.Line && track.Kind != TrackKind.Zone && track.Kind != TrackKind.Output)
            {
                loco.AssignedTrackOffsetX = null;
                return;
            }

            var width = actualWidth > 0 ? actualWidth : 100; // Fallback
            loco.AssignedTrackOffsetX = PlacementLogicHelper.CalculateBestOffset(track, loco, dropX, width);
            PlacementLogicHelper.EnsureTrackOffsets(track);
        }

        private void TryMoveLocomotiveToTrack(LocomotiveModel loco, TrackModel targetTrack)
        {
            if (!LocomotiveStateHelper.CanDropLocomotiveOnTrack(loco, targetTrack))
            {
                _dialogService.ShowWarning("Action impossible", "Les locomotives fantômes (prévision) ou les règles métier empêchent ce déplacement.");
                return;
            }
            
            // Swap if occupied
            if (targetTrack.Locomotives.Any() && !targetTrack.Locomotives.Contains(loco))
            {
                var existingLoco = targetTrack.Locomotives.First();
                
                if (existingLoco.IsForecastGhost)
                {
                    _dialogService.ShowWarning("Action impossible", "Le swap avec une locomotive en mode prévisionnel n'est pas autorisé. Validez ou annulez la prévision.");
                    return;
                }

                if (LocomotiveStateHelper.IsEligibleForSwap(loco, existingLoco))
                {
                    var sourceTrack = Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Locomotives.Contains(loco));
                    if (sourceTrack != null)
                    {
                        SwapLocomotivesBetweenTracks(loco, sourceTrack, existingLoco, targetTrack);
                    }
                    else
                    {
                        targetTrack.Locomotives.Remove(existingLoco);
                        existingLoco.AssignedTrackId = null;
                        existingLoco.AssignedTrackOffsetX = null;

                        targetTrack.Locomotives.Add(loco);
                        loco.AssignedTrackId = targetTrack.Id;
                        loco.AssignedTrackOffsetX = null;

                        PlacementLogicHelper.EnsureTrackOffsets(targetTrack);
                        UpdatePoolVisibility();
                        OnStatePersisted?.Invoke();
                        OnWorkspaceChanged?.Invoke();
                    }
                    return;
                }
                
                _dialogService.ShowMessage("Swap impossible", "Cette tuile est déjà occupée par une locomotive avec laquelle vous ne pouvez pas effectuer d'échange.");
                return;
            }

            MoveLocomotiveToTrack(loco, targetTrack, 0);
        }

        // Exposing old private swap logic from code-behind
        private void SwapLocomotivesBetweenTracks(LocomotiveModel loco1, TrackModel track1, LocomotiveModel loco2, TrackModel track2)
        {
            track1.Locomotives.Remove(loco1);
            track2.Locomotives.Remove(loco2);
            
            track1.Locomotives.Add(loco2);
            track2.Locomotives.Add(loco1);
            
            loco1.AssignedTrackId = track2.Id;
            loco1.AssignedTrackOffsetX = null;
            loco2.AssignedTrackId = track1.Id;
            loco2.AssignedTrackOffsetX = null;
            
            PlacementLogicHelper.EnsureTrackOffsets(track1);
            PlacementLogicHelper.EnsureTrackOffsets(track2);
            UpdatePoolVisibility();
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }
    }
}
