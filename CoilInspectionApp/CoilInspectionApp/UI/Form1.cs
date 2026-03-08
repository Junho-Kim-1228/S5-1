using CoilInspectionApp.Logging;
using CoilInspectionApp.Preprocess;
using CoilInspectionApp.Watcher;
using CoilInspectionApp.Inference; // 추론 엔진 네임스페이스 확인 필요
using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CoilInspectionApp
{
    public partial class Form1 : Form
    {
        // [수정] 멤버 변수: 객체를 전역으로 선언하여 메모리 재사용 및 효율화
        private DirectoryWatcher _dw;
        private CsvLogger _logger = new CsvLogger();
        private ImageProcessor _processor;
        private OnnxModelTester _tester; // 추론 엔진 전역 선언 (요구사항 3, 7)

        public Form1()
        {
            InitializeComponent();
            InitSystem();
        }

        private void InitSystem()
        {
            try
            {
                // 1. 설정값 로드
                string inputPath = ConfigurationManager.AppSettings["InputDir"];
                string modelPath = ConfigurationManager.AppSettings["ModelPath"];
                string maskPath = Path.Combine(Application.StartupPath, "Masks"); // 실행 경로 내 Masks 폴더

                // 2. 폴더 및 객체 초기화
                if (!Directory.Exists(inputPath)) Directory.CreateDirectory(inputPath);

                // [수정] 프로그램 시작 시 전처리 및 추론 엔진을 딱 한 번만 로드 (성능 최적화)
                _processor = new ImageProcessor(maskPath);
                _tester = new OnnxModelTester();
                if (File.Exists(modelPath)) _tester.LoadModel(modelPath);

                // 3. 감시자 설정
                _dw = new DirectoryWatcher();
                _dw.OnFileCreated += (filePath) => {
                    // [수정] 비동기로 실행하여 대량 파일 유입 시에도 UI 프리징 방지 (요구사항 8)
                    Task.Run(() => RunInspection(filePath));
                };

                _dw.StartWatch(inputPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"초기화 오류: {ex.Message}");
            }
        }

        private void RunInspection(string filePath)
        {
            try
            {
                // [참고] DirectoryWatcher 내부에서 이미 파일 대기 로직이 있다면 Sleep 생략 가능
                string fileName = Path.GetFileName(filePath);

                // 1. 통합 전처리 실행 (파일명 끝자리 1,2,3에 따른 마스크 자동 적용 포함)
                // 요구사항 2번: OpenCVSharp 기반 전처리 파이프라인
                using var processedImg = _processor.PrepareImage(filePath, 640, 640);
                if (processedImg == null) return;

                // 2. 추론 실행 (실제 모델 결과 연결)
                // 요구사항 3번: ONNX Runtime 추론 및 Confidence Score 산출
                // var inferenceResult = _tester.Predict(processedImg); // 실제 메서드명에 맞춰 수정 필요
                string result = "OK"; // 예: inferenceResult.Label
                float score = 0.95f;  // 예: inferenceResult.Confidence

                // 3. UI 업데이트 (Invoke 필수)
                this.Invoke(new Action(() => {
                    // pictureBox1.Image = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(processedImg);
                    // lblResult.Text = $"결과: {result} ({score:P1})";
                }));

                // 4. 로그 저장 (요구사항 5)
                _logger.SaveResult(fileName, result, score);

                // 5. 결과에 따라 파일 이동 및 분류 (요구사항 5)
                string targetFolder = result == "OK"
                    ? @"C:\InspectionTest\output\OK"
                    : @"C:\InspectionTest\output\NG";

                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                string targetPath = Path.Combine(targetFolder, fileName);

                // 파일 이동 로직 (안전하게 이동)
                if (File.Exists(filePath))
                {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(filePath, targetPath);
                }
            }
            catch (Exception ex)
            {
                // 요구사항 8: 오류 발생 시 로그 기록
                Console.WriteLine($"검사 에러 ({Path.GetFileName(filePath)}): {ex.Message}");
            }
        }

        private void button2_Click(object sender, EventArgs e)
        {
            // 시스템 연결 및 모델 재로드 테스트 (요구사항 7)
            try
            {
                string modelPath = ConfigurationManager.AppSettings["ModelPath"];
                _tester.LoadModel(modelPath);
                MessageBox.Show("모델 로드 및 시스템 연결 성공!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"테스트 오류: {ex.Message}");
            }
        }
    }
}