using System;
using System.Collections.Generic;
using System.IO;
using OpenCvSharp;

namespace CoilInspectionApp.Preprocess
{
    public class ImageProcessor
    {
        // 1. 마스크 캐시 (Dictionary) - 프로그램 시작 시 로드 권장
        private readonly Dictionary<string, Mat> _maskCache = new Dictionary<string, Mat>();

        public ImageProcessor(string maskFolderPath)
        {
            // 마스크 파일들을 미리 메모리에 로드 (A=1, B=2, C=3)
            // 파일 경로는 실제 환경에 맞춰 조정하세요.
            LoadMask(maskFolderPath, "1", "mask_type_A.bmp");
            LoadMask(maskFolderPath, "2", "mask_type_B.bmp");
            LoadMask(maskFolderPath, "3", "mask_type_C.bmp");
        }

        private void LoadMask(string folder, string key, string fileName)
        {
            string fullPath = Path.Combine(folder, fileName);
            if (File.Exists(fullPath))
                _maskCache.Add(key, Cv2.ImRead(fullPath, ImreadModes.Grayscale));
        }

        public Mat PrepareImage(string imagePath, int width, int height)
        {
            try
            {
                // [1] 이미지 로드
                Mat src = new Mat(imagePath, ImreadModes.Color);
                if (src.Empty()) return null;

                // [2] 파일명 기반 마스크 적용 (추가된 부분)
                string fileName = Path.GetFileNameWithoutExtension(imagePath);
                string typeKey = fileName.Substring(fileName.Length - 1); // 끝자리 추출

                Mat masked = new Mat();
                if (_maskCache.ContainsKey(typeKey))
                {
                    // 마스크 크기가 원본과 다를 수 있으므로 체크 후 적용
                    using var currentMask = new Mat();
                    Cv2.Resize(_maskCache[typeKey], currentMask, src.Size());
                    Cv2.BitwiseAnd(src, src, masked, currentMask);
                }
                else
                {
                    masked = src.Clone(); // 타입이 없으면 원본 유지
                }

                // [3] 리사이즈 (마스크 적용 후 진행)
                Mat resized = new Mat();
                Cv2.Resize(masked, resized, new Size(width, height));

                // [4] BGR을 RGB로 변환
                Mat rgbImage = new Mat();
                Cv2.CvtColor(resized, rgbImage, ColorConversionCodes.BGR2RGB);

                // 메모리 해제
                src.Dispose();
                masked.Dispose();

                return rgbImage;
            }
            catch (Exception ex)
            {
                [cite_start]// 요구사항 8: 예외 발생 시 로그 기록 
                Console.WriteLine($"이미지 처리 오류 ({imagePath}): {ex.Message}");
                return null;
            }
        }
    }
}