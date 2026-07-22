using System;
using System.Collections.Generic;

namespace CoilTrainingUI.Services;

public static class InferencePipelineConfigBuilder
{
    public const string AnomaThenYolo = "anoma_then_yolo";
    public const string AnomaOnly = "anoma_only";
    public const string YoloOnly = "yolo_only";

    public static object Build(
        AppSettings settings,
        string pipelineMode,
        string displayName,
        int? calibratedAnomaInputSize = null,
        double? calibratedAnomaThreshold = null)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        string mode = NormalizeMode(pipelineMode);
        bool requiresAnoma = mode is AnomaThenYolo or AnomaOnly;
        bool requiresYolo = mode is AnomaThenYolo or YoloOnly;
        int anomaInputSize = calibratedAnomaInputSize ?? settings.AnomaInfer.InputSize;
        double anomaThreshold = calibratedAnomaThreshold ?? settings.AnomaInfer.ScoreThres;
        var requiredModels = new List<string>();
        if (requiresAnoma) requiredModels.Add("anoma");
        if (requiresYolo) requiredModels.Add("yolo");

        var root = new Dictionary<string, object?>
        {
            ["schema_version"] = 2,
            ["pipeline"] = new
            {
                mode,
                display_name = displayName,
                stage1 = mode == YoloOnly ? "yolo" : "anoma",
                stage2 = mode == AnomaThenYolo ? "yolo" : null,
                skip_yolo_when_stage1_normal = mode == AnomaThenYolo,
                required_models = requiredModels
            },
            ["input"] = new { image_format = "bmp" },
            ["output"] = new { format = "json", schema = "detections_v1" }
        };

        if (requiresYolo)
        {
            root["yolo"] = new
            {
                model = "models/yolo.onnx",
                imgsz = settings.YoloInfer.ImgSz,
                letterbox = settings.YoloInfer.Letterbox,
                conf_thres = settings.YoloInfer.ConfThres,
                iou_thres = settings.YoloInfer.IouThres,
                max_det = settings.YoloInfer.MaxDet,
                class_map = new { dent = 0, loose = 1 }
            };
        }

        if (requiresAnoma)
        {
            root["anoma"] = new
            {
                model = "models/anoma.onnx",
                mode = settings.AnomaInfer.Mode,
                input_size = anomaInputSize,
                score_thres = anomaThreshold,
                crop_padding_px = settings.AnomaInfer.CropPaddingPx
            };
        }

        return root;
    }

    private static string NormalizeMode(string? mode)
    {
        return (mode ?? "").Trim().ToLowerInvariant() switch
        {
            AnomaOnly => AnomaOnly,
            YoloOnly => YoloOnly,
            _ => AnomaThenYolo
        };
    }
}
