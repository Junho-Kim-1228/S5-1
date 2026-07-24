using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CoilInspectionApp.Interface
{
    public sealed class AnomaInferenceResult
    {
        public float Score { get; set; }
        public string Decision { get; set; } = "";
    }

    public class OnnxModelTester : IDisposable
    {
        private static readonly float[] ImagenetMean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] ImagenetStd = { 0.229f, 0.224f, 0.225f };
        private InferenceSession _anomaSession;
        private InferenceSession _yoloSession;

        public bool HasAnomaModel => _anomaSession != null;
        public bool HasYoloModel => _yoloSession != null;

        public void LoadAnomaModel(string modelPath)
        {
            _anomaSession?.Dispose();
            _anomaSession = new InferenceSession(modelPath);
        }

        public void LoadYoloModel(string modelPath)
        {
            _yoloSession?.Dispose();
            _yoloSession = new InferenceSession(modelPath);
        }

        public void Dispose()
        {
            _anomaSession?.Dispose();
            _anomaSession = null;
            _yoloSession?.Dispose();
            _yoloSession = null;
        }

        public AnomaInferenceResult RunAnomaInference(Mat image, float threshold)
        {
            if (_anomaSession == null)
                throw new InvalidOperationException("Anoma model is not loaded.");

            if (image == null || image.Empty())
                throw new ArgumentException("Anoma input image is empty.");

            string inputName = _anomaSession.InputMetadata.Keys.First();
            int[] shape = _anomaSession.InputMetadata[inputName].Dimensions.ToArray();
            int height = ResolveDimension(shape, 2, image.Height);
            int width = ResolveDimension(shape, 3, image.Width);

            using (Mat resized = EnsureSize(image, width, height))
            {
                DenseTensor<float> inputTensor = ExtractPixelsForAnoma(resized);

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _anomaSession.Run(
                    new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) }))
                {
                    DisposableNamedOnnxValue scoreOutput = results.FirstOrDefault(
                        o => string.Equals(o.Name, "anomaly_score", StringComparison.OrdinalIgnoreCase))
                        ?? results.First();

                    float score = scoreOutput.AsTensor<float>().ToArray().FirstOrDefault();
                    return new AnomaInferenceResult
                    {
                        Score = score,
                        Decision = score >= threshold ? "anomaly" : "normal"
                    };
                }
            }
        }

        public List<CoilInspectionApp.Detection> RunYoloInference(
            Mat image,
            float confThreshold,
            float iouThreshold,
            int maxDet,
            IReadOnlyDictionary<int, string> classNames)
        {
            if (_yoloSession == null)
                throw new InvalidOperationException("YOLO model is not loaded.");

            if (image == null || image.Empty())
                throw new ArgumentException("YOLO input image is empty.");

            string inputName = _yoloSession.InputMetadata.Keys.First();
            int[] shape = _yoloSession.InputMetadata[inputName].Dimensions.ToArray();
            int height = ResolveDimension(shape, 2, image.Height);
            int width = ResolveDimension(shape, 3, image.Width);

            using (Mat resized = EnsureSize(image, width, height))
            {
                DenseTensor<float> inputTensor = ExtractPixelsForYolo(resized);

                using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = _yoloSession.Run(
                    new[] { NamedOnnxValue.CreateFromTensor(inputName, inputTensor) }))
                {
                    DisposableNamedOnnxValue output = results.First();
                    Tensor<float> tensor = output.AsTensor<float>();
                    List<RawYoloDetection> candidates = ParseYoloCandidates(
                        tensor,
                        width,
                        height,
                        confThreshold,
                        classNames?.Count ?? 0);

                    List<RawYoloDetection> kept = ApplyNonMaximumSuppression(candidates, iouThreshold, maxDet);
                    return kept.Select(d => new CoilInspectionApp.Detection
                    {
                        class_name = ResolveClassName(d.ClassId, classNames),
                        conf = d.Confidence,
                        bbox_xywh_norm = new[]
                        {
                            Clamp01(d.Cx / width),
                            Clamp01(d.Cy / height),
                            Clamp01(d.W / width),
                            Clamp01(d.H / height),
                        }
                    }).ToList();
                }
            }
        }

        private static DenseTensor<float> ExtractPixelsForYolo(Mat image)
        {
            int width = image.Width;
            int height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec3b color = image.At<Vec3b>(y, x);
                    tensor[0, 0, y, x] = color.Item0 / 255.0f;
                    tensor[0, 1, y, x] = color.Item1 / 255.0f;
                    tensor[0, 2, y, x] = color.Item2 / 255.0f;
                }
            }

            return tensor;
        }

        private static DenseTensor<float> ExtractPixelsForAnoma(Mat image)
        {
            int width = image.Width;
            int height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Anoma input is already converted from BGR to RGB by ImageProcessor.
                    // Preserve that channel order and apply the same ImageNet normalization
                    // used by coil-ai/anoma/workspace.py.
                    Vec3b color = image.At<Vec3b>(y, x);
                    tensor[0, 0, y, x] = ((color.Item0 / 255.0f) - ImagenetMean[0]) / ImagenetStd[0];
                    tensor[0, 1, y, x] = ((color.Item1 / 255.0f) - ImagenetMean[1]) / ImagenetStd[1];
                    tensor[0, 2, y, x] = ((color.Item2 / 255.0f) - ImagenetMean[2]) / ImagenetStd[2];
                }
            }

            return tensor;
        }

        private static Mat EnsureSize(Mat image, int width, int height)
        {
            if (image.Width == width && image.Height == height)
                return image.Clone();

            Mat resized = new Mat();
            Cv2.Resize(image, resized, new OpenCvSharp.Size(width, height));
            return resized;
        }

        private static int ResolveDimension(int[] dimensions, int index, int fallback)
        {
            if (dimensions.Length <= index)
                return fallback;

            int value = dimensions[index];
            return value > 0 ? value : fallback;
        }

        private static List<RawYoloDetection> ParseYoloCandidates(
            Tensor<float> tensor,
            int inputWidth,
            int inputHeight,
            float confThreshold,
            int configuredClassCount)
        {
            int[] dims = tensor.Dimensions.ToArray();
            if (dims.Length < 2 || dims.Length > 3)
                throw new InvalidOperationException($"Unsupported YOLO output rank: {dims.Length}");

            int boxCount;
            int channelCount;
            bool channelsFirst;

            if (dims.Length == 3)
            {
                if (dims[0] != 1)
                    throw new InvalidOperationException("Unsupported YOLO batch size. Only batch=1 is supported.");

                channelsFirst = dims[1] <= dims[2];
                channelCount = channelsFirst ? dims[1] : dims[2];
                boxCount = channelsFirst ? dims[2] : dims[1];
            }
            else
            {
                channelsFirst = dims[0] <= dims[1];
                channelCount = channelsFirst ? dims[0] : dims[1];
                boxCount = channelsFirst ? dims[1] : dims[0];
            }

            bool hasObjectness = configuredClassCount > 0
                ? channelCount == configuredClassCount + 5
                : channelCount > 6;
            int classCount = configuredClassCount > 0
                ? configuredClassCount
                : Math.Max(1, channelCount - (hasObjectness ? 5 : 4));

            var detections = new List<RawYoloDetection>();
            for (int i = 0; i < boxCount; i++)
            {
                float cx = ReadValue(tensor, dims, channelsFirst, i, 0);
                float cy = ReadValue(tensor, dims, channelsFirst, i, 1);
                float w = ReadValue(tensor, dims, channelsFirst, i, 2);
                float h = ReadValue(tensor, dims, channelsFirst, i, 3);

                if (!IsFiniteBox(cx, cy, w, h))
                    continue;

                float objectness = hasObjectness ? ReadValue(tensor, dims, channelsFirst, i, 4) : 1f;
                int classStart = hasObjectness ? 5 : 4;

                float bestClassScore = float.MinValue;
                int bestClassId = 0;
                for (int classId = 0; classId < classCount; classId++)
                {
                    float classScore = ReadValue(tensor, dims, channelsFirst, i, classStart + classId);
                    if (classScore > bestClassScore)
                    {
                        bestClassScore = classScore;
                        bestClassId = classId;
                    }
                }

                float confidence = objectness * bestClassScore;
                if (confidence < confThreshold)
                    continue;

                // Ultralytics exported boxes are already in model input coordinates.
                detections.Add(new RawYoloDetection
                {
                    ClassId = bestClassId,
                    Confidence = confidence,
                    Cx = Clamp(cx, 0f, inputWidth),
                    Cy = Clamp(cy, 0f, inputHeight),
                    W = Clamp(w, 0f, inputWidth),
                    H = Clamp(h, 0f, inputHeight),
                });
            }

            return detections;
        }

        private static float ReadValue(Tensor<float> tensor, int[] dims, bool channelsFirst, int boxIndex, int channelIndex)
        {
            if (dims.Length == 3)
                return channelsFirst ? tensor[0, channelIndex, boxIndex] : tensor[0, boxIndex, channelIndex];

            return channelsFirst ? tensor[channelIndex, boxIndex] : tensor[boxIndex, channelIndex];
        }

        private static List<RawYoloDetection> ApplyNonMaximumSuppression(
            List<RawYoloDetection> candidates,
            float iouThreshold,
            int maxDet)
        {
            var kept = new List<RawYoloDetection>();
            foreach (IGrouping<int, RawYoloDetection> group in candidates
                .OrderByDescending(d => d.Confidence)
                .GroupBy(d => d.ClassId))
            {
                foreach (RawYoloDetection candidate in group.OrderByDescending(d => d.Confidence))
                {
                    bool suppressed = kept.Any(existing =>
                        existing.ClassId == candidate.ClassId &&
                        ComputeIoU(existing, candidate) > iouThreshold);

                    if (suppressed)
                        continue;

                    kept.Add(candidate);
                    if (kept.Count >= maxDet)
                        return kept;
                }
            }

            return kept;
        }

        private static float ComputeIoU(RawYoloDetection left, RawYoloDetection right)
        {
            float leftX1 = left.Cx - (left.W / 2f);
            float leftY1 = left.Cy - (left.H / 2f);
            float leftX2 = left.Cx + (left.W / 2f);
            float leftY2 = left.Cy + (left.H / 2f);

            float rightX1 = right.Cx - (right.W / 2f);
            float rightY1 = right.Cy - (right.H / 2f);
            float rightX2 = right.Cx + (right.W / 2f);
            float rightY2 = right.Cy + (right.H / 2f);

            float interX1 = Math.Max(leftX1, rightX1);
            float interY1 = Math.Max(leftY1, rightY1);
            float interX2 = Math.Min(leftX2, rightX2);
            float interY2 = Math.Min(leftY2, rightY2);

            float interW = Math.Max(0f, interX2 - interX1);
            float interH = Math.Max(0f, interY2 - interY1);
            float interArea = interW * interH;
            if (interArea <= 0f)
                return 0f;

            float leftArea = Math.Max(0f, left.W) * Math.Max(0f, left.H);
            float rightArea = Math.Max(0f, right.W) * Math.Max(0f, right.H);
            float union = leftArea + rightArea - interArea;
            return union <= 0f ? 0f : interArea / union;
        }

        private static string ResolveClassName(int classId, IReadOnlyDictionary<int, string> classNames)
        {
            if (classNames != null && classNames.TryGetValue(classId, out string className))
                return className;

            return $"class_{classId}";
        }

        private static bool IsFiniteBox(float cx, float cy, float w, float h)
            => IsFinite(cx) && IsFinite(cy) && IsFinite(w) && IsFinite(h) && w > 0 && h > 0;

        private static bool IsFinite(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value);

        private static float Clamp(float value, float min, float max)
            => Math.Max(min, Math.Min(max, value));

        private static float Clamp01(float value)
            => Clamp(value, 0f, 1f);

        private sealed class RawYoloDetection
        {
            public int ClassId { get; set; }
            public float Confidence { get; set; }
            public float Cx { get; set; }
            public float Cy { get; set; }
            public float W { get; set; }
            public float H { get; set; }
        }
    }
}
