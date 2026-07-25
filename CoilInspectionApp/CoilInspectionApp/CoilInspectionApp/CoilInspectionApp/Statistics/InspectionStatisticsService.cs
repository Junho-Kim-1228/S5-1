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

        public InspectionStatisticsService(
            string exportBaseDirectory,
            string currentBatchDirectory,
            string archiveBaseDirectory = "")
        {
            _completedBatchRoots = new[] { exportBaseDirectory, archiveBaseDirectory }
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            _currentBatchDirectory = currentBatchDirectory ?? "";
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
