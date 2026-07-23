using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilInspectionApp.Preprocess
{
    public sealed class MaskOnnxRunner : IDisposable
    {
        private readonly InferenceSession _session;
        private readonly MaskSection _config;
        private readonly string _inputName;
        private readonly string _outputName;

        public MaskOnnxRunner(string modelPath, MaskSection config)
        {
            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
                throw new FileNotFoundException("Mask ONNX model을 찾을 수 없습니다.", modelPath);

            _config = config ?? throw new ArgumentNullException(nameof(config));
            ValidateConfig(_config);
            _session = new InferenceSession(modelPath);
            _inputName = _session.InputMetadata.Keys.FirstOrDefault()
                ?? throw new InvalidOperationException("Mask ONNX 입력을 찾을 수 없습니다.");
            _outputName = _session.OutputMetadata.Keys
                .FirstOrDefault(name => string.Equals(name, "probability", StringComparison.OrdinalIgnoreCase))
                ?? _session.OutputMetadata.Keys.FirstOrDefault()
                ?? throw new InvalidOperationException("Mask ONNX 출력을 찾을 수 없습니다.");

            ValidateModelContract();
        }

        public IReadOnlyDictionary<string, string> RunBatch(
            IEnumerable<string> rawImagePaths,
            string outputDir,
            Action<string, string> onMaskedImageReady = null)
        {
            if (rawImagePaths == null)
                throw new ArgumentNullException(nameof(rawImagePaths));

            string[] paths = rawImagePaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (paths.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            Directory.CreateDirectory(outputDir);
            var results = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var reservedOutputs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string rawPath in paths)
            {
                if (!File.Exists(rawPath))
                    throw new FileNotFoundException("전처리 입력 이미지를 찾을 수 없습니다.", rawPath);

                string outputPath = BuildUniqueOutputPath(outputDir, rawPath, reservedOutputs);
                ProcessImage(rawPath, outputPath);
                results[rawPath] = outputPath;
                onMaskedImageReady?.Invoke(rawPath, outputPath);
            }

            return results;
        }

        public void ProcessImage(string rawImagePath, string outputPath)
        {
            if (string.IsNullOrWhiteSpace(rawImagePath) || !File.Exists(rawImagePath))
                throw new FileNotFoundException("전처리 입력 이미지를 찾을 수 없습니다.", rawImagePath);
            if (string.IsNullOrWhiteSpace(outputPath))
                throw new ArgumentException("Mask 출력 경로가 비어 있습니다.", nameof(outputPath));

            using (Mat source = Cv2.ImRead(rawImagePath, ImreadModes.Color))
            {
                if (source.Empty())
                    throw new InvalidOperationException("전처리 입력 이미지를 읽을 수 없습니다: " + rawImagePath);

                ResizeMetadata resizeMetadata;
                using (Mat input = ResizeWithPadding(source, _config.input_size, out resizeMetadata))
                {
                    DenseTensor<float> inputTensor = CreateInputTensor(input);
                    using (IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = _session.Run(
                        new[] { NamedOnnxValue.CreateFromTensor(_inputName, inputTensor) }))
                    {
                        DisposableNamedOnnxValue output = outputs.First(value =>
                            string.Equals(value.Name, _outputName, StringComparison.OrdinalIgnoreCase));
                        using (Mat probability = RestoreProbabilityMap(
                            output.AsTensor<float>(), resizeMetadata, source.Width, source.Height))
                        using (Mat mask = BuildFinalMask(probability))
                        using (Mat masked = new Mat())
                        {
                            Cv2.BitwiseAnd(source, source, masked, mask);
                            string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
                            Directory.CreateDirectory(directory);
                            if (!Cv2.ImWrite(outputPath, masked))
                                throw new IOException("Mask 전처리 이미지를 저장하지 못했습니다: " + outputPath);
                        }
                    }
                }
            }
        }

        public void Dispose()
        {
            _session?.Dispose();
        }

        private void ValidateModelContract()
        {
            int[] inputDimensions = _session.InputMetadata[_inputName].Dimensions.ToArray();
            if (inputDimensions.Length != 4)
                throw new InvalidOperationException("Mask ONNX 입력은 NCHW 4차원이어야 합니다.");
            if (inputDimensions[1] > 0 && inputDimensions[1] != 3)
                throw new InvalidOperationException("Mask ONNX 입력 채널은 RGB 3채널이어야 합니다.");
            if (inputDimensions[2] > 0 && inputDimensions[2] != _config.input_size)
                throw new InvalidOperationException("Mask ONNX 입력 높이와 pipeline.json의 input_size가 다릅니다.");
            if (inputDimensions[3] > 0 && inputDimensions[3] != _config.input_size)
                throw new InvalidOperationException("Mask ONNX 입력 너비와 pipeline.json의 input_size가 다릅니다.");
        }

        private static void ValidateConfig(MaskSection config)
        {
            if (config.input_size <= 0)
                throw new InvalidOperationException("pipeline.json mask.input_size가 올바르지 않습니다.");
            if (!string.Equals(config.resize_mode, "letterbox", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Mask resize_mode는 letterbox만 지원합니다.");
            if (config.image_mean == null || config.image_mean.Length != 3
                || config.image_std == null || config.image_std.Length != 3
                || config.image_std.Any(value => value <= 0f))
            {
                throw new InvalidOperationException("pipeline.json mask 정규화 설정이 올바르지 않습니다.");
            }
            if (config.confidence_percentile < 0f || config.confidence_percentile > 100f)
                throw new InvalidOperationException("mask.confidence_percentile은 0~100이어야 합니다.");
            if (config.confidence_threshold < 0f || config.confidence_threshold > 1f
                || config.mask_threshold < 0f || config.mask_threshold > 1f)
            {
                throw new InvalidOperationException("Mask 임계값은 0~1이어야 합니다.");
            }
        }

        private DenseTensor<float> CreateInputTensor(Mat image)
        {
            int width = image.Width;
            int height = image.Height;
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vec3b bgr = image.At<Vec3b>(y, x);
                    tensor[0, 0, y, x] = ((bgr.Item2 / 255.0f) - _config.image_mean[0]) / _config.image_std[0];
                    tensor[0, 1, y, x] = ((bgr.Item1 / 255.0f) - _config.image_mean[1]) / _config.image_std[1];
                    tensor[0, 2, y, x] = ((bgr.Item0 / 255.0f) - _config.image_mean[2]) / _config.image_std[2];
                }
            }
            return tensor;
        }

        private static Mat ResizeWithPadding(Mat source, int targetSize, out ResizeMetadata metadata)
        {
            double scale = Math.Min(
                targetSize / (double)Math.Max(source.Height, 1),
                targetSize / (double)Math.Max(source.Width, 1));
            int resizedHeight = Math.Max(1, (int)Math.Round(source.Height * scale));
            int resizedWidth = Math.Max(1, (int)Math.Round(source.Width * scale));
            int top = (targetSize - resizedHeight) / 2;
            int bottom = targetSize - resizedHeight - top;
            int left = (targetSize - resizedWidth) / 2;
            int right = targetSize - resizedWidth - left;

            using (var resized = new Mat())
            {
                Cv2.Resize(source, resized, new Size(resizedWidth, resizedHeight), 0, 0, InterpolationFlags.Linear);
                var padded = new Mat();
                Cv2.CopyMakeBorder(
                    resized,
                    padded,
                    top,
                    bottom,
                    left,
                    right,
                    BorderTypes.Constant,
                    Scalar.Black);
                metadata = new ResizeMetadata
                {
                    ResizedWidth = resizedWidth,
                    ResizedHeight = resizedHeight,
                    Top = top,
                    Left = left,
                    TargetSize = targetSize,
                };
                return padded;
            }
        }

        private static Mat RestoreProbabilityMap(
            Tensor<float> tensor,
            ResizeMetadata metadata,
            int originalWidth,
            int originalHeight)
        {
            int[] dimensions = tensor.Dimensions.ToArray();
            if (dimensions.Length != 4 || dimensions[0] != 1 || dimensions[1] != 1)
                throw new InvalidOperationException("Mask ONNX 출력은 [1,1,H,W] 형식이어야 합니다.");
            int outputHeight = dimensions[2];
            int outputWidth = dimensions[3];
            if (outputHeight != metadata.TargetSize || outputWidth != metadata.TargetSize)
                throw new InvalidOperationException("Mask ONNX 출력 크기가 입력 크기와 다릅니다.");

            using (var padded = new Mat(outputHeight, outputWidth, MatType.CV_32FC1))
            {
                for (int y = 0; y < outputHeight; y++)
                {
                    for (int x = 0; x < outputWidth; x++)
                        padded.Set(y, x, Clamp01(tensor[0, 0, y, x]));
                }

                var region = new Rect(
                    metadata.Left,
                    metadata.Top,
                    metadata.ResizedWidth,
                    metadata.ResizedHeight);
                using (Mat cropped = new Mat(padded, region))
                {
                    var restored = new Mat();
                    Cv2.Resize(
                        cropped,
                        restored,
                        new Size(originalWidth, originalHeight),
                        0,
                        0,
                        InterpolationFlags.Linear);
                    return restored;
                }
            }
        }

        private Mat BuildFinalMask(Mat probability)
        {
            double score = ComputePercentile(probability, _config.confidence_percentile);
            if (score < _config.confidence_threshold)
                return Mat.Zeros(probability.Rows, probability.Cols, MatType.CV_8UC1).ToMat();

            using (var thresholdedFloat = new Mat())
            {
                Cv2.Threshold(probability, thresholdedFloat, _config.mask_threshold, 255, ThresholdTypes.Binary);
                using (var rawMask = new Mat())
                {
                    thresholdedFloat.ConvertTo(rawMask, MatType.CV_8UC1);
                    using (Mat preservedHoles = _config.preserve_inner_holes
                        ? ExtractEnclosedHoles(rawMask, _config.min_hole_area)
                        : Mat.Zeros(rawMask.Rows, rawMask.Cols, MatType.CV_8UC1).ToMat())
                    {
                        Mat output = RemoveSmallComponents(rawMask, _config.min_component_area);
                        output = Replace(output, ApplyMorphology(output, _config.morph_open_kernel, _config.morph_close_kernel));
                        output = Replace(output, RemoveSmallComponents(output, _config.min_component_area));

                        if (_config.keep_largest_component && Cv2.CountNonZero(output) > 0)
                            output = Replace(output, KeepLargestComponent(output));

                        int recoverKernel = NormalizeKernelSize(_config.outer_recover_kernel);
                        if (recoverKernel > 0 && Cv2.CountNonZero(output) > 0)
                        {
                            using (Mat kernel = Cv2.GetStructuringElement(
                                MorphShapes.Ellipse,
                                new Size(recoverKernel, recoverKernel)))
                            {
                                Cv2.Dilate(output, output, kernel);
                            }
                            if (_config.keep_largest_component)
                                output = Replace(output, KeepLargestComponent(output));
                        }

                        if (_config.preserve_inner_holes && Cv2.CountNonZero(preservedHoles) > 0)
                            output.SetTo(Scalar.Black, preservedHoles);

                        return output;
                    }
                }
            }
        }

        private static Mat ApplyMorphology(Mat mask, int openKernelSize, int closeKernelSize)
        {
            var output = mask.Clone();
            int openKernel = NormalizeKernelSize(openKernelSize);
            int closeKernel = NormalizeKernelSize(closeKernelSize);

            if (openKernel > 0)
            {
                using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(openKernel, openKernel)))
                    Cv2.MorphologyEx(output, output, MorphTypes.Open, kernel);
            }
            if (closeKernel > 0)
            {
                using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(closeKernel, closeKernel)))
                    Cv2.MorphologyEx(output, output, MorphTypes.Close, kernel);
            }
            return output;
        }

        private static Mat RemoveSmallComponents(Mat mask, int minArea)
        {
            if (minArea <= 0)
                return mask.Clone();

            ConnectedComponents components = AnalyzeComponents(mask);
            using (components)
            {
                var output = Mat.Zeros(mask.Rows, mask.Cols, MatType.CV_8UC1).ToMat();
                for (int label = 1; label < components.Count; label++)
                {
                    if (components.Stats.At<int>(label, 4) >= minArea)
                        CopyLabelToMask(components.Labels, label, output);
                }
                return output;
            }
        }

        private static Mat KeepLargestComponent(Mat mask)
        {
            ConnectedComponents components = AnalyzeComponents(mask);
            using (components)
            {
                if (components.Count <= 1)
                    return mask.Clone();

                int largestLabel = 1;
                int largestArea = components.Stats.At<int>(1, 4);
                for (int label = 2; label < components.Count; label++)
                {
                    int area = components.Stats.At<int>(label, 4);
                    if (area > largestArea)
                    {
                        largestArea = area;
                        largestLabel = label;
                    }
                }

                var output = Mat.Zeros(mask.Rows, mask.Cols, MatType.CV_8UC1).ToMat();
                CopyLabelToMask(components.Labels, largestLabel, output);
                return output;
            }
        }

        private static Mat ExtractEnclosedHoles(Mat mask, int minHoleArea)
        {
            using (var inverted = new Mat())
            {
                Cv2.BitwiseNot(mask, inverted);
                ConnectedComponents components = AnalyzeComponents(inverted);
                using (components)
                {
                    var borderLabels = new HashSet<int>();
                    for (int x = 0; x < components.Labels.Cols; x++)
                    {
                        borderLabels.Add(components.Labels.At<int>(0, x));
                        borderLabels.Add(components.Labels.At<int>(components.Labels.Rows - 1, x));
                    }
                    for (int y = 0; y < components.Labels.Rows; y++)
                    {
                        borderLabels.Add(components.Labels.At<int>(y, 0));
                        borderLabels.Add(components.Labels.At<int>(y, components.Labels.Cols - 1));
                    }

                    var holes = Mat.Zeros(mask.Rows, mask.Cols, MatType.CV_8UC1).ToMat();
                    for (int label = 1; label < components.Count; label++)
                    {
                        if (!borderLabels.Contains(label)
                            && components.Stats.At<int>(label, 4) >= minHoleArea)
                        {
                            CopyLabelToMask(components.Labels, label, holes);
                        }
                    }
                    return holes;
                }
            }
        }

        private static ConnectedComponents AnalyzeComponents(Mat mask)
        {
            var labels = new Mat();
            var stats = new Mat();
            var centroids = new Mat();
            int count = Cv2.ConnectedComponentsWithStats(
                mask,
                labels,
                stats,
                centroids,
                PixelConnectivity.Connectivity8,
                MatType.CV_32SC1);
            return new ConnectedComponents(count, labels, stats, centroids);
        }

        private static void CopyLabelToMask(Mat labels, int label, Mat destination)
        {
            using (var selected = new Mat())
            {
                Cv2.Compare(labels, label, selected, CmpTypes.EQ);
                destination.SetTo(Scalar.White, selected);
            }
        }

        private static double ComputePercentile(Mat probability, double percentile)
        {
            int count = probability.Rows * probability.Cols;
            if (count == 0)
                return 0d;

            var values = new float[count];
            int index = 0;
            for (int y = 0; y < probability.Rows; y++)
            {
                for (int x = 0; x < probability.Cols; x++)
                    values[index++] = probability.At<float>(y, x);
            }
            Array.Sort(values);

            double position = (values.Length - 1) * (percentile / 100d);
            int lower = (int)Math.Floor(position);
            int upper = (int)Math.Ceiling(position);
            if (lower == upper)
                return values[lower];
            double fraction = position - lower;
            return values[lower] + ((values[upper] - values[lower]) * fraction);
        }

        private static int NormalizeKernelSize(int value)
        {
            if (value <= 0)
                return 0;
            if (value % 2 == 0)
                value++;
            return Math.Max(3, value);
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }

        private static Mat Replace(Mat previous, Mat replacement)
        {
            previous.Dispose();
            return replacement;
        }

        private static string BuildUniqueOutputPath(
            string outputDir,
            string rawImagePath,
            ISet<string> reservedOutputs)
        {
            string stem = Path.GetFileNameWithoutExtension(rawImagePath);
            string candidate = Path.Combine(outputDir, stem + "_masked.bmp");
            int suffix = 1;
            while (!reservedOutputs.Add(candidate))
            {
                candidate = Path.Combine(outputDir, stem + "_" + suffix.ToString("000") + "_masked.bmp");
                suffix++;
            }
            return candidate;
        }

        private sealed class ResizeMetadata
        {
            public int ResizedWidth { get; set; }
            public int ResizedHeight { get; set; }
            public int Top { get; set; }
            public int Left { get; set; }
            public int TargetSize { get; set; }
        }

        private sealed class ConnectedComponents : IDisposable
        {
            public int Count { get; }
            public Mat Labels { get; }
            public Mat Stats { get; }
            private Mat Centroids { get; }

            public ConnectedComponents(int count, Mat labels, Mat stats, Mat centroids)
            {
                Count = count;
                Labels = labels;
                Stats = stats;
                Centroids = centroids;
            }

            public void Dispose()
            {
                Labels.Dispose();
                Stats.Dispose();
                Centroids.Dispose();
            }
        }
    }
}
