using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp; // 이미지 처리를 위해 추가

namespace CoilInspectionApp.Interface
{
    public class OnnxModelTester
    {
        private InferenceSession _session;

        public void LoadModel(string modelPath)
        {
            try
            {
                _session = new InferenceSession(modelPath);
                Console.WriteLine("모델 로드 성공!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"모델 로드 실패:{ex.Message}");
            }
        }

        // --- 세세한 수정 포인트: 실제 추론 함수 추가 ---
        public float[] RunInference(Mat image)
        {
            if (_session == null || image == null) return null;

            // 1. 모델 입력 정보 가져오기 (예: 1x3x640x640)
            var inputMeta = _session.InputMetadata;
            string inputName = inputMeta.Keys.First();
            int[] dimensions = inputMeta[inputName].Dimensions; // [1, 3, 640, 640] 등

            int width = dimensions[3];
            int height = dimensions[2];

            // 2. Mat 데이터를 텐서(Tensor)로 변환 (0~1 사이 값으로 정규화)
            var inputTensor = ExtractPixels(image, width, height);

            // 3. 모델 실행
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputName, inputTensor)
            };

            using (var results = _session.Run(inputs))
            {
                // 4. 결과값 추출 (보통 불량 확률 값이 나옵니다)
                var output = results.First().AsEnumerable<float>().ToArray();
                return output;
            }
        }

        // 이미지를 숫자 배열(Tensor)로 바꾸는 핵심 도우미 함수
        private DenseTensor<float> ExtractPixels(Mat image, int width, int height)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, height, width });

            // OpenCvSharp의 데이터를 텐서에 채우기 (정규화 포함)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var color = image.At<Vec3b>(y, x);
                    // RGB 순서로 0~1 사이 값으로 변환
                    tensor[0, 0, y, x] = color.Item0 / 255.0f; // R
                    tensor[0, 1, y, x] = color.Item1 / 255.0f; // G
                    tensor[0, 2, y, x] = color.Item2 / 255.0f; // B
                }
            }
            return tensor;
        }

        public void SimpleInferenceTest()
        {
            if (_session == null) return;
            var inputMeta = _session.InputMetadata;
            string inputname = inputMeta.Keys.First();
            Console.WriteLine($"모델 입력 이름:{inputname}");
        }
    }
}