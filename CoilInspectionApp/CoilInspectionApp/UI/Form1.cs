using CoilInspectionApp.Logging;
using CoilInspectionApp.Preprocess;
using CoilInspectionApp.Watcher;
using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CoilInspectionApp
{
    public partial class Form1 : Form
    {
        // 변수 선언
        private CoilInspectionApp.Watcher.DirectoryWatcher _dw;
        private CsvLogger _logger = new CsvLogger();

        public Form1()
        {
            InitializeComponent();
            InitSystem(); // 시스템 초기화 실행
        }

        private void InitSystem()
        {
            try
            {
                string inputPath = ConfigurationManager.AppSettings["InputDir"];

                // 폴더가 없으면 생성
                if (!Directory.Exists(inputPath)) Directory.CreateDirectory(inputPath);

                _dw = new DirectoryWatcher();
                _dw.OnFileCreated += (filePath) => {
                    this.Invoke(new Action(() => {
                        RunInspection(filePath); // 실시간 검사 실행
                    }));
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
                // 파일이 생성된 직후에는 다른 프로세스(윈도우)가 잡고 있을 수 있어 잠시 대기
                System.Threading.Thread.Sleep(500);

                string fileName = Path.GetFileName(filePath);

                // 1. 전처리 (OpenCV)
                var processor = new ImageProcessor();
                var img = processor.PrepareImage(filePath, 640, 640);
                if (img == null) return;

                // 2. 추론 (현재는 테스트를 위해 무조건 OK로 처리)
                string result = "OK";
                float score = 0.95f;

                // 3. 로그 저장
                _logger.SaveResult(fileName, result, score);

                // 4. 결과에 따라 파일 이동
                string targetFolder = result == "OK" ? @"C:\InspectionTest\output\OK" : @"C:\InspectionTest\output\NG";
                if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

                string targetPath = Path.Combine(targetFolder, fileName);

                if (File.Exists(filePath))
                {
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                    File.Move(filePath, targetPath);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"검사 에러: {ex.Message}");
            }
        }

        // 디자인 창의 버튼2와 연결된 함수
        private void button2_Click(object sender, EventArgs e)
        {
            try
            {
                string modelPath = ConfigurationManager.AppSettings["ModelPath"];
                var tester = new CoilInspectionApp.Inference.OnnxModelTester();
                tester.LoadModel(modelPath);

                MessageBox.Show("시스템 연결 테스트 성공!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"테스트 오류: {ex.Message}");
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // 필요한 경우 초기화 코드 작성
        }
    }
}