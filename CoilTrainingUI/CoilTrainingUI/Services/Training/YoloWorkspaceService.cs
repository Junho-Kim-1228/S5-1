using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Services
{
    /// <summary>
    /// Train 버튼 클릭 시 사용할 "작업용 workspace" 생성 서비스
    /// - 원본 폴더는 건드리지 않음
    /// - state.json을 SSOT로 사용
    /// - YOLO 학습용으로 images/labels + data.yaml 생성
    /// </summary>
    public class YoloWorkspaceService
    {
        private readonly ImageStateService _stateService;

        public YoloWorkspaceService(ImageStateService stateService)
        {
            _stateService = stateService;
        }

        public YoloWorkspaceResult BuildYoloWorkspace(
            IEnumerable<string> imagePaths,
            string runRootDir,
            double trainRatio = 0.8,
            double valRatio = 0.2,
            int seed = 42
        )
        {
            if (imagePaths == null) throw new ArgumentNullException(nameof(imagePaths));

            var images = imagePaths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (images.Count == 0)
                throw new InvalidOperationException("No valid images to build workspace.");

            if (trainRatio <= 0 || valRatio <= 0 || Math.Abs((trainRatio + valRatio) - 1.0) > 1e-6)
                throw new ArgumentException("trainRatio + valRatio must equal 1.0 (e.g., 0.8 + 0.2).");

            // 1) run 폴더 생성
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string wsRoot = Path.Combine(runRootDir, $"run_{stamp}_yolo");
            string imagesTrainDir = Path.Combine(wsRoot, "images", "train");
            string imagesValDir = Path.Combine(wsRoot, "images", "val");
            string labelsTrainDir = Path.Combine(wsRoot, "labels", "train");
            string labelsValDir = Path.Combine(wsRoot, "labels", "val");

            Directory.CreateDirectory(imagesTrainDir);
            Directory.CreateDirectory(imagesValDir);
            Directory.CreateDirectory(labelsTrainDir);
            Directory.CreateDirectory(labelsValDir);

            // 2) split
            var rng = new Random(seed);
            var shuffled = images.OrderBy(_ => rng.Next()).ToList();
            int trainCount = (int)Math.Round(shuffled.Count * trainRatio);
            trainCount = Math.Clamp(trainCount, 1, shuffled.Count - 1);

            var trainSet = shuffled.Take(trainCount).ToList();
            var valSet = shuffled.Skip(trainCount).ToList();

            // 3) 복사 + 라벨 생성
            int copied = 0;
            int labeled = 0;
            int skippedNoLabels = 0;

            foreach (var srcImagePath in trainSet)
            {
                var state = _stateService.Load(srcImagePath);
                string dstImagePath = CopyImageForWorkspace(srcImagePath, imagesTrainDir);
                copied++;

                string dstLabelPath = Path.Combine(labelsTrainDir, Path.GetFileNameWithoutExtension(dstImagePath) + ".txt");
                if (WriteYoloLabelTxt(dstLabelPath, state))
                    labeled++;
                else
                    skippedNoLabels++;
            }

            foreach (var srcImagePath in valSet)
            {
                var state = _stateService.Load(srcImagePath);
                string dstImagePath = CopyImageForWorkspace(srcImagePath, imagesValDir);
                copied++;

                string dstLabelPath = Path.Combine(labelsValDir, Path.GetFileNameWithoutExtension(dstImagePath) + ".txt");
                if (WriteYoloLabelTxt(dstLabelPath, state))
                    labeled++;
                else
                    skippedNoLabels++;
            }

            // 4) data.yaml 생성
            // Ultralytics가 요구하는 값: train/val 경로, nc, names
            // names는 classId 순서대로. 우리는 dent=0, loose=1 고정.
            string dataYamlPath = Path.Combine(wsRoot, "data.yaml");
            WriteDataYaml(dataYamlPath, imagesTrainDir, imagesValDir);

            return new YoloWorkspaceResult
            {
                WorkspaceRoot = wsRoot,
                DataYamlPath = dataYamlPath,
                TrainImageCount = trainSet.Count,
                ValImageCount = valSet.Count,
                TotalCopiedImages = copied,
                TotalCreatedLabelFiles = labeled,
                TotalImagesWithoutLabels = skippedNoLabels
            };
        }

        // ----------------- 내부 헬퍼 -----------------

        private string CopyImageForWorkspace(string originalImagePath, string dstImagesDir)
        {
            string dst = Path.Combine(dstImagesDir, Path.GetFileName(originalImagePath));
            try
            {
                File.Copy(originalImagePath, dst, overwrite: true);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException(
                    "YOLO workspace 이미지 복사 실패\n" +
                    $"원본: {originalImagePath}\n" +
                    $"대상: {dst}\n" +
                    "디스크 여유 공간 또는 파일 접근 상태를 확인하세요.",
                    ex);
            }
            return dst;
        }

        /// <summary>
        /// state.json(LabelDto: ClassName + normalized xywh) -> YOLO txt 생성
        /// - 라벨이 0개면 빈 txt를 만들지 않고 false 반환 (정책)
        /// </summary>
        private bool WriteYoloLabelTxt(string txtPath, ImageStateDto state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (state.Labels == null || state.Labels.Count == 0)
            {
                // 정책: 라벨 없는 이미지는 txt를 만들지 않음
                // (원하면 빈 파일 생성으로 바꿔도 됨)
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(txtPath)!);

            var lines = new List<string>(state.Labels.Count);
            foreach (var l in state.Labels)
            {
                // className -> classId 변환 (확인 불필요, 규칙 고정)
                int classId = ToClassId(l.ClassName);

                // YOLO 형식: class xc yc w h
                // 모두 normalized(0~1)라고 가정
                string line = string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} {1:F6} {2:F6} {3:F6} {4:F6}",
                    classId, l.X, l.Y, l.Width, l.Height
                );
                lines.Add(line);
            }

            File.WriteAllLines(txtPath, lines, Encoding.UTF8);
            return true;
        }

        private int ToClassId(string className)
        {
            // 대소문자/공백 방어
            var c = (className ?? "").Trim().ToLowerInvariant();
            return c switch
            {
                "dent" => 0,
                "loose" => 1,
                _ => throw new InvalidOperationException($"Unknown ClassName in state.json: '{className}'")
            };
        }

        private void WriteDataYaml(string yamlPath, string trainImagesDir, string valImagesDir)
        {
            // Ultralytics data.yaml은 보통 images 디렉토리를 직접 가리켜도 동작합니다.
            // (절대경로도 가능)
            var sb = new StringBuilder();
            sb.AppendLine($"train: {EscapePathForYaml(trainImagesDir)}");
            sb.AppendLine($"val: {EscapePathForYaml(valImagesDir)}");
            sb.AppendLine("nc: 2");
            sb.AppendLine("names: [dent, loose]");

            File.WriteAllText(yamlPath, sb.ToString(), Encoding.UTF8);
        }

        private string EscapePathForYaml(string path)
        {
            // 공백/특수문자 대비: 따옴표로 감싸기
            return $"\"{path.Replace("\\", "/")}\"";
        }
    }

    public class YoloWorkspaceResult
    {
        public string WorkspaceRoot { get; set; } = "";
        public string DataYamlPath { get; set; } = "";

        public int TrainImageCount { get; set; }
        public int ValImageCount { get; set; }

        public int TotalCopiedImages { get; set; }
        public int TotalCreatedLabelFiles { get; set; }
        public int TotalImagesWithoutLabels { get; set; }
    }
}
