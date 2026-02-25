using System;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
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

            var targetTrack = _dialogService.ShowRollingLineSelectionDialog(Tiles);
            if (targetTrack == null) return;

            loco.IsForecastOrigin = true;
            loco.ForecastTargetRollingLineTrackId = targetTrack.Id;

            var ghost = PrevisionnelLogicHelper.CreateGhostFrom(loco);
            ghost.AssignedTrackId = targetTrack.Id;
            targetTrack.Locomotives.Add(ghost);

            await _repository.AddHistoryAsync("ForecastPlacement", $"Placement prévisionnel de la loco {loco.Number} vers {targetTrack.Name}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task AnnulerPrevisionnelAsync(LocomotiveModel loco)
        {
            if (loco == null || !loco.IsForecastOrigin) return;

            PrevisionnelLogicHelper.RemoveForecastGhostsFor(loco, Tiles);

            loco.IsForecastOrigin = false;
            loco.ForecastTargetRollingLineTrackId = null;

            await _repository.AddHistoryAsync("ForecastCancelled", $"Annulation du placement prévisionnel de la loco {loco.Number}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }

        [RelayCommand]
        public async Task ValiderPrevisionnelAsync(LocomotiveModel loco)
        {
            if (loco == null || !loco.IsForecastOrigin) return;

            var targetTrackId = loco.ForecastTargetRollingLineTrackId;
            if (targetTrackId == null)
            {
                _dialogService.ShowError("Erreur", "Ligne de roulement cible non définie.");
                return;
            }

            var targetTrack = Tiles.SelectMany(t => t.Tracks)
                .FirstOrDefault(t => t.Locomotives.Any(l => PrevisionnelLogicHelper.IsGhostOf(loco, l)));

            if (targetTrack == null)
            {
                _dialogService.ShowError("Erreur", "La ligne de roulement cible n'a pas été trouvée.");
                return;
            }

            PrevisionnelLogicHelper.RemoveForecastGhostsFor(loco, Tiles);

            var realLocosInTarget = targetTrack.Locomotives.Where(l => !l.IsForecastGhost).ToList();
            if (realLocosInTarget.Any())
            {
                var prompt = $"La ligne {targetTrack.Name} est occupée par la locomotive {realLocosInTarget.First().Number}.\nVoulez-vous quand même valider le placement prévisionnel ? Cela remplacera la locomotive existante.";
                if (!_dialogService.ShowConfirmation("Ligne occupée", prompt))
                {
                    var ghost = PrevisionnelLogicHelper.CreateGhostFrom(loco);
                    ghost.AssignedTrackId = targetTrack.Id;
                    targetTrack.Locomotives.Add(ghost);
                    return;
                }

                foreach (var realLoco in realLocosInTarget.ToList())
                {
                    targetTrack.Locomotives.Remove(realLoco);
                    realLoco.AssignedTrackId = null;
                    realLoco.AssignedTrackOffsetX = null;
                }
            }

            loco.IsForecastOrigin = false;
            loco.ForecastTargetRollingLineTrackId = null;

            // Déplace la locomotive (MoveLocomotiveToTrack est déjà défini dans MainViewModel.Tiles.cs)
            MoveLocomotiveToTrack(loco, targetTrack, 0);

            await _repository.AddHistoryAsync("ForecastValidated", $"Validation du placement prévisionnel de {loco.Number} sur {targetTrack.Name}.");
            OnStatePersisted?.Invoke();
            OnWorkspaceChanged?.Invoke();
        }
    }
}
