using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CoilTrainingUI.Services
{
    public class AnomaWorkspaceService
    {
        private readonly ImageStateService _stateService;

        public AnomaWorkspaceService(ImageStateService stateService)
        {
            _stateService = stateService;
        }

        /// <summary>
        /// anomalib 학습용 workspace 생성
        /// - 정상(IsNormal=true)만 사용
        /// - ROI 전처리된 이미지(_roi_processed) 우선 복사
        /// - train/val split만 구성
        /// </summary>
        public AnomaWorkspaceResult BuildWorkspace(
            IEnumerable<string> imagePaths,
            string runRootDir,
            double trainRatio = 0.8,
            double valRatio = 0.2,
            int seed = 42,
            bool useRoiProcessedImages = true
        )
        {
            if (imagePaths == null) throw new ArgumentNullException(nameof(imagePaths));

            var all = imagePaths
                .Where(p => !string.IsNullOrWhiteSpace(p) && File.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (all.Count == 0)
                throw new InvalidOperationException("No valid images.");

            if (trainRatio <= 0 || valRatio <= 0 || Math.Abs((trainRatio + valRatio) - 1.0) > 1e-6)
                throw new ArgumentException("trainRatio + valRatio must equal 1.0 (e.g., 0.8 + 0.2).");

            // ✅ 정상만 필터
            var normalOnly = all
                .Where(p => (_stateService.Load(p).IsNormal ?? true) == true)
                .ToList();

            if (normalOnly.Count < 2)
                throw new InvalidOperationException("Not enough normal images to split train/val (need at least 2).");

            // run 폴더
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string wsRoot = Path.Combine(runRootDir, $"run_{stamp}_anoma");
            string trainDir = Path.Combine(wsRoot, "train");
            string valDir = Path.Combine(wsRoot, "val");

            Directory.CreateDirectory(trainDir);
            Directory.CreateDirectory(valDir);

            // split
            var rng = new Random(seed);
            var shuffled = normalOnly.OrderBy(_ => rng.Next()).ToList();
            int trainCount = (int)Math.Round(shuffled.Count * trainRatio);
            trainCount = Math.Clamp(trainCount, 1, shuffled.Count - 1);

            var trainSet = shuffled.Take(trainCount).ToList();
            var valSet = shuffled.Skip(trainCount).ToList();

            int copied = 0;

            foreach (var src in trainSet)
            {
                CopyImage(src, trainDir, useRoiProcessedImages);
                copied++;
            }

            foreach (var src in valSet)
            {
                CopyImage(src, valDir, useRoiProcessedImages);
                copied++;
            }

            return new AnomaWorkspaceResult
            {
                WorkspaceRoot = wsRoot,
                TrainImageCount = trainSet.Count,
                ValImageCount = valSet.Count,
                TotalCopiedImages = copied,
                TotalNormalCandidates = normalOnly.Count
            };
        }

        private void CopyImage(string originalImagePath, string dstDir, bool useRoiProcessedImages)
        {
            string src = originalImagePath;

            if (useRoiProcessedImages)
            {
                string roiDir = Path.Combine(Path.GetDirectoryName(originalImagePath)!, "_roi_processed");
                string roiCandidate = Path.Combine(roiDir, Path.GetFileName(originalImagePath));
                if (File.Exists(roiCandidate))
                    src = roiCandidate;
            }

            string dst = Path.Combine(dstDir, Path.GetFileName(src));
            File.Copy(src, dst, overwrite: true);
        }
    }

    public class AnomaWorkspaceResult
    {
        public string WorkspaceRoot { get; set; } = "";
        public int TrainImageCount { get; set; }
        public int ValImageCount { get; set; }
        public int TotalCopiedImages { get; set; }
        public int TotalNormalCandidates { get; set; }
    }
}
