using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CoilTrainingUI.Models;
using IOPath = System.IO.Path;

namespace CoilTrainingUI.Services
{
    public class RoiPreprocessService
    {
        private readonly string _roiDir;

        public RoiPreprocessService()
        {
            // 실행 폴더 기준 Resources/ROI
            _roiDir = IOPath.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Resources",
                "ROI"
            );
        }

        /// <summary>
        /// ROI 적용된 이미지를 생성하고 디스크에 저장한 뒤 BitmapSource 반환
        /// </summary>
        public BitmapSource GetOrCreateProcessedImage(
            string originalImagePath,
            RoiType roiType
        )
        {
            // ROI 없음 → 원본 그대로 반환
            if (roiType == RoiType.None)
                return LoadBitmap(originalImagePath);

            string processedPath = GetProcessedPath(originalImagePath, roiType);

            // 이미 만들어졌으면 재사용
            if (File.Exists(processedPath))
                return LoadBitmap(processedPath);

            // 새로 생성
            var processed = ApplyRoi(originalImagePath, roiType);

            SaveBitmap(processed, processedPath);

            return processed;
        }

        // ---------------- 내부 구현 ----------------

        private BitmapSource ApplyRoi(string originalPath, RoiType roiType)
        {
            string roiPath = IOPath.Combine(_roiDir, $"roi_{roiType}.bmp");

            if (!File.Exists(roiPath))
                throw new FileNotFoundException($"ROI mask not found: {roiPath}");

            var original = LoadBitmap(originalPath);
            var mask = LoadBitmap(roiPath);

            if (original.PixelWidth != mask.PixelWidth ||
                original.PixelHeight != mask.PixelHeight)
                throw new InvalidOperationException("ROI mask size mismatch");

            int width = original.PixelWidth;
            int height = original.PixelHeight;

            var originalPixels = new byte[width * height * 4];
            var maskPixels = new byte[width * height * 4];
            var outPixels = new byte[width * height * 4];

            original.CopyPixels(originalPixels, width * 4, 0);
            mask.CopyPixels(maskPixels, width * 4, 0);

            for (int i = 0; i < outPixels.Length; i += 4)
            {
                bool insideRoi = maskPixels[i] > 0; // 흰색이면 ROI

                if (insideRoi)
                {
                    outPixels[i + 0] = originalPixels[i + 0]; // B
                    outPixels[i + 1] = originalPixels[i + 1]; // G
                    outPixels[i + 2] = originalPixels[i + 2]; // R
                    outPixels[i + 3] = 255;
                }
                else
                {
                    outPixels[i + 0] = 0;
                    outPixels[i + 1] = 0;
                    outPixels[i + 2] = 0;
                    outPixels[i + 3] = 255;
                }
            }

            return BitmapSource.Create(
                width,
                height,
                original.DpiX,
                original.DpiY,
                PixelFormats.Bgra32,
                null,
                outPixels,
                width * 4
            );
        }

        private static BitmapSource LoadBitmap(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        private static void SaveBitmap(BitmapSource bitmap, string path)
        {
            Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);

            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);
        }

        private static string GetProcessedPath(string originalPath, RoiType roiType)
        {
            string dir = IOPath.Combine(
                IOPath.GetDirectoryName(originalPath)!,
                "_roi_processed"
            );

            string name = IOPath.GetFileNameWithoutExtension(originalPath);
            return IOPath.Combine(dir, $"{name}_{roiType}.png");
        }
    }
}
