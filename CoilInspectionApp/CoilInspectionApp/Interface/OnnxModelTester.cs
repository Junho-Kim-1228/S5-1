using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace CoilInspectionApp.Inference
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
        public void SimpleInferenceTest()
        {
            if (_session == null) return;

            var inputMeta = _session.InputMetadata;
            string inputname = inputMeta.Keys.First();
            Console.WriteLine($"모델 입력 이름:{inputname}");

        }
    }
}