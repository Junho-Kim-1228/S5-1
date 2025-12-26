using System;
using OpenCvSharp; // NuGet으로 설치한 OpenCV 라이브러리

namespace CoilInspectionApp.Preprocess
{
    public class ImageProcessor
    {
        // 이미지를 로드하고 모델 입력 크기에 맞춰 변환하는 함수
        public Mat PrepareImage(string imagePath, int width, int height)
        {
            try
            {
                // 1. 이미지 로드
                Mat src = new Mat(imagePath, ImreadModes.Color);
                if (src.Empty()) return null;

                // 2. 리사이즈 (예: 640x640)
                Mat resized = new Mat();
                Cv2.Resize(src, resized, new Size(width, height));

                // 3. BGR을 RGB로 변환 (ONNX 모델은 보통 RGB를 사용합니다)
                Mat rgbImage = new Mat();
                Cv2.CvtColor(resized, rgbImage, ColorConversionCodes.BGR2RGB);

                return rgbImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"이미지 처리 오류: {ex.Message}");
                return null;
            }
        }
    }
}