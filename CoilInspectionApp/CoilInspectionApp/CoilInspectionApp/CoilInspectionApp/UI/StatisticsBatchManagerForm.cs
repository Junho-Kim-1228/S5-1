using CoilInspectionApp.Statistics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CoilInspectionApp.UI
{
    public sealed class StatisticsBatchManagerForm : Form
    {
        private readonly InspectionStatisticsService _service;
        private readonly DataGridView _completedGrid = CreateGrid();
        private readonly DataGridView _trashGrid = CreateGrid();
        private readonly TabControl _tabs = new TabControl();
        private readonly Button _excludeButton = new Button();
        private readonly Button _restoreButton = new Button();

        public StatisticsBatchManagerForm(InspectionStatisticsService service)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            InitializeUi();
            Reload();
        }

        private void InitializeUi()
        {
            Text = "통계 완료 배치 관리";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(760, 500);
            Size = new Size(900, 600);
            BackColor = Color.FromArgb(243, 246, 249);
            Font = new Font("맑은 고딕", 9F);

            ConfigureColumns(_completedGrid);
            ConfigureColumns(_trashGrid);
            _completedGrid.SelectionChanged += (sender, args) => UpdateButtons();
            _trashGrid.SelectionChanged += (sender, args) => UpdateButtons();

            var completedPage = new TabPage("완료 배치") { Padding = new Padding(8) };
            var trashPage = new TabPage("통계 제외 / 휴지통") { Padding = new Padding(8) };
            completedPage.Controls.Add(_completedGrid);
            trashPage.Controls.Add(_trashGrid);
            _tabs.Dock = DockStyle.Fill;
            _tabs.TabPages.Add(completedPage);
            _tabs.TabPages.Add(trashPage);
            _tabs.SelectedIndexChanged += (sender, args) => UpdateButtons();

            var description = new Label
            {
                Dock = DockStyle.Top,
                Height = 55,
                Padding = new Padding(14, 10, 14, 6),
                Text = "보관 완료 배치를 통계에서 제외하면 archive\\_trash로 이동합니다. 파일은 지워지지 않으며 다시 복원할 수 있습니다.",
                ForeColor = Color.DimGray,
            };

            var buttons = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 54,
                Padding = new Padding(10, 8, 10, 8),
                FlowDirection = FlowDirection.LeftToRight,
            };
            var openButton = CreateButton("폴더 열기", 100);
            openButton.Click += OpenSelected;
            _excludeButton.Text = "통계에서 제외";
            _excludeButton.Size = new Size(120, 32);
            _excludeButton.Click += ExcludeSelected;
            _restoreButton.Text = "선택 복원";
            _restoreButton.Size = new Size(100, 32);
            _restoreButton.Click += RestoreSelected;
            var refreshButton = CreateButton("새로고침", 90);
            refreshButton.Click += (sender, args) => Reload();
            var closeButton = CreateButton("닫기", 80);
            closeButton.Click += (sender, args) => Close();
            buttons.Controls.Add(openButton);
            buttons.Controls.Add(_excludeButton);
            buttons.Controls.Add(_restoreButton);
            buttons.Controls.Add(refreshButton);
            buttons.Controls.Add(closeButton);

            Controls.Add(_tabs);
            Controls.Add(description);
            Controls.Add(buttons);
        }

        private static DataGridView CreateGrid()
        {
            return new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                AutoGenerateColumns = false,
                BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None,
                RowHeadersVisible = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            };
        }

        private static void ConfigureColumns(DataGridView grid)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "배치",
                DataPropertyName = "BatchName",
                FillWeight = 48,
                MinimumWidth = 260,
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "상태",
                DataPropertyName = "LocationText",
                FillWeight = 18,
                MinimumWidth = 110,
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "결과 수",
                DataPropertyName = "ResultCount",
                FillWeight = 12,
                MinimumWidth = 80,
            });
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                HeaderText = "수정 시각",
                DataPropertyName = "UpdatedAtText",
                FillWeight = 22,
                MinimumWidth = 150,
            });
        }

        private static Button CreateButton(string text, int width)
        {
            return new Button { Text = text, Size = new Size(width, 32), Margin = new Padding(0, 0, 8, 0) };
        }

        private StatisticsBatchItem SelectedItem
        {
            get
            {
                DataGridView grid = _tabs.SelectedIndex == 1 ? _trashGrid : _completedGrid;
                return grid.CurrentRow == null ? null : grid.CurrentRow.DataBoundItem as StatisticsBatchItem;
            }
        }

        private void Reload()
        {
            _completedGrid.DataSource = null;
            _completedGrid.DataSource = _service.GetCompletedBatchItems();
            _trashGrid.DataSource = null;
            _trashGrid.DataSource = _service.GetTrashedBatchItems();
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            StatisticsBatchItem selected = SelectedItem;
            _excludeButton.Visible = _tabs.SelectedIndex == 0;
            _restoreButton.Visible = _tabs.SelectedIndex == 1;
            _excludeButton.Enabled = selected != null && selected.CanMoveToTrash;
            _excludeButton.Text = selected != null && !selected.CanMoveToTrash
                ? "전달 후 제외 가능"
                : "통계에서 제외";
            _restoreButton.Enabled = selected != null;
        }

        private void OpenSelected(object sender, EventArgs e)
        {
            StatisticsBatchItem selected = SelectedItem;
            if (selected == null || !Directory.Exists(selected.BatchDirectory))
                return;
            Process.Start(new ProcessStartInfo
            {
                FileName = selected.BatchDirectory,
                UseShellExecute = true,
            });
        }

        private void ExcludeSelected(object sender, EventArgs e)
        {
            StatisticsBatchItem selected = SelectedItem;
            if (selected == null || !selected.CanMoveToTrash)
                return;
            if (MessageBox.Show(
                    $"{selected.BatchName}\n\n이 배치를 통계에서 제외할까요?\n파일은 archive\\_trash에 보존됩니다.",
                    "통계에서 제외",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;
            try
            {
                _service.MoveArchiveBatchToTrash(selected.BatchDirectory);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "통계 배치 관리", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void RestoreSelected(object sender, EventArgs e)
        {
            StatisticsBatchItem selected = SelectedItem;
            if (selected == null)
                return;
            try
            {
                _service.RestoreBatchFromTrash(selected.BatchDirectory);
                Reload();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "통계 배치 관리", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
