using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilInspectionApp
{
    public sealed class PipelinePackageConfig
    {
        public int schema_version { get; set; }
        public PipelineSection pipeline { get; set; } = new PipelineSection();
        public MaskSection mask { get; set; }
        public YoloSection yolo { get; set; }
        public AnomaSection anoma { get; set; }
        public AutoReviewSection auto_review { get; set; }

        public bool RequiresMask =>
            (pipeline.required_models?.Any(model => string.Equals(model, "mask", StringComparison.OrdinalIgnoreCase)) == true)
            || mask != null;

        public bool RequiresYolo =>
            (pipeline.required_models?.Any(model => string.Equals(model, "yolo", StringComparison.OrdinalIgnoreCase)) == true)
            || yolo != null;

        public bool RequiresAnoma =>
            (pipeline.required_models?.Any(model => string.Equals(model, "anoma", StringComparison.OrdinalIgnoreCase)) == true)
            || anoma != null;

        public IReadOnlyDictionary<int, string> ClassNamesById
        {
            get
            {
                if (yolo?.class_map == null)
                    return new Dictionary<int, string>();

                return yolo.class_map.ToDictionary(kv => kv.Value, kv => kv.Key);
            }
        }
    }

    public sealed class MaskSection
    {
        public string model { get; set; } = "";
        public int input_size { get; set; } = 512;
        public string resize_mode { get; set; } = "letterbox";
        public float[] image_mean { get; set; } = { 0.485f, 0.456f, 0.406f };
        public float[] image_std { get; set; } = { 0.229f, 0.224f, 0.225f };
        public float confidence_percentile { get; set; } = 99.5f;
        public float confidence_threshold { get; set; } = 0.5f;
        public float mask_threshold { get; set; } = 0.3f;
        public int min_component_area { get; set; } = 64;
        public int morph_open_kernel { get; set; }
        public int morph_close_kernel { get; set; }
        public int outer_recover_kernel { get; set; }
        public bool keep_largest_component { get; set; } = true;
        public bool preserve_inner_holes { get; set; } = true;
        public int min_hole_area { get; set; } = 64;
    }

    public sealed class PipelineSection
    {
        public string mode { get; set; } = "anoma_then_yolo";
        public string stage1 { get; set; } = "anoma";
        public string stage2 { get; set; } = "";
        public bool skip_yolo_when_stage1_normal { get; set; }
        public List<string> required_models { get; set; } = new List<string>();
    }

    public sealed class YoloSection
    {
        public string model { get; set; } = "";
        public int imgsz { get; set; } = 640;
        public bool letterbox { get; set; } = true;
        public float conf_thres { get; set; } = 0.25f;
        public float iou_thres { get; set; } = 0.45f;
        public int max_det { get; set; } = 300;
        public Dictionary<string, int> class_map { get; set; } = new Dictionary<string, int>();
    }

    public sealed class AnomaSection
    {
        public string model { get; set; } = "";
        public string mode { get; set; } = "crop";
        public int input_size { get; set; } = 640;
        public float score_thres { get; set; } = 0.5f;
        public int crop_padding_px { get; set; }
    }

    public sealed class AutoReviewSection
    {
        public bool enabled { get; set; }
        public string policy_version { get; set; } = "auto_review_v2_no_audit";
        public float anoma_normal_threshold_multiplier { get; set; } = 0.95f;
        public float anoma_defect_threshold_multiplier { get; set; } = 1.6f;
        public float yolo_box_min_confidence { get; set; } = 0.85f;
        public float audit_sample_rate { get; set; }
    }
}
