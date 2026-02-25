using System;
using System.Collections.Generic;
using Ploco.Models;

namespace Ploco.Data
{
    public interface IPlocoRepository
    {
        Task InitializeAsync();
        Task<PdfDocumentModel?> GetPdfDocumentAsync(string filePath, DateTime date);
        Task<PdfDocumentModel> SavePdfDocumentAsync(PdfDocumentModel document);
        Task<List<PdfTemplateCalibrationModel>> LoadTemplateCalibrationsAsync(string templateHash);
        Task SaveTemplateCalibrationAsync(PdfTemplateCalibrationModel calibration);
        Task<List<PdfPlacementModel>> LoadPlacementsAsync(int pdfDocumentId);
        Task SavePlacementAsync(PdfPlacementModel placement);
        Task DeletePlacementAsync(int placementId);
        Task<AppState> LoadStateAsync();
        Task SeedDefaultDataIfNeededAsync();
        Task SaveStateAsync(AppState state);
        Task AddHistoryAsync(string action, string details);
        Task<List<HistoryEntry>> LoadHistoryAsync();
        Task<Dictionary<string, int>> GetTableCountsAsync();
        Task<Dictionary<TrackKind, int>> GetTrackKindCountsAsync();
        Task ClearHistoryAsync();
        Task ResetOperationalStateAsync();
        Task CopyDatabaseToAsync(string destinationPath);
        bool ReplaceDatabaseWith(string sourcePath);
    }
}
