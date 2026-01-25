using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using IOPath = System.IO.Path;


namespace CoilTrainingUI
{
    public partial class MainWindow : Window
    {
        private bool _isLoadingImage;

        private YoloLabelService _yoloService;
        private BoundingBoxManager _bboxManager;
        private readonly DatasetExportService _exportService = new();
        private CanvasInteractionManager _canvasInteractionManager;
        private ImageStateManager _imageStateManager;
        private AnomalyStateService _anomalyService;
        private RoiStateService _roiService;
        private BitmapSource _originalBitmap;
        private RoiPreprocessService _roiPreprocessService;
        private readonly ImageStateService _stateService = new();

        private DispatcherTimer _labelSaveDebounceTimer;
        private string? _pendingSaveImagePath;
        private const int LabelSaveDebounceMs = 300;

        // 항상 원본은 유지
        private BitmapSource _rawBitmap;

        // ROI 적용된 "실제 사용 이미지"
        private BitmapSource _processedBitmap;

        private string _currentImagePath;


        private readonly Dictionary<string, int> _classToId = new()
        {
            { "dent", 0 },
            { "loose", 1 }
        };

        private ObservableCollection<ImageItem> _images
            = new ObservableCollection<ImageItem>();

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj)
    where T : DependencyObject
        {
            if (depObj == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                    yield return t;

                foreach (T childOfChild in FindVisualChildren<T>(child))
                    yield return childOfChild;
            }
        }


