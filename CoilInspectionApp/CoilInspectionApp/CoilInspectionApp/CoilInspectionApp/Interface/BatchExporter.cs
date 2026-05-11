using Newtonsoft.Json;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace CoilInspectionApp
{
    // 데이터 모델 클래스들
    public class InferJson
    {
        public int schema_version { get; set; } = 1;
        public string image_id { get; set; }
        public ImageSize image_size { get; set; }
        public YoloInfo yolo { get; set; } = new YoloInfo();
        public AnomaInfo anoma { get; set; }
        public FinalInfo final { get; set; }
    }

    public class ImageSize { public int w { get; set; } public int h { get; set; } }
    public class YoloInfo { public List<Detection> detections { get; set; } = new List<Detection>(); }
    public class Detection
    {
        public string class_name { get; set; }
        public float conf { get; set; }
        public float[] bbox_xywh_norm { get; set; }
    }
    public class AnomaInfo { public float score { get; set; } public string decision { get; set; } }
    public class FinalInfo { public bool is_defect { get; set; } public List<string> reason { get; set; } = new List<string>(); }

    public class ManifestJson
    {
        public int schema_version { get; set; } = 2;
        public string batch_type { get; set; } = "inference";
        public string batch_id { get; set; }
        public string created_at { get; set; }
        public List<ManifestItem> items { get; set; } = new List<ManifestItem>();
    }

    public class ManifestItem
    {
        public string id { get; set; }
        public string processed_image { get; set; }
        public string raw_image { get; set; }
        public string infer_json { get; set; }
    }

    public class BatchExporter
    {
        private string _baseOutputDir;
        private string _currentBatchDir;
        private ManifestJson _currentManifest;

        public BatchExporter(string baseOutputDir)
        {
            _baseOutputDir = baseOutputDir;
            if (!Directory.Exists(_baseOutputDir)) Directory.CreateDirectory(_baseOutputDir);
        }

        public void StartNewBatch()
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string batchId = $"export_batch_{timeStamp}";
            _currentBatchDir = Path.Combine(_baseOutputDir, batchId);

            Directory.CreateDirectory(_currentBatchDir);
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "images"));
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "raw"));
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "inference"));
            Directory.CreateDirectory(Path.Combine(_currentBatchDir, "meta"));

            _currentManifest = new ManifestJson
            {
                batch_id = batchId,
                created_at = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss")
            };
        }

        public void AddResult(string imageId, Mat rawImg, Mat processedImg, float anomaScore, bool isDefect, List<string> reasons)
        {
            if (_currentManifest == null) StartNewBatch();

            string relProcessedPath = $"images/{imageId}_masked.bmp";
            string relRawPath = $"raw/{imageId}.bmp";
            string relInferPath = $"inference/{imageId}.infer.json";

            if (rawImg != null && !rawImg.Empty())
                Cv2.ImWrite(Path.Combine(_currentBatchDir, relRawPath), rawImg);

            if (processedImg != null && !processedImg.Empty())
                Cv2.ImWrite(Path.Combine(_currentBatchDir, relProcessedPath), processedImg);

            var inferData = new InferJson
            {
                image_id = imageId,
                image_size = new ImageSize { w = processedImg.Width, h = processedImg.Height },
                anoma = new AnomaInfo { score = anomaScore, decision = anomaScore >= 0.5f ? "anomaly" : "normal" },
                final = new FinalInfo { is_defect = isDefect, reason = reasons }
            };

            string inferFullPath = Path.Combine(_currentBatchDir, relInferPath);
            // Formatting 오류 해결을 위해 Newtonsoft.Json.Formatting 명시
            File.WriteAllText(inferFullPath, JsonConvert.SerializeObject(inferData, Newtonsoft.Json.Formatting.Indented));

            _currentManifest.items.Add(new ManifestItem
            {
                id = imageId,
                processed_image = relProcessedPath,
                raw_image = relRawPath,
                infer_json = relInferPath
            });
        }

        public void CloseBatch()
        {
            if (_currentManifest == null || _currentManifest.items.Count == 0) return;

            string manifestPath = Path.Combine(_currentBatchDir, "meta", "manifest.json");
            File.WriteAllText(manifestPath, JsonConvert.SerializeObject(_currentManifest, Newtonsoft.Json.Formatting.Indented));

            // _currentBatchPath() 대신 변수명을 직접 사용하도록 수정
            string flagPath = Path.Combine(_currentBatchDir, "meta", "DONE.flag");
            File.WriteAllText(flagPath, "READY_FOR_TRAINING_UI");

            _currentManifest = null;
        }
    }
}