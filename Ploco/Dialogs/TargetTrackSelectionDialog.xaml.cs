using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using Ploco.Models;

namespace Ploco.Dialogs
{
    public partial class TargetTrackSelectionDialog : Window
    {
        public class TargetTrackItem
        {
            public TrackModel Track { get; set; } = null!;
            public string TileName { get; set; } = string.Empty;
            public string TrackName { get; set; } = string.Empty;
            public bool IsOccupied { get; set; }
        }

        public TrackModel? SelectedTrack { get; private set; }

        public TargetTrackSelectionDialog(IEnumerable<TileModel> tiles)
        {
            InitializeComponent();

            var tracksList = new List<TargetTrackItem>();

            foreach (var tile in tiles)
            {
                foreach (var track in tile.Tracks)
                {
                    // Exclure la voie technique "Locomotives" (TrackKind.Main) uniquement pour Ligne de roulement
                    if (track.Kind == TrackKind.Main && tile.Type == TileType.RollingLine)
                    {
                        continue;
                    }

                    tracksList.Add(new TargetTrackItem
                    {
                        Track = track,
                        TileName = tile.DisplayTitle,
                        TrackName = string.IsNullOrWhiteSpace(track.Name) ? "Voie" : track.Name,
                        IsOccupied = track.Locomotives.Any()
                    });
                }
            }

            // Tri
            tracksList = tracksList.OrderBy(x => x.TileName).ThenBy(x => x.IsOccupied).ThenBy(x => x.TrackName).ToList();

            ICollectionView collectionView = CollectionViewSource.GetDefaultView(tracksList);
            collectionView.GroupDescriptions.Add(new PropertyGroupDescription("TileName"));

            TracksListBox.ItemsSource = collectionView;
            
            if (tracksList.Any())
            {
                TracksListBox.SelectedIndex = 0;
            }
        }

        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            if (TracksListBox.SelectedItem is TargetTrackItem item)
            {
                SelectedTrack = item.Track;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Veuillez sélectionner une voie.", "Validation", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void ListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TracksListBox.SelectedItem is TargetTrackItem item)
            {
                SelectedTrack = item.Track;
                DialogResult = true;
            }
        }
    }
}
