using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilInspectionApp
{
    public sealed class PipelinePackageConfig
    {
        public int schema_version { get; set; }
        public PipelineSection pipeline { get; set; } = new PipelineSection();
        public YoloSection yolo { get; set; }
        public AnomaSection anoma { get; set; }

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
}
