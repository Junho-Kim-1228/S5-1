namespace CoilInspectionApp
{
    public class InferenceContextInfo
    {
        public string status { get; set; } = "recorded";
        public string reason { get; set; } = "";
        public string context_id { get; set; } = "";
        public string captured_at_utc { get; set; } = "";
        public string pipeline_mode { get; set; } = "";
        public string package_fingerprint { get; set; } = "";
        public string pipeline_sha256 { get; set; } = "";
        public MaskInferenceContext mask { get; set; }
        public AnomaInferenceContext anoma { get; set; }
        public YoloInferenceContext yolo { get; set; }
        public AutoReviewInferenceContext auto_review { get; set; }
    }

    public class MaskInferenceContext
    {
        public string model_file { get; set; } = "";
        public string model_sha256 { get; set; } = "";
        public float confidence_threshold { get; set; }
        public float mask_threshold { get; set; }
        public int input_size { get; set; }
        public string resize_mode { get; set; } = "";
    }

    public class AnomaInferenceContext
    {
        public string model_file { get; set; } = "";
        public string model_sha256 { get; set; } = "";
        public float score_threshold { get; set; }
        public int input_size { get; set; }
        public string mode { get; set; } = "";
    }

    public class YoloInferenceContext
    {
        public string model_file { get; set; } = "";
        public string model_sha256 { get; set; } = "";
        public float confidence_threshold { get; set; }
        public float iou_threshold { get; set; }
        public int input_size { get; set; }
    }

    public class AutoReviewInferenceContext
    {
        public bool enabled { get; set; }
        public string policy_version { get; set; } = "";
        public float anoma_normal_threshold_multiplier { get; set; }
        public float anoma_defect_threshold_multiplier { get; set; }
        public float yolo_box_min_confidence { get; set; }
        public float audit_sample_rate { get; set; }
    }
}
