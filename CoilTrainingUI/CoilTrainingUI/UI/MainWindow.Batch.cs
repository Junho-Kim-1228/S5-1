using CoilTrainingUI.Models;
using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using IOPath = System.IO.Path;

namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private void ImportBatch_Click(object sender, RoutedEventArgs e)
        {
            string projectRoot = FindProjectRoot("capstone_design");
            string inboxRoot = GetTrainingInboxRoot();

            string initialPath = GetInitialImportBatchFolder(inboxRoot, projectRoot);
            var selectedBatchFolder = TrySelectFolder("Import batch folder", initialPath);
            if (string.IsNullOrWhiteSpace(selectedBatchFolder))
                return;

            RememberImportBatchFolder(selectedBatchFolder);

            try
            {
                string batchToLoad;
                if (IsPathUnderRoot(selectedBatchFolder, inboxRoot))
                {
                    var validation = BatchFolderValidationService.Validate(selectedBatchFolder);
                    if (!validation.IsValid)
                    {
                        MessageBox.Show(
                            validation.Message,
                            "Import Batch",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                        return;
                    }

                    batchToLoad = selectedBatchFolder;
                }
                else
                {
                    var validation = BatchFolderValidationService.Validate(selectedBatchFolder);
                    if (!validation.IsValid)
                    {
                        MessageBox.Show(
                            validation.Message,
                            "Import Batch",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                        return;
                    }

                    var imported = _inferenceBatchImportService.Import(
                        selectedBatchFolder,
                        projectRoot,
                        inboxRoot);
                    batchToLoad = imported.ImportedPath;
                }

                RefreshAllImagesFromTrainingInbox(
                    preferredImagePath: null,
                    preferredBatchRoot: batchToLoad
                );

                MessageBox.Show(
                    $"Batch loaded\n{batchToLoad}\n총 image 수: {_images.Count}",
                    "Import Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Batch import/load 실패: {ex.Message}",
                    "Import Batch",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private void CreateBatchFromFolder_Click(object sender, RoutedEventArgs e)
        {
            string projectRoot = FindProjectRoot("capstone_design");
            string inboxRoot = GetTrainingInboxRoot();
            string? previousImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;

            string initialPath = GetInitialProcessedFolder(inboxRoot, projectRoot);

            var srcFolder = TrySelectFolder("Select processed image folder (*.bmp)", initialPath);
            if (string.IsNullOrWhiteSpace(srcFolder))
                return;

            RememberProcessedFolder(srcFolder);

            string rawInitialPath = GetInitialRawFolder(srcFolder, inboxRoot, projectRoot);
            var rawFolder = TrySelectFolder("Select RAW image folder (*.bmp)", rawInitialPath);
            if (string.IsNullOrWhiteSpace(rawFolder))
                return;

            RememberRawFolder(rawFolder);

            try
            {
                var created = CreateSeedBatchFromFolder(srcFolder, rawFolder, inboxRoot);

                MessageBox.Show(
                    $"Batch 생성 완료\n{created.BatchPath}\n총 item 수: {created.ItemCount}",
                    "Create Batch from Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                var shouldLoad = MessageBox.Show(
                    "방금 생성한 배치를 바로 로드할까요?",
                    "Create Batch from Folder",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (shouldLoad == MessageBoxResult.Yes)
                {
                    RefreshAllImagesFromTrainingInbox(
                        preferredImagePath: null,
                        preferredBatchRoot: created.BatchPath
                    );
                }
                else
                {
                    RefreshAllImagesFromTrainingInbox(
                        preferredImagePath: previousImagePath,
                        preferredBatchRoot: null
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Create Batch 실패: {ex.Message}",
                    "Create Batch from Folder",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
            }
        }

        private CreatedBatchInfo CreateSeedBatchFromFolder(string processedFolder, string rawFolder, string inboxRoot)
        {
            if (string.IsNullOrWhiteSpace(processedFolder) || !Directory.Exists(processedFolder))
                throw new DirectoryNotFoundException("선택한 processed 폴더를 찾을 수 없습니다.");

            if (string.IsNullOrWhiteSpace(rawFolder) || !Directory.Exists(rawFolder))
                throw new DirectoryNotFoundException("선택한 RAW 폴더를 찾을 수 없습니다.");

            var sourceImages = Directory.EnumerateFiles(processedFolder, "*.bmp", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (sourceImages.Count == 0)
                throw new InvalidOperationException("선택한 processed 폴더에 bmp 파일이 없습니다.");

            var rawImages = Directory.EnumerateFiles(rawFolder, "*.bmp", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (rawImages.Count == 0)
                throw new InvalidOperationException("선택한 RAW 폴더에 bmp 파일이 없습니다.");

            var rawByFileName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawImage in rawImages)
            {
                string fileName = IOPath.GetFileName(rawImage);
                if (rawByFileName.ContainsKey(fileName))
                    throw new InvalidOperationException($"RAW 폴더에 중복 파일명이 있습니다: {fileName}");

                rawByFileName[fileName] = rawImage;
            }

            var matchedImagePairs = new List<(string ProcessedPath, string RawPath)>();
            var missingRawFiles = new List<string>();

            foreach (var processedImage in sourceImages)
            {
                string processedFileName = IOPath.GetFileName(processedImage);
                string expectedRawFileName = GetExpectedRawFileNameFromProcessed(processedFileName);

                if (!rawByFileName.TryGetValue(expectedRawFileName, out var rawPath))
                {
                    // 호환: 이미 같은 파일명으로 준비된 RAW 폴더도 허용
                    if (!rawByFileName.TryGetValue(processedFileName, out rawPath))
                    {
                        missingRawFiles.Add($"{processedFileName} -> expected RAW: {expectedRawFileName}");
                        continue;
                    }
                }

                matchedImagePairs.Add((processedImage, rawPath));
            }

            if (missingRawFiles.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("RAW 매칭 실패: processed 파일명의 '_masked'를 제거한 RAW 파일을 찾을 수 없습니다.");
                foreach (var missing in missingRawFiles.Take(20))
                    sb.AppendLine($"- {missing}");
                if (missingRawFiles.Count > 20)
                    sb.AppendLine($"... 외 {missingRawFiles.Count - 20}건");

                throw new InvalidOperationException(sb.ToString().TrimEnd());
            }

            string baseBatchId = $"batch_{DateTime.Now:yyyyMMdd_HHmmss}";
            string batchRoot = GetUniqueBatchFolderPath(inboxRoot, baseBatchId, out string batchId);
            string imagesDir = IOPath.Combine(batchRoot, "images");
            string rawDir = IOPath.Combine(batchRoot, "raw");
            string inferenceDir = IOPath.Combine(batchRoot, "inference");
            string metaDir = IOPath.Combine(batchRoot, "meta");

            var manifestItems = new List<SeedManifestItem>();
            var usedProcessedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var usedRawFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                Directory.CreateDirectory(imagesDir);
                Directory.CreateDirectory(rawDir);
                Directory.CreateDirectory(inferenceDir);
                Directory.CreateDirectory(metaDir);

                foreach (var (processedSourcePath, rawSourcePath) in matchedImagePairs)
                {
                    string uniqueProcessedFileName = GetUniqueFileName(
                        IOPath.GetFileName(processedSourcePath),
                        usedProcessedFileNames
                    );
                    usedProcessedFileNames.Add(uniqueProcessedFileName);

                    string processedDestinationPath = IOPath.Combine(imagesDir, uniqueProcessedFileName);
                    File.Copy(processedSourcePath, processedDestinationPath, overwrite: false);

                    string uniqueRawFileName = GetUniqueFileName(
                        IOPath.GetFileName(rawSourcePath),
                        usedRawFileNames
                    );
                    usedRawFileNames.Add(uniqueRawFileName);

                    string rawDestinationPath = IOPath.Combine(rawDir, uniqueRawFileName);
                    File.Copy(rawSourcePath, rawDestinationPath, overwrite: false);

                    manifestItems.Add(new SeedManifestItem
                    {
                        Id = IOPath.GetFileNameWithoutExtension(uniqueProcessedFileName),
                        ProcessedImage = $"images/{uniqueProcessedFileName}",
                        RawImage = $"raw/{uniqueRawFileName}"
                    });
                }

                string manifestPath = IOPath.Combine(metaDir, "manifest.json");
                WriteSeedManifest(manifestPath, batchId, manifestItems);

                string doneFlagPath = IOPath.Combine(metaDir, "DONE.flag");
                File.WriteAllText(doneFlagPath, "done", Encoding.UTF8);
            }
            catch
            {
                TryDeleteDirectory(batchRoot);
                throw;
            }

            return new CreatedBatchInfo
            {
                BatchPath = batchRoot,
                ItemCount = manifestItems.Count
            };
        }

        private static void WriteSeedManifest(string manifestPath, string batchId, IReadOnlyList<SeedManifestItem> items)
        {
            var manifestObject = new
            {
                schema_version = 2,
                batch_id = batchId,
                created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
                meta = new
                {
                    preprocess_id = "manual_seed"
                },
                items = items.Select(item => new SeedManifestItemJson
                {
                    Id = item.Id,
                    ProcessedImage = item.ProcessedImage,
                    RawImage = item.RawImage
                }).ToList()
            };

            var json = JsonSerializer.Serialize(
                manifestObject,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }
            );

            File.WriteAllText(manifestPath, json, Encoding.UTF8);
        }

        private static string GetUniqueBatchFolderPath(string inboxRoot, string baseBatchId, out string batchId)
        {
            batchId = baseBatchId;
            string candidate = IOPath.Combine(inboxRoot, batchId);
            int suffix = 2;

            while (Directory.Exists(candidate))
            {
                batchId = $"{baseBatchId}_{suffix}";
                candidate = IOPath.Combine(inboxRoot, batchId);
                suffix++;
            }

            return candidate;
        }

        private static string GetUniqueFileName(string originalFileName, ISet<string> usedFileNames)
        {
            string ext = IOPath.GetExtension(originalFileName);
            string baseName = IOPath.GetFileNameWithoutExtension(originalFileName);

            if (string.IsNullOrWhiteSpace(ext))
                ext = ".bmp";

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "image";

            string candidate = $"{baseName}{ext}";
            int suffix = 2;

            while (usedFileNames.Contains(candidate))
            {
                candidate = $"{baseName}_{suffix}{ext}";
                suffix++;
            }

            return candidate;
        }

        private static string GetExpectedRawFileNameFromProcessed(string processedFileName)
        {
            string ext = IOPath.GetExtension(processedFileName);
            string baseName = IOPath.GetFileNameWithoutExtension(processedFileName);

            const string maskedSuffix = "_masked";
            if (baseName.EndsWith(maskedSuffix, StringComparison.OrdinalIgnoreCase))
                baseName = baseName[..^maskedSuffix.Length];

            if (string.IsNullOrWhiteSpace(baseName))
                baseName = "image";
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".bmp";

            return $"{baseName}{ext}";
        }

        private static void TryDeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
                return;

            try
            {
                Directory.Delete(directoryPath, recursive: true);
            }
            catch
            {
            }
        }

        private void OpenTrainingInbox_Click(object sender, RoutedEventArgs e)
        {
            string inboxRoot = GetTrainingInboxRoot();
            Directory.CreateDirectory(inboxRoot);
            OpenFolder(inboxRoot);
        }

        private void RefreshImageList_Click(object sender, RoutedEventArgs e)
        {
            string? preferredImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;
            RefreshAllImagesFromTrainingInbox(preferredImagePath, _currentBatchRoot);
        }

        private void RefreshAllImagesFromTrainingInbox(string? preferredImagePath, string? preferredBatchRoot)
        {
            _currentBatchRoot = null;
            _currentBatchType = "library";
            _currentBatchRequiresInfer = false;
            _currentBatchHasAnyInfer = false;
            UpdateDataSourceUiState();

            string inboxRoot = GetTrainingInboxRoot();
            Directory.CreateDirectory(inboxRoot);

            string? currentSelectedImagePath = (ImageListBox.SelectedItem as ImageItem)?.ProcessedPath;

            var skipped = new List<string>();
            var loadedImagePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _suppressFilterRefresh = true;
            try
            {
                _images.Clear();
                _inferJsonByImagePath.Clear();

                var scanResult = _batchLibraryService.Scan(inboxRoot, includeHidden: false);
                skipped.AddRange(scanResult.Skipped);

                foreach (var batch in scanResult.Batches)
                {
                    try
                    {
                        string manifestPath = IOPath.Combine(batch.BatchRoot, "meta", "manifest.json");
                        var manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
                        AppendImagesFromBatch(batch.BatchRoot, manifest, loadedImagePaths);
                    }
                    catch (Exception ex)
                    {
                        skipped.Add($"{batch.BatchId}: manifest 로드 실패 ({ex.Message})");
                    }
                }
            }
            finally
            {
                _suppressFilterRefresh = false;
            }

            if (skipped.Count > 0)
            {
                var preview = string.Join(", ", skipped.Take(5));
                Trace.WriteLine(
                    $"batch library scan skipped {skipped.Count} folders. " +
                    (string.IsNullOrWhiteSpace(preview) ? "" : $"Sample: {preview}")
                );
            }

            RefreshBatchFilterOptions();
            ApplyImageFilters();
            RefreshSummaryCounts();
            UpdateDataSourceUiState();

            if (_images.Count == 0)
            {
                ResetImageDisplay();
                return;
            }

            ImageItem? target = null;
            if (!string.IsNullOrWhiteSpace(preferredImagePath))
                target = _images.FirstOrDefault(i => PathsEqual(i.ProcessedPath, preferredImagePath));

            if (target == null && !string.IsNullOrWhiteSpace(preferredBatchRoot))
                target = _images.FirstOrDefault(i => IsPathUnderRoot(i.ProcessedPath, preferredBatchRoot));

            if (target == null && !string.IsNullOrWhiteSpace(currentSelectedImagePath))
                target = _images.FirstOrDefault(i => PathsEqual(i.ProcessedPath, currentSelectedImagePath));

            if (target != null && IsVisibleInCurrentFilter(target))
            {
                ImageListBox.SelectedItem = target;
                ImageListBox.ScrollIntoView(target);
            }
            else
            {
                var firstVisible = _imageCollectionView?.Cast<object>()
                    .OfType<ImageItem>()
                    .FirstOrDefault();

                if (firstVisible != null)
                {
                    ImageListBox.SelectedItem = firstVisible;
                    ImageListBox.ScrollIntoView(firstVisible);
                }
                else
                {
                    ImageListBox.SelectedItem = null;
                    ResetImageDisplay();
                }
            }
        }

        private void AppendImagesFromBatch(
            string batchFolder,
            ManifestDto manifest,
            HashSet<string> loadedImagePaths)
        {
            string batchName = !string.IsNullOrWhiteSpace(manifest.BatchId)
                ? manifest.BatchId.Trim()
                : IOPath.GetFileName(batchFolder.TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar));

            foreach (var item in manifest.Items)
            {
                string imagePath;
                try
                {
                    imagePath = InferenceBatchPathResolver.ResolveBatchProcessedImagePath(batchFolder, item);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(
                        $"Skip image item in {batchFolder} (id={item.Id}): processed_image 확인 실패 ({ex.Message})");
                    continue;
                }

                if (!loadedImagePaths.Add(imagePath))
                    continue;

                string? rawImagePath = InferenceBatchPathResolver.ResolveBatchRawImagePath(batchFolder, item);
                string inferJsonPath = InferenceBatchPathResolver.ResolveBatchInferJsonPath(batchFolder, item);
                bool itemRequiresInfer = InferenceBatchPathResolver.DetermineItemRequiresInfer(batchFolder, manifest, item);
                var aiMeta = InferMetaEvaluator.Evaluate(inferJsonPath);

                bool hadStateFile = _stateService.HasState(imagePath);
                var state = _stateService.Load(imagePath);
                if (!hadStateFile)
                {
                    state.IsNormal = true;
                    _stateService.Save(imagePath, state);
                }

                bool hasGtLabel = state.HasManualYoloDecision && state.Labels.Count > 0;
                bool isNormal = (state.HasManualAnomalyDecision && state.IsNormal.HasValue)
                    ? state.IsNormal.Value
                    : true;
                var gtCounts = CountDefectClasses(state.Labels.Select(l => l.ClassName));
                string reviewStatus = DetermineReviewStatus(state, aiMeta, itemRequiresInfer);

                _imageStateManager.EnsureImage(imagePath);

                _images.Add(new ImageItem
                {
                    FileName = IOPath.GetFileName(imagePath),
                    BatchName = batchName,
                    ProcessedPath = imagePath,
                    RawPath = rawImagePath,
                    RequiresInfer = itemRequiresInfer,
                    HasInferFile = aiMeta.HasInferFile,
                    InferParseFailed = aiMeta.InferParseFailed,
                    HasStateFile = _stateService.HasState(imagePath),
                    HasLabel = hasGtLabel,
                    IsNormal = isNormal,
                    HasAiInfer = aiMeta.HasAiInfer,
                    AiYoloDefect = aiMeta.HasYoloDefect,
                    AiAnomaDefect = !aiMeta.IsAnomaNormal,
                    AiConsensusHighConfidence = aiMeta.IsConsensusHighConfidence,
                    AiYoloMaxConf = aiMeta.YoloMaxConf,
                    AiAnomaScore = aiMeta.AnomaScore,
                    AiDentCount = aiMeta.DentCount,
                    AiLooseCount = aiMeta.LooseCount,
                    AiOtherCount = aiMeta.OtherCount,
                    GtDentCount = gtCounts.Dent,
                    GtLooseCount = gtCounts.Loose,
                    GtOtherCount = gtCounts.Other,
                    ReviewStatus = reviewStatus,
                    ReviewReasonText = BuildReviewReasonPreview(state.ReviewReasons)
                });

                _inferJsonByImagePath[imagePath] = inferJsonPath;
                if (itemRequiresInfer)
                    _currentBatchRequiresInfer = true;
                if (aiMeta.HasInferFile)
                    _currentBatchHasAnyInfer = true;
            }
        }

        private static bool PathsEqual(string a, string b)
        {
            try
            {
                string left = IOPath.GetFullPath(a)
                    .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
                string right = IOPath.GetFullPath(b)
                    .TrimEnd(IOPath.DirectorySeparatorChar, IOPath.AltDirectorySeparatorChar);
                return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string DetermineReviewStatus(ImageStateDto state, InferMetaSummary aiMeta, bool requiresInfer)
        {
            if (state.HasManualYoloDecision || state.HasManualAnomalyDecision)
                return ReviewStatus.ReviewDone;

            string normalized = NormalizeReviewStatus(state.ReviewStatus);
            if (!string.Equals(normalized, ReviewStatus.None, StringComparison.OrdinalIgnoreCase))
                return normalized;

            if (aiMeta.InferParseFailed)
                return ReviewStatus.ReviewNeeded;

            if (requiresInfer && !aiMeta.HasInferFile)
                return ReviewStatus.ReviewNeeded;

            if (!aiMeta.HasAiInfer)
                return ReviewStatus.None;

            return aiMeta.IsConsensusHighConfidence
                ? ReviewStatus.AutoCandidate
                : ReviewStatus.ReviewNeeded;
        }

        private static string NormalizeReviewStatus(string? reviewStatus)
        {
            var normalized = (reviewStatus ?? "").Trim().ToLowerInvariant();
            return normalized switch
            {
                "review_needed" => ReviewStatus.ReviewNeeded,
                "auto_candidate" => ReviewStatus.AutoCandidate,
                "review_done" => ReviewStatus.ReviewDone,
                _ => ReviewStatus.None
            };
        }

        private static string BuildReviewReasonPreview(IReadOnlyList<string>? reasons)
        {
            if (reasons == null || reasons.Count == 0)
                return "";

            return string.Join(", ", reasons
                .Where(reason => !string.IsNullOrWhiteSpace(reason))
                .Take(3));
        }

    }
}
