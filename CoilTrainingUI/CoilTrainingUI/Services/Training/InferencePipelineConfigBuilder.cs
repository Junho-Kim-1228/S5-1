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
        double? calibratedAnomaThreshold = null,
        string? calibratedAnomaResizeMode = null,
        int? calibratedAnomaCropPaddingPx = null)
    {
        if (settings == null)
            throw new ArgumentNullException(nameof(settings));

        string mode = NormalizeMode(pipelineMode);
        bool requiresAnoma = mode is AnomaThenYolo or AnomaOnly;
        bool requiresYolo = mode is AnomaThenYolo or YoloOnly;
        int anomaInputSize = calibratedAnomaInputSize ?? settings.AnomaInfer.InputSize;
        double anomaThreshold = calibratedAnomaThreshold ?? settings.AnomaInfer.ScoreThres;
        string anomaResizeMode = string.IsNullOrWhiteSpace(calibratedAnomaResizeMode)
            ? settings.AnomaInfer.Mode
            : calibratedAnomaResizeMode.Trim().ToLowerInvariant();
        int anomaCropPaddingPx = calibratedAnomaCropPaddingPx ?? settings.AnomaInfer.CropPaddingPx;
        if (!string.Equals(anomaResizeMode, "crop", StringComparison.OrdinalIgnoreCase))
            anomaCropPaddingPx = 0;
        AutoReviewSection autoReview = settings.AutoReview ?? new AutoReviewSection();
        var requiredModels = new List<string> { "mask" };
        if (requiresAnoma) requiredModels.Add("anoma");
        if (requiresYolo) requiredModels.Add("yolo");

        var root = new Dictionary<string, object?>
        {
            ["schema_version"] = 4,
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

        root["auto_review"] = new
        {
            enabled = autoReview.Enabled,
            policy_version = autoReview.PolicyVersion,
            anoma_normal_threshold_multiplier = autoReview.AnomaNormalThresholdMultiplier,
            anoma_defect_threshold_multiplier = autoReview.AnomaDefectThresholdMultiplier,
            yolo_box_min_confidence = autoReview.YoloBoxMinConfidence,
            // Kept in schema for backward compatibility; sampling is disabled.
            audit_sample_rate = 0.0
        };

        root["mask"] = new
        {
            model = "models/mask.onnx",
            input_size = settings.MaskInfer.InputSize,
            resize_mode = settings.MaskInfer.ResizeMode,
            image_mean = settings.MaskInfer.ImageMean,
            image_std = settings.MaskInfer.ImageStd,
            confidence_percentile = settings.MaskInfer.ConfidencePercentile,
            confidence_threshold = settings.MaskInfer.ConfidenceThreshold,
            mask_threshold = settings.MaskInfer.MaskThreshold,
            min_component_area = settings.MaskInfer.MinComponentArea,
            morph_open_kernel = settings.MaskInfer.MorphOpenKernel,
            morph_close_kernel = settings.MaskInfer.MorphCloseKernel,
            outer_recover_kernel = settings.MaskInfer.OuterRecoverKernel,
            keep_largest_component = settings.MaskInfer.KeepLargestComponent,
            preserve_inner_holes = settings.MaskInfer.PreserveInnerHoles,
            min_hole_area = settings.MaskInfer.MinHoleArea
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
                mode = anomaResizeMode,
                input_size = anomaInputSize,
                score_thres = anomaThreshold,
                crop_padding_px = anomaCropPaddingPx
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
