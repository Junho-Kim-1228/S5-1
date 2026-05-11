using CoilInspectionApp.Logging;
using CoilInspectionApp.Preprocess;
using CoilInspectionApp.Watcher;
using CoilInspectionApp.Interface;
using Microsoft.ML.OnnxRuntime;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Newtonsoft.Json;

namespace CoilInspectionApp
{
    public partial class Form1 : Form
    {
        private CoilInspectionApp.Watcher.DirectoryWatcher _dw;
        private CsvLogger _logger = new CsvLogger();
        private OnnxModelTester _modelTester = new OnnxModelTester();
        private BatchExporter _batchExporter;
        private PipelineConfig _config;

        public Form1()
        {
            InitializeComponent();
            InitSystem();
        }

        private void InitSystem()
        {
            try
            {
                // App.config 경로 로드 (하드코딩 제거)
                string inputPath = ConfigurationManager.AppSettings["InputDir"] ?? @"C:\InspectionTest\input";
                string packagePath = ConfigurationManager.AppSettings["InferencePackagePath"] ?? @".\InferencePackage";
                string exportBasePath = ConfigurationManager.AppSettings["ExportBasePath"] ?? @"C:\InspectionTest\TrainingBatches";

                // 1. Inference Package 로더 (pipeline.json 파싱)
                string configPath = Path.Combine(packagePath, "config", "pipeline.json");
                if (File.Exists(configPath))
                {
                    _config = JsonConvert.DeserializeObject<PipelineConfig>(File.ReadAllText(configPath));
                }
                else
                {
                    _config = new PipelineConfig { Threshold = 0.5f, InputSize = new int[] { 640, 640 } };
                }

                // 2. 모델 로드 (YOLO 모델 우선 로드)
                if (_config.ModelFiles != null && _config.ModelFiles.ContainsKey("yolo"))
                {
                    string mPath = Path.Combine(packagePath, _config.ModelFiles["yolo"]);
                    if (File.Exists(mPath)) _modelTester.LoadModel(mPath);
                }

                if (!Directory.Exists(inputPath)) Directory.CreateDirectory(inputPath);

                // 3. 배치 매니저 초기화
                _batchExporter = new BatchExporter(exportBasePath);
                _batchExporter.StartNewBatch();

                _dw = new DirectoryWatcher();
                _dw.OnFileCreated += (filePath) => {
                    this.Invoke(new Action(() => RunInspection(filePath)));
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
                // 파일 접근 권한을 위해 대기 (Watcher 안정화)
                if (!WaitForFile(filePath)) return;

                string fileName = Path.GetFileName(filePath);
                string imageId = Path.GetFileNameWithoutExtension(filePath);

                var processor = new ImageProcessor();
                using (Mat rawImg = Cv2.ImRead(filePath))
                // pipeline.json의 설정값 사용
                using (Mat preprocessedImg = processor.PrepareImage(filePath, _config.InputSize[0], _config.InputSize[1]))
                {
                    if (preprocessedImg == null || preprocessedImg.Empty()) return;

                    // 화면 표시 업데이트
                    if (pictureBox1.Image != null) pictureBox1.Image.Dispose();
                    pictureBox1.Image = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(preprocessedImg);

                    // AI 추론
                    float[] inferenceResults = _modelTester.RunInference(preprocessedImg);
                    float score = (inferenceResults != null && inferenceResults.Length > 0) ? inferenceResults[0] : 0f;

                    bool isDefect = score > _config.Threshold;
                    string result = isDefect ? "NG" : "OK";
                    List<string> reasons = isDefect ? new List<string> { "anoma" } : new List<string> { "normal" };

                    // 로그 및 구조화된 배치 결과 저장
                    _logger.SaveResult(fileName, result, score);
                    _batchExporter.AddResult(imageId, rawImg, preprocessedImg, score, isDefect, reasons);
                }
            }
            catch (Exception ex)
            {
                LogException(ex);
            }
        }

        private bool WaitForFile(string path)
        {
            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                        return true;
                }
                catch { System.Threading.Thread.Sleep(300); }
            }
            return false;
        }

        private void LogException(Exception ex)
        {
            string logPath = Path.Combine(Application.StartupPath, "error_log.txt");
            File.AppendAllText(logPath, $"{DateTime.Now}: {ex.Message}\n");
        }

        private void button1_Click(object sender, EventArgs e) => SelectAndRunImage();
        private void button2_Click(object sender, EventArgs e) // 배치 마감
        {
            try
            {
                _batchExporter.CloseBatch();
                MessageBox.Show("배치 마감 완료 (DONE.flag 생성됨)");
                _batchExporter.StartNewBatch();
            }
            catch (Exception ex) { MessageBox.Show("마감 오류: " + ex.Message); }
        }

        private void SelectAndRunImage()
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = "이미지 파일|*.jpg;*.png;*.bmp" })
            {
                if (ofd.ShowDialog() == DialogResult.OK) RunInspection(ofd.FileName);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e) { _batchExporter?.CloseBatch(); base.OnFormClosing(e); }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }

    // 설정 클래스 규격
    public class PipelineConfig
    {
        public float Threshold { get; set; }
        public int[] InputSize { get; set; }
        public Dictionary<string, string> ModelFiles { get; set; }
    }
}