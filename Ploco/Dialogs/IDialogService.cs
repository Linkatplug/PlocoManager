using System.Collections.Generic;
using Ploco.Models;

namespace Ploco.Dialogs
{
    public record LinePlaceDialogResult(string PlaceName, string TrackName, string IssueReason, bool IsOnTrain, string TrainNumber, string StopTime, bool IsLocomotiveHs, string HsLocomotiveNumbers, string LocomotiveNumbers);

    public interface IDialogService
    {
        bool ShowConfirmation(string title, string message);
        void ShowMessage(string title, string message);
        void ShowWarning(string title, string message);
        void ShowError(string title, string message);

        (bool success, string result) ShowSimpleTextDialog(string title, string prompt, string defaultValue = "");
        (bool success, TileType? type, string name) ShowPlaceDialog();
        (bool success, LinePlaceDialogResult? result) ShowLinePlaceDialog(string defaultName);
        (bool success, TrackModel? track) ShowLineTrackDialog();
        TrackModel? ShowRollingLineSelectionDialog(IEnumerable<TileModel> tiles);
        List<int>? ShowRollingLineRangeDialog(int defaultCount);
    }
}
