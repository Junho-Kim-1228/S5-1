using CoilTrainingUI.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace CoilTrainingUI.Services
{
    public class DatasetExportService
    {
        public string ExportAnomalyDataset(IEnumerable<ImageItem> images, string projectRoot)
        {
            // ===============================
            // 1. Dataset 디렉토리 준비
            // ===============================
            string baseDir = IOPath.Combine(projectRoot, "datasets", "anomaly");
            string trainDir = IOPath.Combine(baseDir, "train");
            string valDir = IOPath.Combine(baseDir, "val");
            string testDir = IOPath.Combine(baseDir, "test");

            Directory.CreateDirectory(trainDir);
            Directory.CreateDirectory(valDir);
            Directory.CreateDirectory(testDir);

            // ROI 마스크 리소스 경로
            string roiDir = IOPath.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "ROI"
            );

            int normalIndex = 0;

            // ===============================
            // 2. 이미지 순회
            // ===============================
            foreach (var item in images)
            {
                if (!File.Exists(item.FullPath))
                    continue;

                string fileName = IOPath.GetFileName(item.FullPath);
                string destPath;

                // -------------------------------
                // 저장 위치 결정
                // -------------------------------
                if (item.IsNormal)
                {
                    destPath = (normalIndex++ % 5 == 0)
                        ? IOPath.Combine(valDir, fileName)
                        : IOPath.Combine(trainDir, fileName);
                }
                else
                {
                    destPath = IOPath.Combine(testDir, fileName);
                }

                // -------------------------------
                // ROI 적용 여부 판단
                // -------------------------------
                if (item.RoiType == RoiType.None)
                {
                    // ROI 없으면 원본 그대로
                    File.Copy(item.FullPath, destPath, overwrite: true);
                    continue;
                }

                string roiFile = $"roi_{item.RoiType}.bmp";
                string roiPath = IOPath.Combine(roiDir, roiFile);

                if (!File.Exists(roiPath))
                {
                    // ROI 파일 없으면 fallback
                    File.Copy(item.FullPath, destPath, overwrite: true);
                    continue;
                }

                // -------------------------------
                // ROI 마스킹 저장
                // -------------------------------
                SaveWithRoi(item.FullPath, roiPath, destPath);
            }

            return baseDir;
        }

        // ======================================================
        // 🔥 핵심: ROI 마스크 기반 픽셀 단위 마스킹
        // ======================================================
        private void SaveWithRoi(string originalPath, string maskPath, string savePath)
        {
            try
            {
                // 원본 / 마스크 로드
                var original = LoadBitmap(originalPath);
                var mask = LoadBitmap(maskPath);

                int width = original.PixelWidth;
                int height = original.PixelHeight;

                if (mask.PixelWidth != width || mask.PixelHeight != height)
                {
                    // 크기 다르면 위험 → fallback
                    File.Copy(originalPath, savePath, true);
                    return;
                }

                var srcPixels = new byte[width * height * 4];
                var maskPixels = new byte[width * height * 4];
                var outPixels = new byte[width * height * 4];

                original.CopyPixels(srcPixels, width * 4, 0);
                mask.CopyPixels(maskPixels, width * 4, 0);

                // -------------------------------
                // 픽셀 단위 마스킹
                // -------------------------------
                for (int i = 0; i < width * height; i++)
                {
                    int idx = i * 4;

                    // 마스크가 흰색이면 유지
                    bool inside =
                        maskPixels[idx] > 0 ||
                        maskPixels[idx + 1] > 0 ||
                        maskPixels[idx + 2] > 0;

                    if (inside)
                    {
                        outPixels[idx] = srcPixels[idx];     // B
                        outPixels[idx + 1] = srcPixels[idx + 1]; // G
                        outPixels[idx + 2] = srcPixels[idx + 2]; // R
                        outPixels[idx + 3] = 255;
                    }
                    else
                    {
                        // ROI 밖 → 완전 검정
                        outPixels[idx] = 0;
                        outPixels[idx + 1] = 0;
                        outPixels[idx + 2] = 0;
                        outPixels[idx + 3] = 255;
                    }
                }

                var result = BitmapSource.Create(
                    width,
                    height,
                    original.DpiX,
                    original.DpiY,
                    PixelFormats.Bgra32,
                    null,
                    outPixels,
                    width * 4
                );

                SaveBitmap(result, savePath);
            }
            catch
            {
                // 어떤 에러든 안전하게 원본 복사
                File.Copy(originalPath, savePath, true);
            }
        }

        // ===============================
        // Bitmap 로드 유틸
        // ===============================
        private BitmapImage LoadBitmap(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        // ===============================
        // Bitmap 저장 유틸
        // ===============================
        private void SaveBitmap(BitmapSource bitmap, string path)
        {
            BitmapEncoder encoder =
                path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                ? new JpegBitmapEncoder()
                : new PngBitmapEncoder();

            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }
    }
}
