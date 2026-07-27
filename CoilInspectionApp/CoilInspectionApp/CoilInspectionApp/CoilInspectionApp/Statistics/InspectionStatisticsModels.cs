using System.Collections.Generic;

namespace CoilInspectionApp.Statistics
{
    public sealed class StatisticsScopeOption
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string BatchDirectory { get; set; }

        public override string ToString()
        {
            return DisplayName ?? Key ?? "-";
        }
    }

    public sealed class StatisticsBatchItem
    {
        public string BatchName { get; set; }
        public string BatchDirectory { get; set; }
        public string LocationText { get; set; }
        public int ResultCount { get; set; }
        public string UpdatedAtText { get; set; }
        public bool CanMoveToTrash { get; set; }
    }

    public sealed class InspectionStatistics
    {
        public int TotalCount { get; set; }
        public int NormalCount { get; set; }
        public int DefectCount { get; set; }
        public int InvalidFileCount { get; set; }
        public int AnomaExecutedCount { get; set; }
        public int AnomaAnomalyCount { get; set; }
        public int YoloExecutedCount { get; set; }
        public int YoloDetectionImageCount { get; set; }
        public int DetectionCount { get; set; }
        public float? AnomaScoreAverage { get; set; }
        public float? AnomaScoreMinimum { get; set; }
        public float? AnomaScoreMaximum { get; set; }
        public List<DefectClassStatistics> DefectClasses { get; set; } = new List<DefectClassStatistics>();
        public List<InspectionStatisticsRow> Rows { get; set; } = new List<InspectionStatisticsRow>();

        public double DefectRate => TotalCount == 0 ? 0d : (double)DefectCount / TotalCount * 100d;
    }

    public sealed class DefectClassStatistics
    {
        public string ClassName { get; set; }
        public int Count { get; set; }
        public float AverageConfidence { get; set; }
    }

    public sealed class InspectionStatisticsRow
    {
        public string BatchName { get; set; }
        public string ImageId { get; set; }
        public string FinalDecision { get; set; }
        public string AnomaDecision { get; set; }
        public float? AnomaScore { get; set; }
        public string YoloStatus { get; set; }
        public int DetectionCount { get; set; }
        public string DefectClasses { get; set; }
    }
}
