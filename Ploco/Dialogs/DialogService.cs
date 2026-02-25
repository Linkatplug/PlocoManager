using System.Collections.Generic;
using System.Windows;
using Ploco.Models;

namespace Ploco.Dialogs
{
    public class DialogService : IDialogService
    {
        private Window Owner => Application.Current.MainWindow;

        public bool ShowConfirmation(string title, string message)
        {
            return MessageBox.Show(Owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public void ShowMessage(string title, string message)
        {
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowWarning(string title, string message)
        {
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowError(string title, string message)
        {
            MessageBox.Show(Owner, message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public (bool success, string result) ShowSimpleTextDialog(string title, string prompt, string defaultValue = "")
        {
            var dialog = new SimpleTextDialog(title, prompt, defaultValue) { Owner = Owner };
            var result = dialog.ShowDialog() == true;
            return (result, result ? dialog.ResponseText : string.Empty);
        }

        public (bool success, TileType? type, string name) ShowPlaceDialog()
        {
            var dialog = new PlaceDialog { Owner = Owner };
            var result = dialog.ShowDialog() == true;
            return (result, result ? dialog.SelectedType : null, result ? dialog.SelectedName : string.Empty);
        }

        public (bool success, LinePlaceDialogResult? result) ShowLinePlaceDialog(string defaultName)
        {
            var dialog = new LinePlaceDialog(defaultName) { Owner = Owner };
            var result = dialog.ShowDialog() == true;
            if (!result) return (false, null);

            var dialogResult = new LinePlaceDialogResult(
                dialog.PlaceName, dialog.TrackName, dialog.IssueReason,
                dialog.IsOnTrain, dialog.TrainNumber, dialog.StopTime,
                dialog.IsLocomotiveHs, dialog.HsLocomotiveNumbers,
                dialog.LocomotiveNumbers
            );
            return (true, dialogResult);
        }

        public (bool success, TrackModel? track) ShowLineTrackDialog()
        {
            var dialog = new LineTrackDialog { Owner = Owner };
            var result = dialog.ShowDialog() == true;
            return (result, result ? dialog.BuildTrack() : null);
        }

        public TrackModel? ShowRollingLineSelectionDialog(IEnumerable<TileModel> tiles)
        {
            var dialog = new RollingLineSelectionDialog(tiles) { Owner = Owner };
            return dialog.ShowDialog() == true ? dialog.SelectedTrack : null;
        }

        public List<int>? ShowRollingLineRangeDialog(int defaultCount)
        {
            var dialog = new RollingLineRangeDialog(defaultCount.ToString()) { Owner = Owner };
            return dialog.ShowDialog() == true ? dialog.SelectedNumbers : null;
        }
    }
}
