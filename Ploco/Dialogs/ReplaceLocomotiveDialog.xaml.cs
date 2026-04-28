using System.Windows;

namespace Ploco.Dialogs
{
    public partial class ReplaceLocomotiveDialog : Window
    {
        public ReplaceAction SelectedAction { get; private set; } = ReplaceAction.Cancel;

        public ReplaceLocomotiveDialog(string targetTrackName, string existingLocoNumber)
        {
            InitializeComponent();
            MessageTextBlock.Text = $"La ligne {targetTrackName} est occupée par la locomotive {existingLocoNumber}.\nQue voulez-vous faire ?";
        }

        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ReplaceAction.Swap;
            DialogResult = true;
        }

        private void Pool_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ReplaceAction.Pool;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedAction = ReplaceAction.Cancel;
            DialogResult = false;
        }
    }
}
