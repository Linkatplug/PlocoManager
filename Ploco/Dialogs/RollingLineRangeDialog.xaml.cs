using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;

namespace Ploco.Dialogs
{
    public partial class RollingLineRangeDialog : Window
    {
        private const int RollingLineStartNumber = 1101;
        public List<int> SelectedNumbers { get; private set; } = new List<int>();

        public RollingLineRangeDialog(string initialValue)
        {
            InitializeComponent();
            RangesText.Text = initialValue;
            RangesText.TextChanged += RangesText_TextChanged;
            RangesText.Focus();
            RangesText.SelectAll();
            UpdatePreview();
        }

        private void RangesText_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            var input = RangesText.Text.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                PreviewText.Text = "";
                return;
            }

            var (success, numbers, error) = ParseInput(input);
            if (success)
            {
                var count = numbers.Count;
                var min = numbers.Min();
                var max = numbers.Max();
                PreviewText.Text = $"✓ {count} ligne{(count > 1 ? "s" : "")} : {min} à {max}";
                PreviewText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                PreviewText.Text = $"✗ {error}";
                PreviewText.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private (bool success, List<int> numbers, string error) ParseInput(string input)
        {
            input = input.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                return (false, new List<int>(), "Veuillez saisir une valeur");
            }

            // Check if it's a simple number (backward compatibility)
            // Si le nombre est grand (ex: 5404), l'utilisateur souhaite créer spécifiquement cette ligne, pas 5404 lignes.
            if (int.TryParse(input, out var simpleCount) && simpleCount < 150)
            {
                if (simpleCount <= 0)
                {
                    return (false, new List<int>(), "Le nombre doit être supérieur à 0");
                }
                var numbers = Enumerable.Range(RollingLineStartNumber, simpleCount).ToList();
                return (true, numbers, string.Empty);
            }

            // Parse ranges like "1101-1112, 1113-1125"
            var result = new HashSet<int>();
            var parts = input.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (trimmed.Contains('-'))
                {
                    // Range format
                    var rangeParts = trimmed.Split('-');
                    if (rangeParts.Length != 2)
                    {
                        return (false, new List<int>(), $"Format invalide : '{trimmed}' (utilisez: début-fin)");
                    }

                    if (!int.TryParse(rangeParts[0].Trim(), out var start) || 
                        !int.TryParse(rangeParts[1].Trim(), out var end))
                    {
                        return (false, new List<int>(), $"Nombres invalides dans la plage : '{trimmed}'");
                    }

                    if (start > end)
                    {
                        return (false, new List<int>(), $"Plage invalide : {start} > {end}");
                    }

                    for (int i = start; i <= end; i++)
                    {
                        result.Add(i);
                    }
                }
                else
                {
                    // Single number
                    if (!int.TryParse(trimmed, out var number))
                    {
                        return (false, new List<int>(), $"Nombre invalide : '{trimmed}'");
                    }
                    result.Add(number);
                }
            }

            if (result.Count == 0)
            {
                return (false, new List<int>(), "Aucun numéro de ligne spécifié");
            }

            return (true, result.OrderBy(n => n).ToList(), string.Empty);
        }

        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            var input = RangesText.Text.Trim();
            var (success, numbers, error) = ParseInput(input);

            if (!success)
            {
                MessageBox.Show(error, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedNumbers = numbers;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
