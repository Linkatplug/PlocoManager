using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Docnet.Core;
using Docnet.Core.Models;
using Microsoft.Win32;
using PdfSharpCore.Drawing;
using PdfSharpCore.Pdf.IO;
using Ploco.Data;
using Ploco.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Ploco.Dialogs
{
    public partial class PlanningPdfWindow : Window
    {
        private readonly IPlocoRepository _repository;
        private readonly ObservableCollection<PdfPageViewModel> _pages = new();
        private PdfDocumentModel? _document;
        private readonly Dictionary<int, PdfTemplateCalibrationModel> _calibrations = new();
        private readonly Dictionary<int, (double Width, double Height)> _pdfPageSizes = new();
        private readonly CalibrationEditorViewModel _calibrationEditor = new();
        private double _zoom = 1.0;
        private bool _isDraggingToken;
        private PdfPlacementViewModel? _draggedPlacement;
        private Point _dragStart;
        private bool _isCalibrationMode;
        private CalibrationLineMode _calibrationLineMode = CalibrationLineMode.None;
        private CalibrationStep _calibrationStep = CalibrationStep.None;
        private bool _showCalibrationLines = true;

        public PlanningPdfWindow(IPlocoRepository repository)
        {
            InitializeComponent();
            _repository = repository;
            PdfPages.ItemsSource = _pages;
            DocumentDatePicker.SelectedDate = DateTime.Today;
            DataContext = new { Calibration = _calibrationEditor };
            UpdateZoomLabel();
        }

        private async void LoadPdf_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                Title = "Choisir un PDF"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await LoadPdfAsync(dialog.FileName);
        }

        private async Task LoadPdfAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                MessageBox.Show("Fichier introuvable.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _pages.Clear();
            _calibrations.Clear();
            _pdfPageSizes.Clear();
            _calibrationEditor.Reset();

            var date = DocumentDatePicker.SelectedDate ?? DateTime.Today;
            var templateHash = ComputeFileHash(filePath);
            var pageCount = GetPdfPageCount(filePath);
            _document = await _repository.GetPdfDocumentAsync(filePath, date) ?? new PdfDocumentModel
            {
                FilePath = filePath,
                DocumentDate = date,
                TemplateHash = templateHash,
                PageCount = pageCount
            };

            _document.TemplateHash = templateHash;
            _document.PageCount = pageCount;
            _document = await _repository.SavePdfDocumentAsync(_document);

            var calibrations = await _repository.LoadTemplateCalibrationsAsync(templateHash);
            foreach (var calibration in calibrations)
            {
                _calibrations[calibration.PageIndex] = calibration;
            }

            var extracted = ExtractCalibration(filePath);
            foreach (var calibration in extracted)
            {
                if (!_calibrations.ContainsKey(calibration.PageIndex))
                {
                    _calibrations[calibration.PageIndex] = calibration;
                    await _repository.SaveTemplateCalibrationAsync(calibration);
                }
            }

            RenderPages(filePath);
            await LoadPlacementsAsync();
            RefreshCalibrationLines();
            SetActiveCalibration(0);
            UpdateCalibrationUI();
        }

        private void RenderPages(string filePath)
        {
            var renderer = DocLib.Instance.GetDocReader(filePath, new PageDimensions(1440, 2030));
            for (var pageIndex = 0; pageIndex < renderer.GetPageCount(); pageIndex++)
            {
                using var pageReader = renderer.GetPageReader(pageIndex);
                var rawBytes = pageReader.GetImage();
                var (pdfWidth, pdfHeight) = _pdfPageSizes.TryGetValue(pageIndex, out var size) ? size : (pageReader.GetPageWidth(), pageReader.GetPageHeight());
                var page = new PdfPageViewModel(pageIndex, rawBytes, pageReader.GetPageWidth(), pageReader.GetPageHeight(), pdfWidth, pdfHeight);
                page.ApplyZoom(_zoom);
                _pages.Add(page);
            }
        }

        private async Task LoadPlacementsAsync()
        {
            if (_document == null)
            {
                return;
            }

            var placements = await _repository.LoadPlacementsAsync(_document.Id);
            foreach (var page in _pages)
            {
                page.Placements.Clear();
            }

            foreach (var placement in placements)
            {
                var page = _pages.FirstOrDefault(p => p.PageIndex == placement.PageIndex);
                if (page == null)
                {
                    continue;
                }

                var vm = PdfPlacementViewModel.FromModel(placement);
                ApplyPlacementPosition(page, vm);
                page.Placements.Add(vm);
            }
        }

        private void ApplyPlacementPosition(PdfPageViewModel page, PdfPlacementViewModel placement)
        {
            if (!_calibrations.TryGetValue(page.PageIndex, out var calibration))
            {
                placement.X = 10;
                placement.Y = 10;
                return;
            }

            var x = MapMinuteToX(calibration, page, placement.MinuteOfDay);
            var row = calibration.Rows.FirstOrDefault(r => string.Equals(r.RoulementId, placement.RoulementId, StringComparison.OrdinalIgnoreCase));
            var y = row != null ? MapPdfYToImage(page, row.YCenter) : 10;
            placement.X = x - placement.Width / 2;
            placement.Y = y - placement.Height / 2;
        }

        private void Overlay_DragOver(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(typeof(LocomotiveModel)) && !_isDraggingToken)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            e.Effects = DragDropEffects.Move;
            e.Handled = true;
        }

        private async void Overlay_Drop(object sender, DragEventArgs e)
        {
            if (sender is not Canvas canvas || canvas.DataContext is not PdfPageViewModel page)
            {
                return;
            }

            if (_document == null)
            {
                MessageBox.Show("Chargez un PDF avant d'ajouter des placements.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_calibrations.TryGetValue(page.PageIndex, out var calibration))
            {
                MessageBox.Show("Calibration manquante pour cette page. Lancez un recalibrage.", "Planning PDF",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SetActiveCalibration(page.PageIndex);

            var dropPoint = GetCanvasPoint(e.GetPosition(canvas));
            if (_isDraggingToken && _draggedPlacement != null)
            {
                await UpdatePlacementFromDropAsync(page, calibration, _draggedPlacement, dropPoint);
                _isDraggingToken = false;
                _draggedPlacement = null;
                return;
            }

            if (!e.Data.GetDataPresent(typeof(LocomotiveModel)))
            {
                return;
            }

            var loco = (LocomotiveModel)e.Data.GetData(typeof(LocomotiveModel))!;
            var placement = BuildPlacementFromLocomotive(loco, _document.Id, page.PageIndex);
            await UpdatePlacementFromDropAsync(page, calibration, placement, dropPoint);
            await SavePlacementAsync(page, placement);
        }

        private async Task UpdatePlacementFromDropAsync(PdfPageViewModel page, PdfTemplateCalibrationModel calibration, PdfPlacementViewModel placement, Point dropPoint)
        {
            var minute = MapXToMinute(calibration, page, dropPoint.X);
            var row = FindNearestRow(calibration, page, dropPoint.Y);
            if (row == null)
            {
                MessageBox.Show("Aucun roulement détecté pour ce drop.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            placement.MinuteOfDay = minute;
            placement.RoulementId = row.RoulementId;
            
            // Snap to the calibration line position
            var snappedX = MapMinuteToX(calibration, page, minute);
            var snappedY = MapPdfYToImage(page, row.YCenter);
            
            placement.X = snappedX - placement.Width / 2;
            placement.Y = snappedY - placement.Height / 2;

            if (placement.Id == 0)
            {
                page.Placements.Add(placement);
            }
            else
            {
                await SavePlacementAsync(page, placement);
            }
        }

        private async Task SavePlacementAsync(PdfPageViewModel page, PdfPlacementViewModel placement)
        {
            if (_document == null)
            {
                return;
            }

            var existing = _pages.SelectMany(p => p.Placements)
                .FirstOrDefault(p => p.LocNumber == placement.LocNumber && p.Id != placement.Id);
            if (existing != null)
            {
                var existingPage = _pages.FirstOrDefault(p => p.Placements.Contains(existing));
                existingPage?.Placements.Remove(existing);
                if (existing.Id > 0)
                {
                    await _repository.DeletePlacementAsync(existing.Id);
                }
            }

            placement.PdfDocumentId = _document.Id;
            var model = placement.ToModel();
            await _repository.SavePlacementAsync(model);
            placement.Id = model.Id;
            placement.UpdatedAt = model.UpdatedAt;
            placement.CreatedAt = model.CreatedAt;
        }

        private PdfPlacementViewModel BuildPlacementFromLocomotive(LocomotiveModel loco, int documentId, int pageIndex)
        {
            return new PdfPlacementViewModel
            {
                PdfDocumentId = documentId,
                PageIndex = pageIndex,
                LocNumber = loco.Number,
                Status = loco.Status,
                TractionPercent = loco.TractionPercent,
                HsReason = loco.HsReason
            };
        }

        private void Token_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is PdfPlacementViewModel placement)
            {
                _draggedPlacement = placement;
                _dragStart = e.GetPosition(PdfPages);
                _isDraggingToken = true;
                border.CaptureMouse();
            }
        }

        private void Token_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDraggingToken || _draggedPlacement == null || e.LeftButton != MouseButtonState.Pressed)
            {
                return;
            }

            if (sender is Border border)
            {
                DragDrop.DoDragDrop(border, _draggedPlacement, DragDropEffects.Move);
            }
        }

        private void Overlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isCalibrationMode || sender is not Canvas canvas || canvas.DataContext is not PdfPageViewModel page)
            {
                return;
            }

            SetActiveCalibration(page.PageIndex);
            if (!_calibrations.TryGetValue(page.PageIndex, out var calibration))
            {
                calibration = new PdfTemplateCalibrationModel
                {
                    TemplateHash = _document?.TemplateHash ?? string.Empty,
                    PageIndex = page.PageIndex,
                    XStart = 0,
                    XEnd = page.PageWidth
                };
                _calibrations[page.PageIndex] = calibration;
            }

            var point = GetCanvasPoint(e.GetPosition(canvas));
            var pdfX = MapImageXToPdf(page, point.X);
            var pdfY = MapImageYToPdf(page, point.Y);

            // Nouveau système de calibration avec lignes visuelles
            if (_calibrationLineMode == CalibrationLineMode.AddingHorizontal)
            {
                var input = new SimpleTextDialog("Ligne de roulement", "Identifiant (ex: @1101) :", string.Empty)
                {
                    Owner = this
                };
                if (input.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.ResponseText))
                {
                    var line = new PdfCalibrationLine
                    {
                        Type = CalibrationLineType.Horizontal,
                        Position = pdfY,
                        Label = input.ResponseText.Trim()
                    };
                    calibration.VisualLines.Add(line);

                    // Aussi ajouter dans Rows pour compatibilité
                    calibration.Rows.Add(new PdfTemplateRowMapping
                    {
                        RoulementId = line.Label,
                        YCenter = pdfY
                    });

                    var viewModel = new CalibrationLineViewModel
                    {
                        Type = CalibrationLineType.Horizontal,
                        PdfPosition = pdfY,
                        Label = line.Label
                    };
                    viewModel.UpdatePosition(page, _zoom);
                    page.CalibrationLines.Add(viewModel);

                    // We call it but do not await if we stay in Event Handler, however since this is Mouse event 
                    // we must change this. Wait I can just fire-and-forget _repository.SaveTemplateCalibrationAsync here.
                    _ = _repository.SaveTemplateCalibrationAsync(calibration);
                    _calibrationEditor.UpdateFromCalibration(calibration);
                }
                // Mode reste actif - l'utilisateur peut placer plusieurs lignes
            }
            else if (_calibrationLineMode == CalibrationLineMode.AddingVertical)
            {
                var input = new SimpleTextDialog("Marqueur d'heure", "Heure (ex: 06:00) ou minute (ex: 360) :", string.Empty)
                {
                    Owner = this
                };
                if (input.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.ResponseText))
                {
                    var text = input.ResponseText.Trim();
                    int minuteOfDay;
                    string label;

                    // Parse heure (HH:MM) ou minute directe
                    if (text.Contains(":"))
                    {
                        var parts = text.Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var hour) && int.TryParse(parts[1], out var minute))
                        {
                            minuteOfDay = hour * 60 + minute;
                            label = $"{hour:D2}:{minute:D2}";
                        }
                        else
                        {
                            MessageBox.Show("Format d'heure invalide. Utilisez HH:MM.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                    }
                    else if (int.TryParse(text, out minuteOfDay))
                    {
                        var hour = minuteOfDay / 60;
                        var minute = minuteOfDay % 60;
                        label = $"{hour:D2}:{minute:D2}";
                    }
                    else
                    {
                        MessageBox.Show("Format invalide. Utilisez HH:MM ou minutes.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    var line = new PdfCalibrationLine
                    {
                        Type = CalibrationLineType.Vertical,
                        Position = pdfX,
                        Label = label,
                        MinuteOfDay = minuteOfDay
                    };
                    calibration.VisualLines.Add(line);

                    // Mettre à jour XStart/XEnd pour compatibilité
                    var verticalLines = calibration.VisualLines.Where(l => l.Type == CalibrationLineType.Vertical && l.MinuteOfDay.HasValue).OrderBy(l => l.MinuteOfDay).ToList();
                    if (verticalLines.Any())
                    {
                        calibration.XStart = verticalLines.First().Position;
                        calibration.XEnd = verticalLines.Last().Position;
                    }

                    var viewModel = new CalibrationLineViewModel
                    {
                        Type = CalibrationLineType.Vertical,
                        PdfPosition = pdfX,
                        Label = label,
                        MinuteOfDay = minuteOfDay
                    };
                    viewModel.UpdatePosition(page, _zoom);
                    page.CalibrationLines.Add(viewModel);

                    _ = _repository.SaveTemplateCalibrationAsync(calibration);
                    _calibrationEditor.UpdateFromCalibration(calibration);
                }
                // Mode reste actif - l'utilisateur peut placer plusieurs lignes
            }
            // Ancien système de calibration (fallback)
            else if (_calibrationStep == CalibrationStep.SelectStart)
            {
                calibration.XStart = pdfX;
                _calibrationStep = CalibrationStep.SelectEnd;
                _calibrationEditor.UpdateFromCalibration(calibration);
                MessageBox.Show("Cliquez sur la position 24:00.", "Calibrage", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (_calibrationStep == CalibrationStep.SelectEnd)
            {
                calibration.XEnd = pdfX;
                _calibrationStep = CalibrationStep.SelectRows;
                _calibrationEditor.UpdateFromCalibration(calibration);
                MessageBox.Show("Cliquez sur une ligne et saisissez son identifiant.", "Calibrage", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (_calibrationStep == CalibrationStep.SelectRows)
            {
                var input = new SimpleTextDialog("Roulement", "Identifiant (ex: @1101) :", string.Empty)
                {
                    Owner = this
                };
                if (input.ShowDialog() == true && !string.IsNullOrWhiteSpace(input.ResponseText))
                {
                    calibration.Rows.Add(new PdfTemplateRowMapping
                    {
                        RoulementId = input.ResponseText.Trim(),
                        YCenter = pdfY
                    });
                    _calibrationEditor.UpdateFromCalibration(calibration);
                }
            }

            if (_calibrationStep != CalibrationStep.None || _calibrationLineMode != CalibrationLineMode.None)
            {
                _ = _repository.SaveTemplateCalibrationAsync(calibration);
            }
        }

        private void CalibrationLine_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isCalibrationMode)
            {
                return;
            }

            if (sender is Line line && line.DataContext is CalibrationLineViewModel lineViewModel)
            {
                // Ctrl+Click pour supprimer
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    var result = MessageBox.Show($"Supprimer la ligne {lineViewModel.Label}?", "Calibration", 
                        MessageBoxButton.YesNo, MessageBoxImage.Question);
                    
                    if (result == MessageBoxResult.Yes)
                    {
                        // Trouver la page et la calibration
                        foreach (var page in _pages)
                        {
                            if (page.CalibrationLines.Contains(lineViewModel))
                            {
                                page.CalibrationLines.Remove(lineViewModel);
                                
                                if (_calibrations.TryGetValue(page.PageIndex, out var calibration))
                                {
                                    // Supprimer de VisualLines
                                    var toRemove = calibration.VisualLines.FirstOrDefault(l => 
                                        l.Type == lineViewModel.Type && 
                                        Math.Abs(l.Position - lineViewModel.PdfPosition) < 0.1);
                                    if (toRemove != null)
                                    {
                                        calibration.VisualLines.Remove(toRemove);
                                    }

                                    // Supprimer de Rows si horizontal
                                    if (lineViewModel.Type == CalibrationLineType.Horizontal)
                                    {
                                        var rowToRemove = calibration.Rows.FirstOrDefault(r => r.RoulementId == lineViewModel.Label);
                                        if (rowToRemove != null)
                                        {
                                            calibration.Rows.Remove(rowToRemove);
                                        }
                                    }

                                    _ = _repository.SaveTemplateCalibrationAsync(calibration);
                                    _calibrationEditor.UpdateFromCalibration(calibration);
                                }
                                break;
                            }
                        }
                    }
                }
                e.Handled = true;
            }
        }

        private void Overlay_MouseMove(object sender, MouseEventArgs e)
        {
        }

        private void Overlay_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingToken = false;
            _draggedPlacement = null;
        }

        private async void EditPlacement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is PdfPlacementViewModel placement)
            {
                var dialog = new PdfPlacementDialog(placement) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    await SavePlacementAsync(_pages.First(p => p.PageIndex == placement.PageIndex), placement);
                }
            }
        }

        private async void DeletePlacement_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.DataContext is PdfPlacementViewModel placement)
            {
                var page = _pages.First(p => p.PageIndex == placement.PageIndex);
                page.Placements.Remove(placement);
                if (placement.Id > 0)
                {
                    await _repository.DeletePlacementAsync(placement.Id);
                }
            }
        }

        private void ExportPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                MessageBox.Show("Aucun PDF chargé.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "PDF (*.pdf)|*.pdf",
                FileName = $"planning_{_document.DocumentDate:yyyyMMdd}.pdf"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            ExportPdf(dialog.FileName);
        }

        private void ExportPdf(string outputPath)
        {
            if (_document == null)
            {
                return;
            }

            using var input = PdfReader.Open(_document.FilePath, PdfDocumentOpenMode.Modify);
            foreach (var page in _pages)
            {
                if (!_calibrations.TryGetValue(page.PageIndex, out var calibration))
                {
                    continue;
                }

                var pdfPage = input.Pages[page.PageIndex];
                using var gfx = XGraphics.FromPdfPage(pdfPage);
                foreach (var placement in page.Placements)
                {
                    var xPdf = MapMinuteToPdfX(calibration, placement.MinuteOfDay);
                    var row = calibration.Rows.FirstOrDefault(r => string.Equals(r.RoulementId, placement.RoulementId, StringComparison.OrdinalIgnoreCase));
                    if (row == null)
                    {
                        continue;
                    }

                    var yPdf = row.YCenter;
                    var rectWidth = 46;
                    var rectHeight = 18;
                    var x = xPdf - rectWidth / 2;
                    var y = pdfPage.Height - yPdf - rectHeight / 2;

                    var brush = placement.Status switch
                    {
                        LocomotiveStatus.HS => XBrushes.IndianRed,
                        LocomotiveStatus.ManqueTraction => XBrushes.Orange,
                        _ => XBrushes.SeaGreen
                    };
                    gfx.DrawRectangle(brush, x, y, rectWidth, rectHeight);
                    var font = new XFont("Arial", 8, XFontStyle.Bold);
                    gfx.DrawString(placement.LocNumber.ToString(), font, XBrushes.White,
                        new XRect(x, y, rectWidth, rectHeight), XStringFormats.CenterLeft);

                    var badge = placement.Status == LocomotiveStatus.ManqueTraction && placement.TractionPercent.HasValue
                        ? $"{placement.TractionPercent}%"
                        : placement.Status == LocomotiveStatus.HS ? "HS" : string.Empty;
                    if (!string.IsNullOrWhiteSpace(badge))
                    {
                        gfx.DrawString(badge, new XFont("Arial", 7, XFontStyle.Regular), XBrushes.White,
                            new XRect(x, y, rectWidth, rectHeight), XStringFormats.CenterRight);
                    }
                }
            }

            input.Save(outputPath);
            MessageBox.Show("Export terminé.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"planning_{_document.DocumentDate:yyyyMMdd}.csv"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            var builder = new StringBuilder();
            builder.AppendLine("pageIndex,roulementId,minuteOfDay,heure,locNumber,status,tractionPercent,onTrain,trainNumber,hsReason,comment");
            foreach (var placement in _pages.SelectMany(p => p.Placements))
            {
                var time = TimeSpan.FromMinutes(placement.MinuteOfDay);
                builder.AppendLine($"{placement.PageIndex},{placement.RoulementId},{placement.MinuteOfDay},{time:hh\\:mm},{placement.LocNumber},{placement.Status},{placement.TractionPercent},{placement.OnTrain},{placement.TrainNumber},{placement.HsReason},{placement.Comment}");
            }

            File.WriteAllText(dialog.FileName, builder.ToString(), Encoding.UTF8);
            MessageBox.Show("Export CSV terminé.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleCalibrationMode_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                MessageBox.Show("Chargez un PDF avant de calibrer.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isCalibrationMode = !_isCalibrationMode;
            _calibrationLineMode = CalibrationLineMode.None;
            
            UpdateCalibrationUI();
            
            MessageBox.Show(_isCalibrationMode
                    ? "Mode calibration activé. Utilisez les boutons pour ajouter des lignes horizontales (roulements) ou verticales (heures)."
                    : "Mode calibration désactivé.",
                "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddHorizontalLine_Click(object sender, RoutedEventArgs e)
        {
            if (_calibrationLineMode == CalibrationLineMode.AddingHorizontal)
            {
                // Désactiver le mode
                _calibrationLineMode = CalibrationLineMode.None;
            }
            else
            {
                // Activer le mode ajout ligne horizontale
                _calibrationLineMode = CalibrationLineMode.AddingHorizontal;
                MessageBox.Show("Mode ajout ligne horizontale activé.\nCliquez sur le PDF pour ajouter des lignes.\nRecliquez sur le bouton pour désactiver.", 
                    "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            UpdateCalibrationUI();
        }

        private void AddVerticalLine_Click(object sender, RoutedEventArgs e)
        {
            if (_calibrationLineMode == CalibrationLineMode.AddingVertical)
            {
                // Désactiver le mode
                _calibrationLineMode = CalibrationLineMode.None;
            }
            else
            {
                // Activer le mode ajout ligne verticale
                _calibrationLineMode = CalibrationLineMode.AddingVertical;
                MessageBox.Show("Mode ajout ligne verticale activé.\nCliquez sur le PDF pour ajouter des lignes.\nRecliquez sur le bouton pour désactiver.", 
                    "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            UpdateCalibrationUI();
        }

        private void SaveCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                FileName = $"calibration_{_document.TemplateHash.Substring(0, 8)}.json"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var calibrations = _calibrations.Values.ToList();
                var json = System.Text.Json.JsonSerializer.Serialize(calibrations, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(dialog.FileName, json);
                MessageBox.Show("Calibration sauvegardée avec succès.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde: {ex.Message}", "Calibration", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void LoadCalibration_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON (*.json)|*.json",
                Title = "Charger une calibration"
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var calibrations = System.Text.Json.JsonSerializer.Deserialize<List<PdfTemplateCalibrationModel>>(json);
                
                if (calibrations == null || !calibrations.Any())
                {
                    MessageBox.Show("Fichier de calibration invalide.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var calibration in calibrations)
                {
                    if (_document != null)
                    {
                        calibration.TemplateHash = _document.TemplateHash;
                    }
                    _calibrations[calibration.PageIndex] = calibration;
                    await _repository.SaveTemplateCalibrationAsync(calibration);
                }

                // Recharger les lignes visuelles
                RefreshCalibrationLines();
                
                MessageBox.Show("Calibration chargée avec succès.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement: {ex.Message}", "Calibration", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ResetCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            var result = MessageBox.Show(
                "Voulez-vous vraiment supprimer toutes les lignes de calibration de toutes les pages ?\n\nCette action est irréversible.",
                "Réinitialiser calibration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                // Supprimer toutes les calibrations
                var pagesToReset = _calibrations.Keys.ToList();
                foreach (var pageIndex in pagesToReset)
                {
                    var calibration = _calibrations[pageIndex];
                    calibration.VisualLines.Clear();
                    calibration.Rows.Clear();
                    calibration.XStart = 0;
                    calibration.XEnd = 0;
                    await _repository.SaveTemplateCalibrationAsync(calibration);
                }

                // Rafraîchir l'affichage
                RefreshCalibrationLines();
                
                MessageBox.Show("Calibration réinitialisée avec succès.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la réinitialisation: {ex.Message}", "Calibration", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ToggleCalibrationLinesVisibility_Click(object sender, RoutedEventArgs e)
        {
            _showCalibrationLines = !_showCalibrationLines;
            
            // Mettre à jour la visibilité de toutes les lignes
            foreach (var page in _pages)
            {
                foreach (var line in page.CalibrationLines)
                {
                    line.IsVisible = _showCalibrationLines;
                }
            }
            
            // Mettre à jour le bouton
            ToggleLinesButton.Content = _showCalibrationLines ? "👁️ Lignes" : "🚫 Lignes";
            ToggleLinesButton.FontWeight = _showCalibrationLines ? FontWeights.Normal : FontWeights.Bold;
        }

        private void UpdateCalibrationUI()
        {
            AddHorizontalButton.IsEnabled = _isCalibrationMode;
            AddVerticalButton.IsEnabled = _isCalibrationMode;
            SaveCalibrationButton.IsEnabled = _isCalibrationMode && _calibrations.Any();
            ResetCalibrationButton.IsEnabled = _isCalibrationMode && _calibrations.Any();
            
            CalibrationModeButton.FontWeight = _isCalibrationMode ? FontWeights.Bold : FontWeights.Normal;
            
            // Mise en évidence des boutons actifs
            AddHorizontalButton.FontWeight = _calibrationLineMode == CalibrationLineMode.AddingHorizontal ? FontWeights.Bold : FontWeights.Normal;
            AddHorizontalButton.Background = _calibrationLineMode == CalibrationLineMode.AddingHorizontal ? Brushes.LightBlue : null;
            
            AddVerticalButton.FontWeight = _calibrationLineMode == CalibrationLineMode.AddingVertical ? FontWeights.Bold : FontWeights.Normal;
            AddVerticalButton.Background = _calibrationLineMode == CalibrationLineMode.AddingVertical ? Brushes.LightCoral : null;
        }

        private void RefreshCalibrationLines()
        {
            foreach (var page in _pages)
            {
                page.CalibrationLines.Clear();
                
                if (!_calibrations.TryGetValue(page.PageIndex, out var calibration))
                {
                    continue;
                }

                // Ajouter les lignes depuis VisualLines
                foreach (var line in calibration.VisualLines)
                {
                    var viewModel = new CalibrationLineViewModel
                    {
                        Type = line.Type,
                        PdfPosition = line.Position,
                        Label = line.Label,
                        MinuteOfDay = line.MinuteOfDay
                    };
                    viewModel.UpdatePosition(page, _zoom);
                    page.CalibrationLines.Add(viewModel);
                }

                // Si pas de VisualLines, générer depuis l'ancien système (Rows)
                if (!calibration.VisualLines.Any() && calibration.Rows.Any())
                {
                    foreach (var row in calibration.Rows)
                    {
                        var viewModel = new CalibrationLineViewModel
                        {
                            Type = CalibrationLineType.Horizontal,
                            PdfPosition = row.YCenter,
                            Label = row.RoulementId
                        };
                        viewModel.UpdatePosition(page, _zoom);
                        page.CalibrationLines.Add(viewModel);
                    }
                }

                // Ajouter lignes verticales si XStart/XEnd définis
                if (calibration.XStart > 0 && calibration.XEnd > calibration.XStart && !calibration.VisualLines.Any(l => l.Type == CalibrationLineType.Vertical))
                {
                    // Ligne 00:00
                    var startLine = new CalibrationLineViewModel
                    {
                        Type = CalibrationLineType.Vertical,
                        PdfPosition = calibration.XStart,
                        Label = "00:00",
                        MinuteOfDay = 0
                    };
                    startLine.UpdatePosition(page, _zoom);
                    page.CalibrationLines.Add(startLine);

                    // Ligne 24:00
                    var endLine = new CalibrationLineViewModel
                    {
                        Type = CalibrationLineType.Vertical,
                        PdfPosition = calibration.XEnd,
                        Label = "24:00",
                        MinuteOfDay = 1440
                    };
                    endLine.UpdatePosition(page, _zoom);
                    page.CalibrationLines.Add(endLine);
                }
            }
        }

        private void Recalibrate_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                MessageBox.Show("Chargez un PDF avant de calibrer.", "Planning PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _isCalibrationMode = !_isCalibrationMode;
            _calibrationStep = _isCalibrationMode ? CalibrationStep.SelectStart : CalibrationStep.None;
            _calibrationEditor.HelpText = _isCalibrationMode
                ? "Cliquez sur la page pour définir 00:00, puis 24:00, puis plusieurs lignes."
                : "Mode calibrage désactivé.";
            MessageBox.Show(_isCalibrationMode
                    ? "Mode calibrage activé. Cliquez sur la position 00:00."
                    : "Mode calibrage désactivé.",
                "Calibrage", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void ApplyCalibration_Click(object sender, RoutedEventArgs e)
        {
            if (_document == null)
            {
                return;
            }

            if (!_calibrationEditor.TryBuildCalibration(out var calibration))
            {
                MessageBox.Show("Calibration invalide. Vérifiez les valeurs X.", "Calibration",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _calibrations[calibration.PageIndex] = calibration;
            await _repository.SaveTemplateCalibrationAsync(calibration);
            MessageBox.Show("Calibration enregistrée.", "Calibration", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AddCalibrationRow_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SimpleTextDialog("Ajouter un roulement", "Identifiant (ex: @1101) :", string.Empty) { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.ResponseText))
            {
                _calibrationEditor.Rows.Add(new PdfTemplateRowMapping
                {
                    RoulementId = dialog.ResponseText.Trim(),
                    YCenter = 0
                });
            }
        }

        private void RemoveCalibrationRow_Click(object sender, RoutedEventArgs e)
        {
            if (_calibrationEditor.Rows.Any())
            {
                _calibrationEditor.Rows.RemoveAt(_calibrationEditor.Rows.Count - 1);
            }
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _zoom = e.NewValue;
            UpdateZoomLabel();
            foreach (var page in _pages)
            {
                page.ApplyZoom(_zoom);
            }
        }

        private void UpdateZoomLabel()
        {
            if (ZoomLabel != null)
            {
                ZoomLabel.Text = $"{_zoom:P0}";
            }
        }

        private void SetActiveCalibration(int pageIndex)
        {
            if (_document == null)
            {
                return;
            }

            if (!_calibrations.TryGetValue(pageIndex, out var calibration))
            {
                calibration = new PdfTemplateCalibrationModel
                {
                    TemplateHash = _document.TemplateHash,
                    PageIndex = pageIndex,
                    XStart = 0,
                    XEnd = 0
                };
            }

            _calibrationEditor.UpdateFromCalibration(calibration);
        }

        private static int GetPdfPageCount(string filePath)
        {
            using var pdf = PdfDocument.Open(filePath);
            return pdf.NumberOfPages;
        }

        private static string ComputeFileHash(string filePath)
        {
            using var stream = File.OpenRead(filePath);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        private List<PdfTemplateCalibrationModel> ExtractCalibration(string filePath)
        {
            var results = new List<PdfTemplateCalibrationModel>();
            using var pdf = PdfDocument.Open(filePath);
            var rowRegex = new Regex(@"@?\d{4}", RegexOptions.Compiled);
            var timeRegex = new Regex(@"^(?:[01]?\d|2[0-3])$", RegexOptions.Compiled);

            for (var pageIndex = 0; pageIndex < pdf.NumberOfPages; pageIndex++)
            {
                var page = pdf.GetPage(pageIndex + 1);
                _pdfPageSizes[pageIndex] = (page.Width, page.Height);
                var words = page.GetWords().ToList();
                var rowCandidates = words
                    .Where(w => rowRegex.IsMatch(w.Text))
                    .Select(w => new
                    {
                        Id = w.Text.StartsWith("@") ? w.Text : $"@{w.Text}",
                        Y = (w.BoundingBox.Bottom + w.BoundingBox.Top) / 2
                    })
                    .GroupBy(item => item.Id)
                    .Select(group => new PdfTemplateRowMapping
                    {
                        RoulementId = group.Key,
                        YCenter = group.Average(item => item.Y)
                    })
                    .ToList();

                var timeCandidates = words
                    .Where(w => timeRegex.IsMatch(w.Text))
                    .Select(w => new { Hour = int.Parse(w.Text, CultureInfo.InvariantCulture), X = (w.BoundingBox.Left + w.BoundingBox.Right) / 2 })
                    .GroupBy(item => item.Hour)
                    .Select(group => new { Hour = group.Key, X = group.Average(item => item.X) })
                    .ToList();

                if (!rowCandidates.Any() || timeCandidates.Count < 2)
                {
                    continue;
                }

                var xStart = timeCandidates.OrderBy(t => t.Hour).First().X;
                var xEnd = timeCandidates.OrderBy(t => t.Hour).Last().X;
                results.Add(new PdfTemplateCalibrationModel
                {
                    TemplateHash = _document?.TemplateHash ?? string.Empty,
                    PageIndex = pageIndex,
                    XStart = xStart,
                    XEnd = xEnd,
                    Rows = rowCandidates
                });
            }

            return results;
        }

        private static int MapXToMinute(PdfTemplateCalibrationModel calibration, PdfPageViewModel page, double xImage)
        {
            var pdfX = MapImageXToPdf(page, xImage);
            
            // Utiliser les lignes verticales si disponibles
            var verticalLines = calibration.VisualLines
                .Where(l => l.Type == CalibrationLineType.Vertical && l.MinuteOfDay.HasValue)
                .OrderBy(l => l.MinuteOfDay)
                .ToList();

            if (verticalLines.Count >= 2)
            {
                // Interpolation entre les lignes verticales
                for (int i = 0; i < verticalLines.Count - 1; i++)
                {
                    var line1 = verticalLines[i];
                    var line2 = verticalLines[i + 1];
                    
                    if (pdfX >= line1.Position && pdfX <= line2.Position)
                    {
                        var lineRatio = (pdfX - line1.Position) / (line2.Position - line1.Position);
                        var minuteRange = line2.MinuteOfDay!.Value - line1.MinuteOfDay!.Value;
                        return (int)Math.Round(line1.MinuteOfDay!.Value + lineRatio * minuteRange);
                    }
                }
                
                // Avant la première ligne
                if (pdfX < verticalLines[0].Position)
                {
                    return 0;
                }
                
                // Après la dernière ligne
                if (pdfX > verticalLines[^1].Position)
                {
                    return 1440;
                }
            }
            
            // Fallback: ancien système avec XStart/XEnd
            var fallbackRatio = (pdfX - calibration.XStart) / (calibration.XEnd - calibration.XStart);
            fallbackRatio = Math.Max(0, Math.Min(1, fallbackRatio));
            return (int)Math.Round(fallbackRatio * 1440);
        }

        private static double MapMinuteToX(PdfTemplateCalibrationModel calibration, PdfPageViewModel page, int minute)
        {
            var pdfX = MapMinuteToPdfX(calibration, minute);
            return MapPdfXToImage(page, pdfX);
        }

        private static double MapMinuteToPdfX(PdfTemplateCalibrationModel calibration, int minute)
        {
            // Utiliser les lignes verticales si disponibles
            var verticalLines = calibration.VisualLines
                .Where(l => l.Type == CalibrationLineType.Vertical && l.MinuteOfDay.HasValue)
                .OrderBy(l => l.MinuteOfDay)
                .ToList();

            if (verticalLines.Count >= 2)
            {
                // Interpolation entre les lignes verticales
                for (int i = 0; i < verticalLines.Count - 1; i++)
                {
                    var line1 = verticalLines[i];
                    var line2 = verticalLines[i + 1];
                    
                    if (minute >= line1.MinuteOfDay && minute <= line2.MinuteOfDay)
                    {
                        var minuteRange = line2.MinuteOfDay!.Value - line1.MinuteOfDay!.Value;
                        var lineRatio = (minute - line1.MinuteOfDay!.Value) / (double)minuteRange;
                        return line1.Position + lineRatio * (line2.Position - line1.Position);
                    }
                }
                
                // Avant la première ligne
                if (minute < verticalLines[0].MinuteOfDay)
                {
                    return verticalLines[0].Position;
                }
                
                // Après la dernière ligne
                if (minute > verticalLines[^1].MinuteOfDay)
                {
                    return verticalLines[^1].Position;
                }
            }
            
            // Fallback: ancien système avec XStart/XEnd
            var fallbackRatio = minute / 1440.0;
            return calibration.XStart + fallbackRatio * (calibration.XEnd - calibration.XStart);
        }

        private static PdfTemplateRowMapping? FindNearestRow(PdfTemplateCalibrationModel calibration, PdfPageViewModel page, double yImage)
        {
            var pdfY = MapImageYToPdf(page, yImage);
            return calibration.Rows.OrderBy(r => Math.Abs(r.YCenter - pdfY)).FirstOrDefault();
        }

        private static double MapPdfXToImage(PdfPageViewModel page, double pdfX)
        {
            return pdfX * page.ScaleX;
        }

        private static double MapPdfYToImage(PdfPageViewModel page, double pdfY)
        {
            return (page.PdfHeight - pdfY) * page.ScaleY;
        }

        private static double MapImageXToPdf(PdfPageViewModel page, double imageX)
        {
            return imageX / page.ScaleX;
        }

        private static double MapImageYToPdf(PdfPageViewModel page, double imageY)
        {
            return page.PdfHeight - (imageY / page.ScaleY);
        }

        private Point GetCanvasPoint(Point position)
        {
            return new Point(position.X / _zoom, position.Y / _zoom);
        }

        public sealed class PdfPageViewModel
        {
            public int PageIndex { get; }
            public BitmapSource PageImage { get; }
            public double PdfWidth { get; }
            public double PdfHeight { get; }
            public double ScaleX { get; private set; }
            public double ScaleY { get; private set; }
            public double PageWidth { get; private set; }
            public double PageHeight { get; private set; }
            public ObservableCollection<PdfPlacementViewModel> Placements { get; } = new();
            public ObservableCollection<CalibrationLineViewModel> CalibrationLines { get; } = new();

            public PdfPageViewModel(int pageIndex, byte[] imageBytes, int width, int height, double pdfWidth, double pdfHeight)
            {
                PageIndex = pageIndex;
                PdfWidth = pdfWidth;
                PdfHeight = pdfHeight;
                PageImage = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, imageBytes, width * 4);
                ApplyZoom(1.0);
            }

            public void ApplyZoom(double zoom)
            {
                PageWidth = PageImage.PixelWidth * zoom;
                PageHeight = PageImage.PixelHeight * zoom;
                ScaleX = PageWidth / PdfWidth;
                ScaleY = PageHeight / PdfHeight;
                
                // Update calibration lines positions for zoom
                foreach (var line in CalibrationLines)
                {
                    line.UpdatePosition(this, zoom);
                }
            }
        }

        public class CalibrationLineViewModel : System.ComponentModel.INotifyPropertyChanged
        {
            private double _x1;
            private double _y1;
            private double _x2;
            private double _y2;
            private string _label = string.Empty;
            private bool _isVisible = true;

            public CalibrationLineType Type { get; set; }
            public double PdfPosition { get; set; }  // Position en coordonnées PDF
            public string Label
            {
                get => _label;
                set
                {
                    if (_label != value)
                    {
                        _label = value;
                        OnPropertyChanged(nameof(Label));
                    }
                }
            }
            public int? MinuteOfDay { get; set; }

            public bool IsVisible
            {
                get => _isVisible;
                set
                {
                    if (_isVisible != value)
                    {
                        _isVisible = value;
                        OnPropertyChanged(nameof(IsVisible));
                        OnPropertyChanged(nameof(LineVisibility));
                    }
                }
            }

            public Visibility LineVisibility => _isVisible ? Visibility.Visible : Visibility.Collapsed;

            public double X1
            {
                get => _x1;
                set
                {
                    if (Math.Abs(_x1 - value) > 0.1)
                    {
                        _x1 = value;
                        OnPropertyChanged(nameof(X1));
                    }
                }
            }

            public double Y1
            {
                get => _y1;
                set
                {
                    if (Math.Abs(_y1 - value) > 0.1)
                    {
                        _y1 = value;
                        OnPropertyChanged(nameof(Y1));
                    }
                }
            }

            public double X2
            {
                get => _x2;
                set
                {
                    if (Math.Abs(_x2 - value) > 0.1)
                    {
                        _x2 = value;
                        OnPropertyChanged(nameof(X2));
                    }
                }
            }

            public double Y2
            {
                get => _y2;
                set
                {
                    if (Math.Abs(_y2 - value) > 0.1)
                    {
                        _y2 = value;
                        OnPropertyChanged(nameof(Y2));
                    }
                }
            }

            public Brush LineBrush => Type == CalibrationLineType.Horizontal 
                ? Brushes.DodgerBlue 
                : Brushes.OrangeRed;

            public double LabelX => Type == CalibrationLineType.Vertical ? X1 - 30 : X1 + 5;
            public double LabelY => Type == CalibrationLineType.Vertical ? Y1 + 5 : Y1 - 20;

            public void UpdatePosition(PdfPageViewModel page, double zoom)
            {
                if (Type == CalibrationLineType.Horizontal)
                {
                    // Ligne horizontale pour roulement
                    var y = MapPdfYToImage(page, PdfPosition);
                    X1 = 0;
                    Y1 = y;
                    X2 = page.PageWidth;
                    Y2 = y;
                }
                else
                {
                    // Ligne verticale pour heure
                    var x = MapPdfXToImage(page, PdfPosition);
                    X1 = x;
                    Y1 = 0;
                    X2 = x;
                    Y2 = page.PageHeight;
                }
                OnPropertyChanged(nameof(LabelX));
                OnPropertyChanged(nameof(LabelY));
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }

        public class PdfPlacementViewModel
        {
            public int Id { get; set; }
            public int PdfDocumentId { get; set; }
            public int PageIndex { get; set; }
            public string RoulementId { get; set; } = string.Empty;
            public int MinuteOfDay { get; set; }
            public int LocNumber { get; set; }
            public LocomotiveStatus Status { get; set; }
            public int? TractionPercent { get; set; }
            public int? MotorsHsCount { get; set; }
            public string? HsReason { get; set; }
            public bool OnTrain { get; set; }
            public string? TrainNumber { get; set; }
            public string? TrainStopTime { get; set; }
            public string? Comment { get; set; }
            public DateTime CreatedAt { get; set; }
            public DateTime UpdatedAt { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double Width => 48;
            public double Height => 24;
            public Brush StatusBrush => Status switch
            {
                LocomotiveStatus.HS => Brushes.IndianRed,
                LocomotiveStatus.ManqueTraction => Brushes.Orange,
                _ => Brushes.SeaGreen
            };

            public string BadgeText
            {
                get
                {
                    if (Status == LocomotiveStatus.ManqueTraction && TractionPercent.HasValue)
                    {
                        return $"{TractionPercent}%";
                    }

                    return Status == LocomotiveStatus.HS ? "HS" : string.Empty;
                }
            }

            public PdfPlacementModel ToModel()
            {
                return new PdfPlacementModel
                {
                    Id = Id,
                    PdfDocumentId = PdfDocumentId,
                    PageIndex = PageIndex,
                    RoulementId = RoulementId,
                    MinuteOfDay = MinuteOfDay,
                    LocNumber = LocNumber,
                    Status = Status,
                    TractionPercent = TractionPercent,
                    MotorsHsCount = MotorsHsCount,
                    HsReason = HsReason,
                    OnTrain = OnTrain,
                    TrainNumber = TrainNumber,
                    TrainStopTime = TrainStopTime,
                    Comment = Comment,
                    CreatedAt = CreatedAt,
                    UpdatedAt = UpdatedAt
                };
            }

            public static PdfPlacementViewModel FromModel(PdfPlacementModel model)
            {
                return new PdfPlacementViewModel
                {
                    Id = model.Id,
                    PdfDocumentId = model.PdfDocumentId,
                    PageIndex = model.PageIndex,
                    RoulementId = model.RoulementId,
                    MinuteOfDay = model.MinuteOfDay,
                    LocNumber = model.LocNumber,
                    Status = model.Status,
                    TractionPercent = model.TractionPercent,
                    MotorsHsCount = model.MotorsHsCount,
                    HsReason = model.HsReason,
                    OnTrain = model.OnTrain,
                    TrainNumber = model.TrainNumber,
                    TrainStopTime = model.TrainStopTime,
                    Comment = model.Comment,
                    CreatedAt = model.CreatedAt,
                    UpdatedAt = model.UpdatedAt
                };
            }
        }

        private enum CalibrationStep
        {
            None,
            SelectStart,
            SelectEnd,
            SelectRows
        }

        private enum CalibrationLineMode
        {
            None,
            AddingHorizontal,
            AddingVertical
        }

        private sealed class CalibrationEditorViewModel : System.ComponentModel.INotifyPropertyChanged
        {
            private string _pageLabel = "Aucune";
            private string _xStartText = string.Empty;
            private string _xEndText = string.Empty;
            private string _helpText = "Sélectionnez une page pour éditer la calibration.";
            private int _pageIndex;
            private string _templateHash = string.Empty;

            public ObservableCollection<PdfTemplateRowMapping> Rows { get; } = new();

            public string PageLabel
            {
                get => _pageLabel;
                set
                {
                    if (_pageLabel != value)
                    {
                        _pageLabel = value;
                        OnPropertyChanged(nameof(PageLabel));
                    }
                }
            }

            public string XStartText
            {
                get => _xStartText;
                set
                {
                    if (_xStartText != value)
                    {
                        _xStartText = value;
                        OnPropertyChanged(nameof(XStartText));
                    }
                }
            }

            public string XEndText
            {
                get => _xEndText;
                set
                {
                    if (_xEndText != value)
                    {
                        _xEndText = value;
                        OnPropertyChanged(nameof(XEndText));
                    }
                }
            }

            public string HelpText
            {
                get => _helpText;
                set
                {
                    if (_helpText != value)
                    {
                        _helpText = value;
                        OnPropertyChanged(nameof(HelpText));
                    }
                }
            }

            public void UpdateFromCalibration(PdfTemplateCalibrationModel calibration)
            {
                _pageIndex = calibration.PageIndex;
                _templateHash = calibration.TemplateHash;
                PageLabel = $"Page {calibration.PageIndex + 1}";
                XStartText = calibration.XStart.ToString(CultureInfo.InvariantCulture);
                XEndText = calibration.XEnd.ToString(CultureInfo.InvariantCulture);
                Rows.Clear();
                foreach (var row in calibration.Rows.OrderBy(r => r.YCenter))
                {
                    Rows.Add(new PdfTemplateRowMapping
                    {
                        RoulementId = row.RoulementId,
                        YCenter = row.YCenter
                    });
                }
            }

            public bool TryBuildCalibration(out PdfTemplateCalibrationModel calibration)
            {
                calibration = new PdfTemplateCalibrationModel
                {
                    TemplateHash = _templateHash,
                    PageIndex = _pageIndex
                };

                if (!double.TryParse(XStartText, NumberStyles.Float, CultureInfo.InvariantCulture, out var xStart))
                {
                    return false;
                }

                if (!double.TryParse(XEndText, NumberStyles.Float, CultureInfo.InvariantCulture, out var xEnd))
                {
                    return false;
                }

                calibration.XStart = xStart;
                calibration.XEnd = xEnd;
                calibration.Rows = Rows
                    .Where(r => !string.IsNullOrWhiteSpace(r.RoulementId))
                    .Select(r => new PdfTemplateRowMapping
                    {
                        RoulementId = r.RoulementId,
                        YCenter = r.YCenter
                    })
                    .ToList();
                return true;
            }

            public void Reset()
            {
                _pageIndex = 0;
                _templateHash = string.Empty;
                PageLabel = "Aucune";
                XStartText = string.Empty;
                XEndText = string.Empty;
                Rows.Clear();
                HelpText = "Sélectionnez une page pour éditer la calibration.";
            }

            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

            private void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
