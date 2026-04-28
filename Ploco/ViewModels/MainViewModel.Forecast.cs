using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using Ploco.Dialogs;
using Ploco.Helpers;
using Ploco.Models;

namespace Ploco.ViewModels
{
    public partial class MainViewModel
    {
        [RelayCommand]
        public async Task PlacementPrevisionnelAsync(LocomotiveModel loco)
        {
            if (loco == null) return;

            if (loco.IsForecastOrigin)
            {
                _dialogService.ShowMessage("Placement prévisionnel", "Cette locomotive est déjà en mode prévisionnel.");
                return;
            }

            if (loco.AssignedTrackId == null)
            {
                _dialogService.ShowWarning("Placement prévisionnel", "La locomotive doit être placée dans une tuile pour activer le mode prévisionnel.");
                return;
            }

            var targetTrack = _dialogService.ShowTargetTrackSelectionDialog(Tiles);
            if (targetTrack == null) return;

            loco.IsForecastOrigin = true;
            loco.ForecastTargetRollingLineTrackId = targetTrack.Id;

            var ghost = PrevisionnelLogicHelper.CreateGhostFrom(loco);
            ghost.AssignedTrackId = targetTrack.Id;
            targetTrack.Locomotives.Add(ghost);
            
            PlacementLogicHelper.EnsureTrackOffsets(targetTrack);

            await _repository.AddHistoryAsync("ForecastPlacement", $"Placement prévisionnel de la loco {loco.Number} vers {targetTrack.Name}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task AnnulerPrevisionnelAsync(LocomotiveModel loco)
        {
            if (loco == null) return;
            
            var originLoco = loco.IsForecastGhost 
                ? Locomotives.FirstOrDefault(l => l.Id == loco.ForecastSourceLocomotiveId || l.Number == loco.Number) 
                : loco;
                
            if (originLoco == null)
            {
                _dialogService.ShowError("Erreur", "Locomotive d'origine introuvable pour ce fantôme.");
                return;
            }
            if (!originLoco.IsForecastOrigin)
            {
                _dialogService.ShowError("Erreur", "La locomotive cible n'est pas marquée comme origine d'un prévisionnel.");
                return;
            }

            PrevisionnelLogicHelper.RemoveForecastGhostsFor(originLoco, Tiles);

            originLoco.IsForecastOrigin = false;
            originLoco.ForecastTargetRollingLineTrackId = null;

            await _repository.AddHistoryAsync("ForecastCancelled", $"Annulation du placement prévisionnel de la loco {originLoco.Number}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task ValiderPrevisionnelAsync(LocomotiveModel loco)
        {
            if (loco == null) return;
            
            var originLoco = loco.IsForecastGhost 
                ? Locomotives.FirstOrDefault(l => l.Id == loco.ForecastSourceLocomotiveId || l.Number == loco.Number) 
                : loco;
                
            if (originLoco == null)
            {
                _dialogService.ShowError("Erreur", "Locomotive d'origine introuvable pour ce fantôme.");
                return;
            }
            if (!originLoco.IsForecastOrigin)
            {
                _dialogService.ShowError("Erreur", "La locomotive cible n'est pas marquée comme origine d'un prévisionnel.");
                return;
            }

            var targetTrackId = originLoco.ForecastTargetRollingLineTrackId;
            if (targetTrackId == null)
            {
                _dialogService.ShowError("Erreur", "Ligne cible non définie.");
                return;
            }

            var targetTrack = Tiles.SelectMany(t => t.Tracks)
                .FirstOrDefault(t => t.Locomotives.Any(l => PrevisionnelLogicHelper.IsGhostOf(originLoco, l)));

            if (targetTrack == null)
            {
                _dialogService.ShowError("Erreur", "La ligne de roulement cible n'a pas été trouvée.");
                return;
            }

            PrevisionnelLogicHelper.RemoveForecastGhostsFor(originLoco, Tiles);

            var realLocosInTarget = targetTrack.Locomotives.Where(l => !l.IsForecastGhost).ToList();
            if (realLocosInTarget.Any() && targetTrack.Kind == TrackKind.RollingLine)
            {
                var existingLoco = realLocosInTarget.First();
                var action = _dialogService.ShowReplaceLocomotiveDialog(targetTrack.Name, existingLoco.Number.ToString());
                
                if (action == ReplaceAction.Cancel)
                {
                    var ghost = PrevisionnelLogicHelper.CreateGhostFrom(originLoco);
                    ghost.AssignedTrackId = targetTrack.Id;
                    targetTrack.Locomotives.Add(ghost);
                    return;
                }

                var originTrack = Tiles.SelectMany(t => t.Tracks).FirstOrDefault(t => t.Id == originLoco.AssignedTrackId);

                foreach (var realLoco in realLocosInTarget.ToList())
                {
                    targetTrack.Locomotives.Remove(realLoco);
                    realLoco.AssignedTrackId = null;
                    realLoco.AssignedTrackOffsetX = null;
                }

                if (action == ReplaceAction.Swap && originTrack != null)
                {
                    // Déplacer l'ancienne locomotive vers la voie d'origine de la nouvelle
                    MoveLocomotiveToTrack(existingLoco, originTrack, 0);
                }
            }

            originLoco.IsForecastOrigin = false;
            originLoco.ForecastTargetRollingLineTrackId = null;

            // Déplace la locomotive (MoveLocomotiveToTrack est déjà défini dans MainViewModel.Tiles.cs)
            MoveLocomotiveToTrack(originLoco, targetTrack, 0);

            // S'assure que les offsets sont recalculés correctement au cas où
            PlacementLogicHelper.EnsureTrackOffsets(targetTrack);

            await _repository.AddHistoryAsync("ForecastValidated", $"Validation du placement prévisionnel de {originLoco.Number} sur {targetTrack.Name}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }
    }
}
