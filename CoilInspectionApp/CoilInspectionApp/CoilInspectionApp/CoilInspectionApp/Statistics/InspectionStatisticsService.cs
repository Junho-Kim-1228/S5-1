using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilInspectionApp.Statistics
{
    public sealed class InspectionStatisticsService
    {
        public const string CurrentScopeKey = "__current__";
        public const string AllCompletedScopeKey = "__all_completed__";

        private readonly List<string> _completedBatchRoots;
        private readonly string _currentBatchDirectory;
        private readonly string _exportBaseDirectory;
        private readonly string _archiveBaseDirectory;
        private readonly string _trashDirectory;

        public InspectionStatisticsService(
            string exportBaseDirectory,
            string currentBatchDirectory,
            string archiveBaseDirectory = "")
        {
            _exportBaseDirectory = string.IsNullOrWhiteSpace(exportBaseDirectory)
                ? ""
                : Path.GetFullPath(exportBaseDirectory);
            _archiveBaseDirectory = string.IsNullOrWhiteSpace(archiveBaseDirectory)
                ? ""
                : Path.GetFullPath(archiveBaseDirectory);
            _trashDirectory = string.IsNullOrWhiteSpace(_archiveBaseDirectory)
                ? ""
                : Path.Combine(_archiveBaseDirectory, "_trash");
            _completedBatchRoots = new[] { _exportBaseDirectory, _archiveBaseDirectory }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _currentBatchDirectory = currentBatchDirectory ?? "";
        }

        public List<StatisticsBatchItem> GetCompletedBatchItems()
        {
            return GetCompletedBatchDirectories()
                .Select(directory => CreateBatchItem(
                    directory,
                    IsDirectChild(directory, _archiveBaseDirectory) ? "보관 완료" : "학습 UI 전달 대기",
                    IsDirectChild(directory, _archiveBaseDirectory)))
                .ToList();
        }

        public List<StatisticsBatchItem> GetTrashedBatchItems()
        {
            if (string.IsNullOrWhiteSpace(_trashDirectory) || !Directory.Exists(_trashDirectory))
                return new List<StatisticsBatchItem>();

            return Directory.GetDirectories(_trashDirectory, "export_batch_*")
                .Where(IsCompletedBatchDirectory)
                .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
                .Select(directory => CreateBatchItem(directory, "통계 제외", false))
                .ToList();
        }

        public string MoveArchiveBatchToTrash(string batchDirectory)
        {
            string source = ValidateDirectChildBatch(batchDirectory, _archiveBaseDirectory, "보관 배치");
            Directory.CreateDirectory(_trashDirectory);
            string destination = UniqueDirectory(Path.Combine(_trashDirectory, Path.GetFileName(source)));
            Directory.Move(source, destination);
            return destination;
        }

        public string RestoreBatchFromTrash(string batchDirectory)
        {
            string source = ValidateDirectChildBatch(batchDirectory, _trashDirectory, "휴지통 배치");
            if (string.IsNullOrWhiteSpace(_archiveBaseDirectory))
                throw new InvalidOperationException("archive 폴더가 설정되지 않았습니다.");
            Directory.CreateDirectory(_archiveBaseDirectory);
            string destination = UniqueDirectory(Path.Combine(_archiveBaseDirectory, Path.GetFileName(source)));
            Directory.Move(source, destination);
            return destination;
        }

        public List<StatisticsScopeOption> GetScopeOptions()
        {
            var options = new List<StatisticsScopeOption>
            {
                new StatisticsScopeOption
                {
                    Key = CurrentScopeKey,
                    DisplayName = "현재 배치 (추론 완료 기준)",
                    BatchDirectory = _currentBatchDirectory,
                }
            };

            List<string> completedDirectories = GetCompletedBatchDirectories();
            if (completedDirectories.Count > 0)
            {
                options.Add(new StatisticsScopeOption
                {
                    Key = AllCompletedScopeKey,
                    DisplayName = $"완료 배치 전체 ({completedDirectories.Count}개)",
                });
            }

            options.AddRange(completedDirectories.Select(directory => new StatisticsScopeOption
            {
                Key = directory,
                DisplayName = Path.GetFileName(directory),
                BatchDirectory = directory,
            }));
            return options;
        }

        public InspectionStatistics Load(StatisticsScopeOption scope)
        {
            var statistics = new InspectionStatistics();
            if (scope == null)
                return statistics;

            IEnumerable<string> batchDirectories;
            if (string.Equals(scope.Key, AllCompletedScopeKey, StringComparison.OrdinalIgnoreCase))
                batchDirectories = GetCompletedBatchDirectories();
            else
                batchDirectories = new[] { scope.BatchDirectory };

            var confidenceByClass = new Dictionary<string, List<float>>(StringComparer.OrdinalIgnoreCase);
            var scores = new List<float>();

            foreach (string batchDirectory in batchDirectories.Where(Directory.Exists))
            {
                string batchName = string.Equals(scope.Key, CurrentScopeKey, StringComparison.OrdinalIgnoreCase)
                    ? "현재 배치"
                    : Path.GetFileName(batchDirectory);
                string inferenceDirectory = Path.Combine(batchDirectory, "inference");
                if (!Directory.Exists(inferenceDirectory))
                    continue;

                foreach (string inferPath in Directory.GetFiles(inferenceDirectory, "*.infer.json")
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    InferJson infer;
                    try
                    {
                        infer = JsonConvert.DeserializeObject<InferJson>(File.ReadAllText(inferPath));
                    }
                    catch
                    {
                        statistics.InvalidFileCount++;
                        continue;
                    }

                    if (infer == null)
                    {
                        statistics.InvalidFileCount++;
                        continue;
                    }

                    AddInference(statistics, confidenceByClass, scores, batchName, infer);
                }
            }

            statistics.AnomaScoreAverage = scores.Count == 0 ? (float?)null : scores.Average();
            statistics.AnomaScoreMinimum = scores.Count == 0 ? (float?)null : scores.Min();
            statistics.AnomaScoreMaximum = scores.Count == 0 ? (float?)null : scores.Max();
            statistics.DefectClasses = confidenceByClass
                .Select(pair => new DefectClassStatistics
                {
                    ClassName = pair.Key,
                    Count = pair.Value.Count,
                    AverageConfidence = pair.Value.Count == 0 ? 0f : pair.Value.Average(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.ClassName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            statistics.Rows = statistics.Rows
                .OrderByDescending(row => row.BatchName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.ImageId, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return statistics;
        }

        private List<string> GetCompletedBatchDirectories()
        {
            return _completedBatchRoots
                .Where(Directory.Exists)
                .SelectMany(root => Directory.GetDirectories(root, "export_batch_*"))
                .Where(directory => File.Exists(Path.Combine(directory, "meta", "DONE.flag")))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(directory => directory, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static StatisticsBatchItem CreateBatchItem(
            string directory,
            string locationText,
            bool canMoveToTrash)
        {
            string inferenceDirectory = Path.Combine(directory, "inference");
            int resultCount = Directory.Exists(inferenceDirectory)
                ? Directory.GetFiles(inferenceDirectory, "*.infer.json").Length
                : 0;
            return new StatisticsBatchItem
            {
                BatchName = Path.GetFileName(directory),
                BatchDirectory = directory,
                LocationText = locationText,
                ResultCount = resultCount,
                UpdatedAtText = Directory.GetLastWriteTime(directory).ToString("yyyy-MM-dd HH:mm:ss"),
                CanMoveToTrash = canMoveToTrash,
            };
        }

        private static string ValidateDirectChildBatch(string batchDirectory, string expectedRoot, string label)
        {
            if (string.IsNullOrWhiteSpace(batchDirectory) || string.IsNullOrWhiteSpace(expectedRoot))
                throw new InvalidOperationException($"{label} 경로가 올바르지 않습니다.");

            string source = Path.GetFullPath(batchDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string root = Path.GetFullPath(expectedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(source) ?? "";
            if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{label} 폴더 밖의 경로는 이동할 수 없습니다.");
            if (!IsCompletedBatchDirectory(source))
                throw new InvalidDataException("완료된 추론 배치가 아닙니다.");
            return source;
        }

        private static bool IsCompletedBatchDirectory(string directory)
        {
            return Directory.Exists(directory)
                   && Path.GetFileName(directory).StartsWith("export_batch_", StringComparison.OrdinalIgnoreCase)
                   && File.Exists(Path.Combine(directory, "meta", "DONE.flag"));
        }

        private static bool IsDirectChild(string directory, string root)
        {
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(root))
                return false;
            string parent = Path.GetDirectoryName(Path.GetFullPath(directory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)) ?? "";
            string normalizedRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(parent, normalizedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static string UniqueDirectory(string requestedPath)
        {
            if (!Directory.Exists(requestedPath))
                return requestedPath;
            string suffix = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string candidate = requestedPath + "-" + suffix;
            int index = 2;
            while (Directory.Exists(candidate))
                candidate = requestedPath + "-" + suffix + "-" + index++;
            return candidate;
        }

        private static void AddInference(
            InspectionStatistics statistics,
            Dictionary<string, List<float>> confidenceByClass,
            List<float> scores,
            string batchName,
            InferJson infer)
        {
            statistics.TotalCount++;
            bool isDefect = infer.final?.is_defect == true;
            if (isDefect)
                statistics.DefectCount++;
            else
                statistics.NormalCount++;

            if (infer.anoma?.executed == true)
            {
                statistics.AnomaExecutedCount++;
                scores.Add(infer.anoma.score);
                if (string.Equals(infer.anoma.decision, "anomaly", StringComparison.OrdinalIgnoreCase))
                    statistics.AnomaAnomalyCount++;
            }

            List<Detection> detections = infer.yolo?.detections ?? new List<Detection>();
            if (infer.yolo?.executed == true)
            {
                statistics.YoloExecutedCount++;
                if (detections.Count > 0)
                    statistics.YoloDetectionImageCount++;
            }

            statistics.DetectionCount += detections.Count;
            foreach (Detection detection in detections)
            {
                string className = ToDisplayClassName(detection.class_name);
                List<float> confidences;
                if (!confidenceByClass.TryGetValue(className, out confidences))
                {
                    confidences = new List<float>();
                    confidenceByClass[className] = confidences;
                }
                confidences.Add(detection.conf);
            }

            string classes = string.Join(", ", detections
                .GroupBy(detection => ToDisplayClassName(detection.class_name))
                .Select(group => $"{group.Key} {group.Count()}"));
            statistics.Rows.Add(new InspectionStatisticsRow
            {
                BatchName = batchName,
                ImageId = infer.image_id ?? "-",
                FinalDecision = isDefect ? "불량" : "정상",
                AnomaDecision = ToAnomaDecision(infer.anoma),
                AnomaScore = infer.anoma?.executed == true ? (float?)infer.anoma.score : null,
                YoloStatus = ToYoloStatus(infer.yolo, detections.Count),
                DetectionCount = detections.Count,
                DefectClasses = string.IsNullOrWhiteSpace(classes) ? "-" : classes,
            });
        }

        private static string ToAnomaDecision(AnomaInfo anoma)
        {
            if (anoma?.executed != true)
                return "미실행";
            return string.Equals(anoma.decision, "anomaly", StringComparison.OrdinalIgnoreCase)
                ? "이상"
                : "정상";
        }

        private static string ToYoloStatus(YoloInfo yolo, int detectionCount)
        {
            if (yolo?.executed != true)
                return "미실행";
            return detectionCount > 0 ? "검출" : "미검출";
        }

        private static string ToDisplayClassName(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return "미분류";
            if (string.Equals(className, "dent", StringComparison.OrdinalIgnoreCase))
                return "찍힘";
            if (string.Equals(className, "loose", StringComparison.OrdinalIgnoreCase))
                return "풀림";
            return className.Trim();
        }
    }
}