        private void ImageCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Rectangle)
                return;

            // 🔥 이전 선택 완전 해제
            ClassComboBox.SelectedIndex = -1;
            ClassComboBox.IsEnabled = false;

            _bboxManager.ClearSelection();

            _canvasInteractionManager.StartDraw(
                e.GetPosition(ImageCanvas)
            );
        }


        private void ImageCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            _canvasInteractionManager.UpdateDraw(e.GetPosition(ImageCanvas));
        }

        private void ImageCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var bbox = _canvasInteractionManager.EndDraw(
                ImageCanvas.Width,
                ImageCanvas.Height
            );

            if (bbox == null)
                return;

            // 1️⃣ 상태 저장
            _imageStateManager.AddLabel(_currentImagePath, bbox);

            // 2️⃣ 🔥 방금 만든 박스를 자동 선택 상태로 만들기
            _bboxManager.SelectLastCreated();

            // 3️⃣ 클래스 UI 활성화 + 기본값 반영
            ClassComboBox.IsEnabled = true;
            ClassComboBox.SelectedItem = ClassComboBox.Items
                .OfType<ComboBoxItem>()
                .First(i => i.Content.ToString() == bbox.ClassName);
            
            RequestSaveLabelsDebounced(_currentImagePath);


            SaveLabelsToStateJson(_currentImagePath);
        }

        private void ImageCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Rectangle rect)
            {
                var bbox = _bboxManager.Select(
                    rect,
                    e.GetPosition(ImageCanvas)
                );

                if (bbox != null)
                {
                    ClassComboBox.IsEnabled = true;

                    // 🔥 핵심: 선택된 박스의 클래스 → ComboBox 반영
                    ClassComboBox.SelectedItem = ClassComboBox.Items
                        .OfType<ComboBoxItem>()
                        .FirstOrDefault(i =>
                            i.Content?.ToString() == bbox.ClassName
                        );
                }

                e.Handled = true;
            }
        }

        private void ImageCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            _bboxManager.Drag(
                e.GetPosition(ImageCanvas)
            );
        }

        private void ImageCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _bboxManager.EndDrag(
                ImageCanvas.Width,
                ImageCanvas.Height
            );

            // ✅ 드래그가 끝난 좌표를 state.json에 저장
            if (!string.IsNullOrEmpty(_currentImagePath))
            {
                _bboxManager.ForceUpdateAll(ImageCanvas.Width, ImageCanvas.Height);
                SaveLabelsToStateJson(_currentImagePath);
            }

            // 드래그 끝난 결과 저장(디바운스)
            if (!string.IsNullOrEmpty(_currentImagePath))
                RequestSaveLabelsDebounced(_currentImagePath);
        }

        private void ImageCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            _canvasInteractionManager.OnMouseWheel(e);
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _canvasInteractionManager.ZoomIn();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _canvasInteractionManager.ZoomOut();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;

            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            var removedBBox = _bboxManager.DeleteSelected();
            if (removedBBox == null)
                return;

            // 1️⃣ 메모리 상태에서 제거
            _imageStateManager.RemoveLabel(_currentImagePath, removedBBox);

            // 2️⃣ UI 모델 상태 갱신
            var item = _images.FirstOrDefault(i => i.FullPath == _currentImagePath);
            if (item != null)
            {
                item.HasLabel = _imageStateManager.HasLabel(_currentImagePath);
                ImageListBox.Items.Refresh();
            }

            _imageStateManager.RemoveLabel(_currentImagePath, removedBBox);

            // ✅ 삭제 반영 저장
            SaveLabelsToStateJson(_currentImagePath);

            RequestSaveLabelsDebounced(_currentImagePath);
        }


        private void ClassComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ClassComboBox.SelectedItem is not ComboBoxItem item)
                return;

            if (string.IsNullOrEmpty(_currentImagePath))
                return;

            string className = item.Content.ToString();

            _bboxManager.SetSelectedClass(className);

            // ✅ 상태는 ImageStateManager 기준으로 갱신
            var imageItem = _images.FirstOrDefault(i => i.FullPath == _currentImagePath);
            if (imageItem != null)
            {
                imageItem.HasLabel = _imageStateManager.HasLabel(_currentImagePath);
                ImageListBox.Items.Refresh();
            }

            _bboxManager.SetSelectedClass(className);
            RequestSaveLabelsDebounced(_currentImagePath);

            // ✅ 클래스 변경 반영 저장
            SaveLabelsToStateJson(_currentImagePath);

        }

        private void SaveLabelsToStateJson(string imagePath)
        {
            // 혹시 캔버스 변경이 남아있다면 확정(드래그 종료 등에서 호출하므로 안전)
            _bboxManager.ForceUpdateAll(ImageCanvas.Width, ImageCanvas.Height);

            var state = _stateService.Load(imagePath);

            state.Labels.Clear();

            var boxes = _imageStateManager.GetLabels(imagePath);
            foreach (var b in boxes)
            {
                state.Labels.Add(new LabelDto
                {
                    ClassName = b.ClassName,
                    X = b.X,
                    Y = b.Y,
                    Width = b.Width,
                    Height = b.Height
                });
            }

            _stateService.Save(imagePath, state); // 여기서 IsNormal null이면 true로 정리되게 해놔야 함
        }




        private void LoadImage(string imagePath)
        {
            _isLoadingImage = true;
            try
            {
                _currentImagePath = imagePath;
                ClassComboBox.IsEnabled = false;

                // 1️⃣ ImageStateManager 보장
                _imageStateManager.EnsureImage(imagePath);

                // 2️⃣ 이미지 로드
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                _rawBitmap = bitmap;              // 🔥 반드시 저장
                MainImage.Source = bitmap;

                ImageCanvas.Width = bitmap.PixelWidth;
                ImageCanvas.Height = bitmap.PixelHeight;

                // 3️⃣ Canvas 초기화
                _bboxManager.ClearAll();

                // 4️⃣ 라벨 로드: state.json 우선
                _bboxManager.ClearAll();
                _imageStateManager.ClearLabels(imagePath);

                var state = _stateService.Load(imagePath);

                if (state.Labels.Count > 0)
                {
                    var mutable = _imageStateManager.GetMutableLabels(imagePath);

                    foreach (var l in state.Labels)
                    {
                        mutable.Add(new BoundingBox
                        {
                            X = l.X,
                            Y = l.Y,
                            Width = l.Width,
                            Height = l.Height,
                            ClassName = l.ClassName
                        });
                    }
                }
                else
                {
                    // 레거시 txt fallback (읽기만)
                    _yoloService.Load(imagePath, _imageStateManager.GetMutableLabels(imagePath));
                }

                // 캔버스에 표시
                foreach (var bbox in _imageStateManager.GetLabels(imagePath))
                {
                    _bboxManager.AddFromModel(bbox, ImageCanvas.Width, ImageCanvas.Height);
                }

                // 5️⃣ Anomaly 상태
                bool isNormal = _anomalyService.Load(imagePath);
                _imageStateManager.SetNormal(imagePath, isNormal);

                // 6️⃣ ROI 타입
                var roiType = _roiService.Load(imagePath);
                _imageStateManager.SetRoiType(imagePath, roiType);

                _roiPreprocessService.EnsureProcessed(imagePath, roiType);

                // 7️⃣ UI 반영
                if (ImageListBox.SelectedItem is ImageItem item)
                {
                    item.IsNormal = isNormal;
                    item.HasLabel = _imageStateManager.HasLabel(imagePath);
                    item.RoiType = roiType;

                    NormalRadio.IsChecked = isNormal;
                    AbnormalRadio.IsChecked = !isNormal;
                }

                RestoreRoiTypeUI(imagePath);
                ImageListBox.Items.Refresh();

                // 🔥 8️⃣ ROI 체크 상태에 따라 화면 갱신 (이게 핵심)
                UpdateRoiDisplay();
            }
            finally
            {
                _isLoadingImage = false;
            }
        }

        private void UpdateRoiDisplay()
        {
            if (_rawBitmap == null || string.IsNullOrEmpty(_currentImagePath))
                return;

            if (ShowRoiCheckBox.IsChecked == true)
            {
                var roiType = _imageStateManager.GetRoiType(_currentImagePath);

                // ✅ 서비스가 알아서 (생성/로드)해서 BitmapSource 반환
                MainImage.Source = _roiPreprocessService.GetOrCreateProcessedImage(_currentImagePath, roiType);
            }
            else
            {
                MainImage.Source = _rawBitmap;
            }
        }



        private void LoadImageFolder(string folderPath)
        {
            _images.Clear();

            var imageFiles = Directory.GetFiles(folderPath, "*.bmp");

            foreach (var img in imageFiles)
            {
                _imageStateManager.EnsureImage(img);

                // ✅ 1) 저장된 ROI 먼저 로드 (없으면 None으로 간주)
                RoiType roiType = RoiType.None;

                if (_roiService.HasState(img))
                    roiType = _roiService.Load(img);

                // ✅ 2) 저장값이 None일 때만 파일명 규칙으로 자동 지정
                if (roiType == RoiType.None)
                {
                    var inferred = InferRoiTypeFromFileName(IOPath.GetFileName(img));

                    // inferred가 None이어도 저장해두면 "처리됨" 상태가 유지됨(원하면 조건 걸어도 됨)
                    roiType = inferred;
                    _roiService.Save(img, roiType);
                }

                // ✅ 3) 메모리 반영
                _imageStateManager.SetRoiType(img, roiType);

                // ✅ 4) 전처리 생성(Show 상관 없음)
                _roiPreprocessService.EnsureProcessed(img, roiType);

                // 1️⃣ YOLO 라벨 실제 로드
                var s = _stateService.Load(img);
                bool hasLabel = s.Labels.Count > 0;

                if (!hasLabel)
                {
                    var labels = new List<BoundingBox>();
                    _yoloService.Load(img, labels);
                    hasLabel = labels.Count > 0;

                    // (선택) txt가 있으면 state.json으로 마이그레이션
                    if (hasLabel)
                    {
                        s.Labels.Clear();
                        foreach (var b in labels)
                        {
                            s.Labels.Add(new LabelDto { ClassName = b.ClassName, X = b.X, Y = b.Y, Width = b.Width, Height = b.Height });
                        }
                        _stateService.Save(img, s);
                    }
                }


                // 2️⃣ Anomaly 상태 로드
                bool isNormal = _anomalyService.Load(img);

                _images.Add(new ImageItem
                {
                    FileName = IOPath.GetFileName(img),
                    FullPath = img,
                    HasLabel = hasLabel,
                    IsNormal = isNormal,
                    RoiType = roiType
                });
            }

            ImageListBox.ItemsSource = _images;
        }




        private void ImageListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ImageListBox.SelectedItem is ImageItem item)
            {
                LoadImage(item.FullPath);

                NormalRadio.IsChecked = item.IsNormal;
                AbnormalRadio.IsChecked = !item.IsNormal;

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            }
        }
        private void NormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            // 1️⃣ 메모리 상태 변경
            _imageStateManager.SetNormal(item.FullPath, true);

            // 2️⃣ 파일 저장
            _anomalyService.Save(item.FullPath, true);

            // 3️⃣ UI 모델 반영
            item.IsNormal = true;

            ImageListBox.Items.Refresh();
        }

        private void AbnormalRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            _imageStateManager.SetNormal(item.FullPath, false);
            _anomalyService.Save(item.FullPath, false);

            item.IsNormal = false;

            ImageListBox.Items.Refresh();
        }


        private void ExportAnomalyDataset_Click(object sender, RoutedEventArgs e)
        {
            // 1. 프로젝트 루트는 UI가 판단
            string root = FindProjectRoot("capstone_design");

            // 2. 실제 export는 서비스에게 맡김
            string outputPath = _exportService.ExportAnomalyDataset(_images, root);

            // 3. 결과를 사용자에게 보여줌
            MessageBox.Show($"완료: {outputPath}");
        }

        private string FindProjectRoot(string targetFolderName)
        {
            DirectoryInfo? dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);

            while (dir != null)
            {
                if (dir.Name.Equals(targetFolderName, StringComparison.OrdinalIgnoreCase))
                    return dir.FullName;

                dir = dir.Parent;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private void RoiTypeRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (_isLoadingImage) return;

            if (ImageListBox.SelectedItem is not ImageItem item)
                return;
            if (sender is not RadioButton rb)
                return;
            if (!Enum.TryParse<RoiType>(rb.Tag.ToString(), out var roiType))
                return;

            // 1) 메모리(UI 모델 + StateManager 둘 다)
            item.RoiType = roiType;
            _imageStateManager.SetRoiType(item.FullPath, roiType);

            // 2) 파일 저장
            _roiService.Save(item.FullPath, roiType);

            // ✅ Show 여부와 무관하게 "전처리 파일"을 생성/갱신
            _roiPreprocessService.EnsureProcessed(item.FullPath, roiType);

            // 4) 즉시 화면 갱신
            UpdateRoiDisplay();

            ImageListBox.Items.Refresh();
        }


        private void RestoreRoiTypeUI(string imagePath)
        {
            if (ImageListBox.SelectedItem is not ImageItem item)
                return;

            foreach (var child in LogicalTreeHelper.GetChildren(this))
            {
                // 아무것도 안 함 (placeholder)
            }

            // 간단하게 직접 찾는 방식
            foreach (var rb in FindVisualChildren<RadioButton>(this))
            {
                if (rb.Tag?.ToString() == item.RoiType.ToString())
                {
                    rb.IsChecked = true;
                    return;
                }
            }
        }

        private void ShowRoiCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            UpdateRoiDisplay();
        }

        private void ShowRoiCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateRoiDisplay();
        }

        private RoiType InferRoiTypeFromFileName(string fileName)
        {
            // fileName: "250825_151708_A35W_4-1 [1024].bmp" 처럼 들어온다고 가정
            // 1) 확장자 제거
            string name = IOPath.GetFileNameWithoutExtension(fileName);

            // 2) 마지막 '-' 위치 찾기
            int idx = name.LastIndexOf('-');
            if (idx < 0 || idx == name.Length - 1)
                return RoiType.None;

            // 3) 마지막 '-' 뒤 숫자만 파싱 (뒤에 공백/대괄호가 있어도 안전하게)
            // 예: "4-1 [1024]" -> idx 뒤 문자열은 "1 [1024]"
            string tail = name.Substring(idx + 1).Trim();

            // tail의 선두 숫자만 읽기
            int n = 0;
            int i = 0;
            while (i < tail.Length && char.IsDigit(tail[i]))
            {
                n = n * 10 + (tail[i] - '0');
                i++;
            }

            if (i == 0) return RoiType.None; // 숫자 없음

            return n switch
            {
                1 => RoiType.A,
                2 => RoiType.B,
                3 => RoiType.C,
                _ => RoiType.None
            };
        }

        private RoiType ResolveRoiTypeOnlyWhenNone(string imagePath)
        {
            // 1) 저장된 값이 있으면 로드
            // (HasState는 RoiStateService에 추가되어 있어야 합니다)
            if (_roiService.HasState(imagePath))
            {
                var saved = _roiService.Load(imagePath);

                // ✅ 이미 지정(A/B/C)되어 있으면 그대로 둠
                if (saved != RoiType.None)
                    return saved;

                // ✅ 저장은 되어있는데 None이면 -> 자동 지정 시도
                var inferred = InferRoiTypeFromFileName(IOPath.GetFileName(imagePath));
                if (inferred != RoiType.None)
                {
                    _roiService.Save(imagePath, inferred);
                    return inferred;
                }

                return RoiType.None;
            }

            // 2) 저장 자체가 없으면 -> 자동 지정 시도 후 저장(원하면 None도 저장 가능)
            var inferred2 = InferRoiTypeFromFileName(IOPath.GetFileName(imagePath));
            _roiService.Save(imagePath, inferred2);  // None도 저장해두면 다음 실행 때 "처리됨" 상태가 됨
            return inferred2;
        }
        private void RequestSaveLabelsDebounced(string imagePath) //디바운스 트리거
        {
            if (_isLoadingImage) return;
            if (string.IsNullOrEmpty(imagePath)) return;

            _pendingSaveImagePath = imagePath;

            _labelSaveDebounceTimer.Stop();
            _labelSaveDebounceTimer.Start();
        }

        private async void TrainAll_Click(object sender, RoutedEventArgs e)
        {
            // 학습은 오래 걸리니 UI 멈춤 방지
            try
            {
                if (_images.Count == 0)
                {
                    MessageBox.Show("이미지가 없습니다.");
                    return;
                }

                // 1) runRoot
                string inputDir = IOPath.GetDirectoryName(_images[0].FullPath)!;
                string runRoot = IOPath.Combine(inputDir, "_train_runs");
                Directory.CreateDirectory(runRoot);

                // 2) 현재 이미지 경로
                var imagePaths = _images.Select(x => x.FullPath)
                                        .Where(File.Exists)
                                        .ToList();

                // 3) YOLO workspace 생성 (라벨 txt는 여기서 workspace에만 생성됨)
                var yoloWsSvc = new YoloWorkspaceService(_stateService);
                var yoloWs = yoloWsSvc.BuildYoloWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: 0.8,
                    valRatio: 0.2,
                    seed: 42,
                    useRoiProcessedImages: true
                );

                // 4) Anoma workspace 생성 (정상만)
                var anomaWsSvc = new AnomaWorkspaceService(_stateService);
                var anomaWs = anomaWsSvc.BuildWorkspace(
                    imagePaths,
                    runRootDir: runRoot,
                    trainRatio: 0.8,
                    valRatio: 0.2,
                    seed: 42,
                    useRoiProcessedImages: true
                );

                // 5) 이번 실행 결과 폴더(로그/산출물)
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string runDir = IOPath.Combine(runRoot, $"run_{stamp}_all");
                string logsDir = IOPath.Combine(runDir, "logs");
                Directory.CreateDirectory(logsDir);

                string yoloOut = IOPath.Combine(runDir, "yolo_out");
                string anomaOut = IOPath.Combine(runDir, "anoma_out");
                Directory.CreateDirectory(yoloOut);
                Directory.CreateDirectory(anomaOut);

                // 6) 파이썬 스크립트 실행(순차)
                // ✅ 여긴 나중에 설정파일로 빼세요(지금은 하드코딩으로 먼저 동작시키는 게 우선)
                string pythonExe = @"C:\Users\wnsgh\anaconda3\envs\mask_vision\python.exe";
                string projectRoot = FindProjectRoot("capstone_design"); // 당신이 이미 구현한 함수 사용 가능

                string yoloScript = IOPath.Combine(projectRoot, "scripts", "train_yolo.py");
                string anomaScript = IOPath.Combine(projectRoot, "scripts", "train_anoma.py");

                if (!File.Exists(yoloScript) || !File.Exists(anomaScript))
                {
                    MessageBox.Show("scripts/train_yolo.py 또는 scripts/train_anoma.py가 없습니다.");
                    return;
                }

                var runner = new PythonRunner();
                using var cts = new CancellationTokenSource(); // 추후 Cancel 메뉴 추가 가능

                // (1) YOLO
                int yoloCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: yoloScript,
                    args: $"--workspace \"{yoloWs.WorkspaceRoot}\" --out \"{yoloOut}\"",
                    workingDir: projectRoot,
                    logPath: IOPath.Combine(logsDir, "yolo.log"),
                    ct: cts.Token
                );

                if (yoloCode != 0)
                {
                    MessageBox.Show($"YOLO 학습 실패 (ExitCode={yoloCode})\nlogs/yolo.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                // (2) Anomalib
                int anomaCode = await runner.RunAsync(
                    pythonExe: pythonExe,
                    scriptPath: anomaScript,
                    args: $"--workspace \"{anomaWs.WorkspaceRoot}\" --out \"{anomaOut}\"",
                    workingDir: projectRoot,
                    logPath: IOPath.Combine(logsDir, "anoma.log"),
                    ct: cts.Token
                );

                if (anomaCode != 0)
                {
                    MessageBox.Show($"Anomalib 학습 실패 (ExitCode={anomaCode})\nlogs/anoma.log 확인");
                    OpenFolder(logsDir);
                    return;
                }

                // 7) inference package 생성
                string pkgDir = IOPath.Combine(runDir, "inference_package");
                string modelsDir = IOPath.Combine(pkgDir, "models");
                string cfgDir = IOPath.Combine(pkgDir, "config");
                Directory.CreateDirectory(modelsDir);
                Directory.CreateDirectory(cfgDir);
                Directory.CreateDirectory(IOPath.Combine(pkgDir, "run"));

                // ✅ 스크립트가 아래 파일명을 생성한다는 계약이 필요
                string yoloOnnx = IOPath.Combine(yoloOut, "yolo.onnx");
                string anomaOnnx = IOPath.Combine(anomaOut, "anoma.onnx");

                if (!File.Exists(yoloOnnx) || !File.Exists(anomaOnnx))
                {
                    MessageBox.Show("ONNX 산출물이 없습니다. 스크립트가 yolo.onnx / anoma.onnx를 out 폴더에 생성해야 합니다.");
                    OpenFolder(runDir);
                    return;
                }

                File.Copy(yoloOnnx, IOPath.Combine(modelsDir, "yolo.onnx"), true);
                File.Copy(anomaOnnx, IOPath.Combine(modelsDir, "anoma.onnx"), true);

                File.WriteAllText(IOPath.Combine(cfgDir, "pipeline.json"),
        @"{
  ""preprocess"": { ""use_roi_processed"": true },
  ""yolo"": { ""classes"": { ""dent"": 0, ""loose"": 1 } },
  ""fusion"": { ""rule"": ""AND"", ""yolo_threshold"": 0.25, ""anoma_threshold"": 0.5 }
}", System.Text.Encoding.UTF8);

                MessageBox.Show($"Train All 완료\n\n{pkgDir}");
                OpenFolder(pkgDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Train All 중 예외 발생:\n" + ex.Message);
            }
        }

        private void OpenFolder(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        public MainWindow()
        {
            InitializeComponent();
            _yoloService = new YoloLabelService(_classToId);
            _bboxManager = new BoundingBoxManager(ImageCanvas);
            _canvasInteractionManager = new CanvasInteractionManager(
                ImageScrollViewer,
                ImageScale,
                _bboxManager
            );
            _imageStateManager = new ImageStateManager();
            _anomalyService = new AnomalyStateService();
            _roiService = new RoiStateService();
            _roiPreprocessService = new RoiPreprocessService();

            //타이머 초기화
            _labelSaveDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(LabelSaveDebounceMs)
            };
            _labelSaveDebounceTimer.Tick += (s, e) =>
            {
                _labelSaveDebounceTimer.Stop();

                if (_isLoadingImage) return;
                if (string.IsNullOrEmpty(_pendingSaveImagePath)) return;

                SaveLabelsToStateJson(_pendingSaveImagePath);
            };


            string folderPath = @"C:\Users\wnsgh\Desktop\input";
            LoadImageFolder(folderPath);

            Loaded += (s, e) =>
            {
                if (_images.Count > 0)
                {
                    ImageListBox.SelectedIndex = 0;
                }

                _canvasInteractionManager.FitToView(
                    ImageCanvas.Width,
                    ImageCanvas.Height
                );
            };
        }

    }
}