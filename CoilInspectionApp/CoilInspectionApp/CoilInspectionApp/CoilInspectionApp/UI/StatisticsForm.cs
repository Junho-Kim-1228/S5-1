using CoilInspectionApp.Statistics;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace CoilInspectionApp.UI
{
    public sealed class StatisticsForm : Form
    {
        private readonly InspectionStatisticsService _service;
        private readonly float? _anomaThreshold;
        private readonly ComboBox _scopeCombo = new ComboBox();
        private readonly Label _totalValue = CreateMetricValueLabel();
        private readonly Label _normalValue = CreateMetricValueLabel();
        private readonly Label _defectValue = CreateMetricValueLabel();
        private readonly Label _defectRateValue = CreateMetricValueLabel();
        private readonly Label _detectionValue = CreateMetricValueLabel();
        private readonly StatisticsBarChart _decisionChart = new StatisticsBarChart();
        private readonly StatisticsBarChart _classChart = new StatisticsBarChart();
        private readonly Label _anomaSummary = new Label();
        private readonly Label _yoloSummary = new Label();
        private readonly DataGridView _detailsGrid = new DataGridView();
        private readonly Label _statusLabel = new Label();
        private readonly Timer _refreshTimer = new Timer();
        private InspectionStatistics _currentStatistics = new InspectionStatistics();

        public StatisticsForm(string exportBaseDirectory, string currentBatchDirectory, float? anomaThreshold)
        {
            _service = new InspectionStatisticsService(exportBaseDirectory, currentBatchDirectory);
            _anomaThreshold = anomaThreshold;
            InitializeUi();
            ReloadScopes(false);

            _refreshTimer.Interval = 3000;
            _refreshTimer.Tick += (sender, args) =>
            {
                StatisticsScopeOption selected = _scopeCombo.SelectedItem as StatisticsScopeOption;
                if (string.Equals(selected?.Key, InspectionStatisticsService.CurrentScopeKey, StringComparison.OrdinalIgnoreCase))
                    RefreshStatistics();
            };
            _refreshTimer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            base.OnFormClosed(e);
        }

        private void InitializeUi()
        {
            Text = "검사 통계";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(1050, 720);
            Size = new Size(1220, 820);
            BackColor = Color.FromArgb(243, 246, 249);
            Font = new Font("맑은 고딕", 9F);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 66,
                BackColor = Color.FromArgb(25, 45, 67),
                Padding = new Padding(20, 12, 16, 10),
            };
            var title = new Label
            {
                Text = "검사 결과 통계",
                ForeColor = Color.White,
                Font = new Font("맑은 고딕", 15F, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 18),
            };
            _scopeCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _scopeCombo.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _scopeCombo.Location = new Point(690, 20);
            _scopeCombo.Size = new Size(290, 25);
            _scopeCombo.SelectedIndexChanged += (sender, args) => RefreshStatistics();

            var refreshButton = new Button
            {
                Text = "새로고침",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(990, 17),
                Size = new Size(90, 31),
                BackColor = Color.FromArgb(45, 126, 192),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            refreshButton.FlatAppearance.BorderSize = 0;
            refreshButton.Click += (sender, args) => ReloadScopes(true);

            var exportButton = new Button
            {
                Text = "CSV 저장",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(1088, 17),
                Size = new Size(96, 31),
                BackColor = Color.FromArgb(34, 139, 94),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false,
            };
            exportButton.FlatAppearance.BorderSize = 0;
            exportButton.Click += ExportCsv;
            header.Controls.Add(title);
            header.Controls.Add(_scopeCombo);
            header.Controls.Add(refreshButton);
            header.Controls.Add(exportButton);
            header.Resize += (sender, args) =>
            {
                exportButton.Left = header.ClientSize.Width - exportButton.Width - 16;
                refreshButton.Left = exportButton.Left - refreshButton.Width - 8;
                _scopeCombo.Left = refreshButton.Left - _scopeCombo.Width - 10;
            };

            var cards = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 112,
                ColumnCount = 5,
                Padding = new Padding(14, 14, 14, 10),
                BackColor = BackColor,
            };
            for (int i = 0; i < 5; i++)
                cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            cards.Controls.Add(CreateMetricCard("검사 완료", _totalValue, Color.FromArgb(31, 78, 121)), 0, 0);
            cards.Controls.Add(CreateMetricCard("정상", _normalValue, Color.FromArgb(20, 132, 92)), 1, 0);
            cards.Controls.Add(CreateMetricCard("불량", _defectValue, Color.FromArgb(205, 62, 62)), 2, 0);
            cards.Controls.Add(CreateMetricCard("불량률", _defectRateValue, Color.FromArgb(222, 132, 27)), 3, 0);
            cards.Controls.Add(CreateMetricCard("YOLO 박스", _detectionValue, Color.FromArgb(32, 110, 158)), 4, 0);

            var chartArea = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 225,
                ColumnCount = 3,
                Padding = new Padding(14, 0, 14, 10),
                BackColor = BackColor,
            };
            chartArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31F));
            chartArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34F));
            chartArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
            chartArea.Controls.Add(CreateChartGroup("정상 / 불량", _decisionChart), 0, 0);
            chartArea.Controls.Add(CreateChartGroup("결함 유형별 YOLO 박스", _classChart), 1, 0);
            chartArea.Controls.Add(CreateModelSummaryGroup(), 2, 0);

            ConfigureDetailsGrid();
            var detailsGroup = new GroupBox
            {
                Text = "상세 검사 결과",
                Dock = DockStyle.Fill,
                Padding = new Padding(9),
                BackColor = Color.White,
            };
            detailsGroup.Controls.Add(_detailsGrid);

            _statusLabel.Dock = DockStyle.Bottom;
            _statusLabel.Height = 28;
            _statusLabel.Padding = new Padding(16, 6, 0, 0);
            _statusLabel.ForeColor = Color.DimGray;
            _statusLabel.BackColor = Color.FromArgb(234, 238, 242);

            var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 0, 14, 10) };
            content.Controls.Add(detailsGroup);
            Controls.Add(content);
            Controls.Add(chartArea);
            Controls.Add(cards);
            Controls.Add(header);
            Controls.Add(_statusLabel);
        }

        private static Label CreateMetricValueLabel()
        {
            return new Label
            {
                Text = "0",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("맑은 고딕", 20F, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
            };
        }

        private static Panel CreateMetricCard(string title, Label valueLabel, Color accentColor)
        {
            var card = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                BackColor = Color.White,
                Padding = new Padding(15, 9, 10, 8),
            };
            var accent = new Panel { Dock = DockStyle.Left, Width = 4, BackColor = accentColor };
            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = Color.FromArgb(90, 100, 112),
            };
            card.Controls.Add(valueLabel);
            card.Controls.Add(titleLabel);
            card.Controls.Add(accent);
            return card;
        }

        private static GroupBox CreateChartGroup(string title, Control chart)
        {
            var group = new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                Padding = new Padding(10),
                BackColor = Color.White,
            };
            chart.Dock = DockStyle.Fill;
            group.Controls.Add(chart);
            return group;
        }

        private GroupBox CreateModelSummaryGroup()
        {
            var group = new GroupBox
            {
                Text = "모델별 결과",
                Dock = DockStyle.Fill,
                Margin = new Padding(5),
                Padding = new Padding(14),
                BackColor = Color.White,
            };
            _anomaSummary.Dock = DockStyle.Top;
            _anomaSummary.Height = 92;
            _anomaSummary.Font = new Font("맑은 고딕", 9.5F);
            _yoloSummary.Dock = DockStyle.Fill;
            _yoloSummary.Font = new Font("맑은 고딕", 9.5F);
            group.Controls.Add(_yoloSummary);
            group.Controls.Add(_anomaSummary);
            return group;
        }

        private void ConfigureDetailsGrid()
        {
            _detailsGrid.Dock = DockStyle.Fill;
            _detailsGrid.ReadOnly = true;
            _detailsGrid.AllowUserToAddRows = false;
            _detailsGrid.AllowUserToDeleteRows = false;
            _detailsGrid.AllowUserToResizeRows = false;
            _detailsGrid.AutoGenerateColumns = false;
            _detailsGrid.BackgroundColor = Color.White;
            _detailsGrid.BorderStyle = BorderStyle.None;
            _detailsGrid.RowHeadersVisible = false;
            _detailsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _detailsGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _detailsGrid.Columns.Add(CreateTextColumn("배치", "BatchName", 145));
            _detailsGrid.Columns.Add(CreateTextColumn("이미지", "ImageId", 190));
            _detailsGrid.Columns.Add(CreateTextColumn("최종", "FinalDecision", 65));
            _detailsGrid.Columns.Add(CreateTextColumn("Anoma", "AnomaDecision", 70));
            _detailsGrid.Columns.Add(CreateTextColumn("이상 점수", "AnomaScore", 80, "0.000"));
            _detailsGrid.Columns.Add(CreateTextColumn("YOLO", "YoloStatus", 70));
            _detailsGrid.Columns.Add(CreateTextColumn("박스", "DetectionCount", 55));
            _detailsGrid.Columns.Add(CreateTextColumn("결함 유형", "DefectClasses", 130));
        }

        private static DataGridViewTextBoxColumn CreateTextColumn(
            string header,
            string property,
            int minimumWidth,
            string format = null)
        {
            var column = new DataGridViewTextBoxColumn
            {
                HeaderText = header,
                DataPropertyName = property,
                MinimumWidth = minimumWidth,
            };
            if (!string.IsNullOrWhiteSpace(format))
                column.DefaultCellStyle.Format = format;
            return column;
        }

        private void ReloadScopes(bool keepSelection)
        {
            string selectedKey = keepSelection
                ? (_scopeCombo.SelectedItem as StatisticsScopeOption)?.Key
                : InspectionStatisticsService.CurrentScopeKey;
            List<StatisticsScopeOption> options = _service.GetScopeOptions();

            _scopeCombo.BeginUpdate();
            _scopeCombo.Items.Clear();
            _scopeCombo.Items.AddRange(options.Cast<object>().ToArray());
            _scopeCombo.EndUpdate();

            StatisticsScopeOption selected = options.FirstOrDefault(option =>
                string.Equals(option.Key, selectedKey, StringComparison.OrdinalIgnoreCase))
                ?? options.FirstOrDefault();
            if (selected != null)
                _scopeCombo.SelectedItem = selected;
            else
                RefreshStatistics();
        }

        private void RefreshStatistics()
        {
            StatisticsScopeOption scope = _scopeCombo.SelectedItem as StatisticsScopeOption;
            _currentStatistics = _service.Load(scope);

            _totalValue.Text = _currentStatistics.TotalCount.ToString("N0");
            _normalValue.Text = _currentStatistics.NormalCount.ToString("N0");
            _defectValue.Text = _currentStatistics.DefectCount.ToString("N0");
            _defectRateValue.Text = $"{_currentStatistics.DefectRate:0.0}%";
            _detectionValue.Text = _currentStatistics.DetectionCount.ToString("N0");

            _decisionChart.SetBars(new[]
            {
                new StatisticsBar { Label = "정상", Value = _currentStatistics.NormalCount, Color = Color.FromArgb(29, 154, 108) },
                new StatisticsBar { Label = "불량", Value = _currentStatistics.DefectCount, Color = Color.FromArgb(215, 72, 72) },
            });
            _classChart.SetBars(_currentStatistics.DefectClasses.Select(item => new StatisticsBar
            {
                Label = $"{item.ClassName} ({item.AverageConfidence:0.00})",
                Value = item.Count,
                Color = ResolveClassColor(item.ClassName),
            }));

            string thresholdText = _anomaThreshold.HasValue ? _anomaThreshold.Value.ToString("0.###") : "-";
            _anomaSummary.Text =
                "Anoma 이상 탐지\r\n" +
                $"실행 {_currentStatistics.AnomaExecutedCount:N0}건 · 이상 {_currentStatistics.AnomaAnomalyCount:N0}건\r\n" +
                $"점수 평균 {FormatScore(_currentStatistics.AnomaScoreAverage)}\r\n" +
                $"최소 {FormatScore(_currentStatistics.AnomaScoreMinimum)} · 최대 {FormatScore(_currentStatistics.AnomaScoreMaximum)} · 임계값 {thresholdText}";
            _yoloSummary.Text =
                "YOLO 결함 위치 검출\r\n" +
                $"실행 {_currentStatistics.YoloExecutedCount:N0}건\r\n" +
                $"검출 이미지 {_currentStatistics.YoloDetectionImageCount:N0}건\r\n" +
                $"전체 박스 {_currentStatistics.DetectionCount:N0}개";

            _detailsGrid.DataSource = null;
            _detailsGrid.DataSource = _currentStatistics.Rows;
            _statusLabel.Text = _currentStatistics.InvalidFileCount > 0
                ? $"추론 완료 결과 기준 · 읽지 못한 결과 파일 {_currentStatistics.InvalidFileCount}개 제외"
                : "추론 완료 결과 기준 · 현재 배치는 3초마다 자동 갱신됩니다.";
        }

        private static string FormatScore(float? value)
        {
            return value.HasValue ? value.Value.ToString("0.000") : "-";
        }

        private static Color ResolveClassColor(string className)
        {
            if (string.Equals(className, "찍힘", StringComparison.OrdinalIgnoreCase)
                || string.Equals(className, "dent", StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(210, 63, 63);
            if (string.Equals(className, "풀림", StringComparison.OrdinalIgnoreCase)
                || string.Equals(className, "loose", StringComparison.OrdinalIgnoreCase))
                return Color.FromArgb(45, 112, 190);
            return Color.FromArgb(66, 135, 157);
        }

        private void ExportCsv(object sender, EventArgs e)
        {
            if (_currentStatistics.Rows.Count == 0)
            {
                MessageBox.Show("저장할 검사 결과가 없습니다.", "검사 통계", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Filter = "CSV 파일|*.csv",
                FileName = $"inspection_statistics_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                    return;

                using (var writer = new StreamWriter(dialog.FileName, false, new UTF8Encoding(true)))
                {
                    writer.WriteLine("Batch,ImageId,Final,AnomaDecision,AnomaScore,YoloStatus,DetectionCount,DefectClasses");
                    foreach (InspectionStatisticsRow row in _currentStatistics.Rows)
                    {
                        writer.WriteLine(string.Join(",", new[]
                        {
                            Csv(row.BatchName),
                            Csv(row.ImageId),
                            Csv(row.FinalDecision),
                            Csv(row.AnomaDecision),
                            row.AnomaScore.HasValue ? row.AnomaScore.Value.ToString("0.000") : "",
                            Csv(row.YoloStatus),
                            row.DetectionCount.ToString(),
                            Csv(row.DefectClasses),
                        }));
                    }
                }

                MessageBox.Show("통계 CSV를 저장했습니다.", "검사 통계", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static string Csv(string value)
        {
            string safe = value ?? "";
            return $"\"{safe.Replace("\"", "\"\"")}\"";
        }
    }
}
