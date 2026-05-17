using System;
using OpenCvSharp; 

namespace CoilInspectionApp.Preprocess
{
    public class ImageProcessor
    {
        // --- 1.전처리 로직에서 가져온 상수 설정 ---
        private const double ENERGY_KERNEL_RATIO = 0.005;
        private const double OPEN_KERNEL_RATIO = 0.006;
        private const double CLOSE_KERNEL_RATIO = 0.012;
        private const double RECON_KERNEL_RATIO = 0.006;
        private const double FINAL_OPEN_KERNEL_RATIO = 0.004;
        private const double MIN_CONTOUR_AREA_RATIO = 0.01;
        private const double BORDER_PENALTY = 0.35;

        // --- 2. 메인 실행 함수 (외부에서 호출하는 함수) ---
        public Mat PrepareImage(string imagePath, int width, int height)
        {
            return PrepareModelInput(imagePath, width, height);
        }

        public Mat PrepareModelInput(string imagePath, int width, int height)
        {
            try
            {
                // 1. 이미지 로드
                using (Mat src = new Mat(imagePath, ImreadModes.Color))
                {
                    if (src.Empty()) return null;

                    // 2. 질감 분석 전처리 (배경 제거)
                    using (Mat coilOnly = ApplyTextureMask(src))
                    {
                        // 3. 모델 입력 크기에 맞춰 리사이즈 (예: 640x640)
                        Mat resized = new Mat();
                        Cv2.Resize(coilOnly, resized, new Size(width, height));

                        // 4. BGR을 RGB로 변환 (ONNX 모델용)
                        Mat rgbImage = new Mat();
                        Cv2.CvtColor(resized, rgbImage, ColorConversionCodes.BGR2RGB);

                        return rgbImage;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"이미지 처리 오류: {ex.Message}");
                return null;
            }
        }

        public Mat PrepareDisplayImage(string imagePath, int width, int height)
        {
            try
            {
                using (Mat src = new Mat(imagePath, ImreadModes.Color))
                {
                    if (src.Empty()) return null;

                    using (Mat coilOnly = ApplyTextureMask(src))
                    {
                        Mat resized = new Mat();
                        Cv2.Resize(coilOnly, resized, new Size(width, height));
                        return resized;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"이미지 처리 오류: {ex.Message}");
                return null;
            }
        }

        public Mat PrepareExistingMaskedDisplayImage(string imagePath, int width, int height)
        {
            try
            {
                using (Mat src = new Mat(imagePath, ImreadModes.Color))
                {
                    if (src.Empty()) return null;

                    Mat resized = new Mat();
                    Cv2.Resize(src, resized, new Size(width, height));
                    return resized;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"이미지 처리 오류: {ex.Message}");
                return null;
            }
        }

        public Mat PrepareExistingMaskedModelInput(string imagePath, int width, int height)
        {
            try
            {
                using (Mat src = new Mat(imagePath, ImreadModes.Color))
                {
                    if (src.Empty()) return null;

                    Mat resized = new Mat();
                    Cv2.Resize(src, resized, new Size(width, height));

                    Mat rgbImage = new Mat();
                    Cv2.CvtColor(resized, rgbImage, ColorConversionCodes.BGR2RGB);
                    resized.Dispose();

                    return rgbImage;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"이미지 처리 오류: {ex.Message}");
                return null;
            }
        }

        // --- 3. 질감 분석 기반 코일 마스킹 (파이썬 apply_texture_mask 대응) ---
        private Mat ApplyTextureMask(Mat src)
        {
            int h = src.Height;
            int w = src.Width;
            int shortSide = Math.Min(h, w);

            // A. 가로/세로 미분(Sobel)을 통한 에너지 지도 생성
            using (Mat gray = src.CvtColor(ColorConversionCodes.BGR2GRAY))
            using (Mat sobelX = new Mat())
            using (Mat sobelY = new Mat())
            using (Mat magnitude = new Mat())
            {
                Cv2.Sobel(gray, sobelX, MatType.CV_32F, 1, 0, 3);
                Cv2.Sobel(gray, sobelY, MatType.CV_32F, 0, 1, 3);
                Cv2.Magnitude(sobelX, sobelY, magnitude);

                // B. 박스 필터로 에너지를 부드럽게 뭉침
                int energyK = OddInt((int)(shortSide * ENERGY_KERNEL_RATIO));
                using (Mat energy = magnitude.BoxFilter(-1, new Size(energyK, energyK)))
                using (Mat energy8u = new Mat())
                {
                    Cv2.Normalize(energy, energy8u, 0, 255, NormTypes.MinMax, MatType.CV_8U);

                    // C. 오츠(Otsu) 이진화로 코일 영역 후보 추출
                    Mat mask = energy8u.Threshold(0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

                    // D. 모폴로지(Open/Close)로 노이즈 제거
                    using (Mat kOpen = GetKernel(shortSide, OPEN_KERNEL_RATIO))
                    using (Mat kClose = GetKernel(shortSide, CLOSE_KERNEL_RATIO))
                    {
                        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kOpen);
                        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, kClose);
                    }

                    // E. 최종 마스크 적용 (코일 부분만 복사)
                    Mat result = new Mat(src.Size(), src.Type(), Scalar.All(0));
                    src.CopyTo(result, mask);

                    mask.Dispose();
                    return result;
                }
            }
        }

        // --- 4. 도우미 함수 (Helper Functions) ---
        private int OddInt(int v, int minValue = 3)
        {
            int x = Math.Max(minValue, v);
            return (x % 2 == 0) ? x + 1 : x;
        }

        private Mat GetKernel(int shortSide, double ratio)
        {
            int k = OddInt((int)(shortSide * ratio));
            return Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(k, k));
        }
    }
}
