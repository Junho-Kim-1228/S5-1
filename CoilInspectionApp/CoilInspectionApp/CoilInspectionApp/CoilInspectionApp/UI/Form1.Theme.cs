using System;
using System.Drawing;
using System.Windows.Forms;

namespace CoilInspectionApp
{
    public partial class Form1
    {
        private static readonly Color ThemeCanvas = Color.FromArgb(243, 246, 250);
        private static readonly Color ThemeSurface = Color.FromArgb(255, 255, 255);
        private static readonly Color ThemeSurfaceMuted = Color.FromArgb(238, 243, 248);
        private static readonly Color ThemeBorder = Color.FromArgb(213, 222, 232);
        private static readonly Color ThemeTextPrimary = Color.FromArgb(23, 32, 51);
        private static readonly Color ThemeTextMuted = Color.FromArgb(102, 112, 133);
        private static readonly Color ThemePrimary = Color.FromArgb(37, 99, 235);
        private static readonly Color ThemePrimaryHover = Color.FromArgb(29, 78, 216);
        private static readonly Color ThemeNavy = Color.FromArgb(15, 28, 51);
        private static readonly Color ThemeSuccess = Color.FromArgb(21, 128, 61);
        private static readonly Color ThemeSuccessSurface = Color.FromArgb(220, 252, 231);
        private static readonly Color ThemeWarning = Color.FromArgb(194, 65, 12);
        private static readonly Color ThemeDanger = Color.FromArgb(220, 38, 38);
        private static readonly Color ThemeDangerSurface = Color.FromArgb(254, 226, 226);

        private readonly Font _themeBodyFont = new Font("맑은 고딕", 9F, FontStyle.Regular);
        private readonly Font _themeMediumFont = new Font("맑은 고딕", 9F, FontStyle.Bold);
        private readonly Font _themeSectionFont = new Font("맑은 고딕", 10F, FontStyle.Bold);
        private readonly ToolTip _themePathToolTip = new ToolTip();
        private Label _themeHeaderStatusLabel;

        private void ApplyModernVisualTheme()
        {
            SuspendLayout();
            splitContainerMain.SuspendLayout();

            Text = "Coil Vision  |  AI Inspection Console";
            BackColor = ThemeCanvas;
            ForeColor = ThemeTextPrimary;
            Font = _themeBodyFont;

            InstallApplicationHeader();

            splitContainerMain.BackColor = ThemeBorder;
            splitContainerMain.SplitterWidth = 6;
            splitContainerMain.Panel1.BackColor = ThemeCanvas;
            splitContainerMain.Panel2.BackColor = ThemeCanvas;

            StyleSection(groupBoxBatch, "실행 환경");
            StyleSection(groupBoxResult, "선택 이미지 분석");
            StyleProgressPanel();
            StyleResultList();
            StyleLabels();
            StyleButtons();
            StyleContextMenu();
            ConfigureResponsiveLayout();

            pictureBox1.BackColor = Color.FromArgb(8, 13, 23);
            pictureBox1.BorderStyle = BorderStyle.FixedSingle;

            splitContainerMain.ResumeLayout(true);
            ResumeLayout(true);
        }

        private void InstallApplicationHeader()
        {
            var header = new Panel
            {
                Name = "panelApplicationHeader",
                Dock = DockStyle.Top,
                Height = 56,
                Width = ClientSize.Width,
                BackColor = ThemeNavy,
                TabStop = false
            };

            var title = new Label
            {
                AutoSize = true,
                Location = new Point(18, 12),
                Font = new Font("Segoe UI Semibold", 15F, FontStyle.Bold),
                ForeColor = Color.White,
                Text = "COIL VISION"
            };
            var subtitle = new Label
            {
                AutoSize = true,
                Location = new Point(155, 19),
                Font = new Font("Segoe UI", 9F, FontStyle.Regular),
                ForeColor = Color.FromArgb(177, 194, 220),
                Text = "AI INSPECTION CONSOLE  ·  ANOMA → YOLO"
            };
            _themeHeaderStatusLabel = new Label
            {
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Location = new Point(Math.Max(600, ClientSize.Width - 132), 15),
                Size = new Size(108, 26),
                BackColor = Color.FromArgb(30, 50, 80),
                ForeColor = Color.FromArgb(191, 219, 254),
                Font = new Font("Segoe UI Semibold", 8F, FontStyle.Bold),
                Text = "INITIALIZING",
                TextAlign = ContentAlignment.MiddleCenter
            };
            var accent = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = ThemePrimary,
                TabStop = false
            };

            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(_themeHeaderStatusLabel);
            header.Controls.Add(accent);
            Controls.Add(header);
        }

        private void StyleSection(GroupBox groupBox, string title)
        {
            groupBox.Text = title;
            groupBox.BackColor = ThemeSurface;
            groupBox.ForeColor = ThemeTextPrimary;
            groupBox.Font = _themeMediumFont;
            groupBox.FlatStyle = FlatStyle.Flat;
        }

        private void StyleProgressPanel()
        {
            panelPipelineProgress.BackColor = ThemeSurface;
            panelPipelineProgress.ForeColor = ThemeTextPrimary;
            panelPipelineProgress.BorderStyle = BorderStyle.FixedSingle;
            labelInputProgress.Font = _themeMediumFont;
            labelInputProgress.ForeColor = ThemeTextPrimary;
            labelPreprocessProgress.ForeColor = ThemeTextMuted;
            labelInferenceProgress.ForeColor = ThemeTextMuted;
            labelAutoCloseProgress.ForeColor = ThemeTextMuted;
        }

        private void StyleResultList()
        {
            labelRecentResults.Text = "검사 결과";
            labelRecentResults.Font = _themeSectionFont;
            labelRecentResults.ForeColor = ThemeTextPrimary;

            listViewResults.BackColor = ThemeSurface;
            listViewResults.ForeColor = ThemeTextPrimary;
            listViewResults.Font = _themeBodyFont;
            listViewResults.BorderStyle = BorderStyle.FixedSingle;
            listViewResults.GridLines = false;
            listViewResults.HeaderStyle = ColumnHeaderStyle.Nonclickable;

            columnHeaderNo.Text = "번호";
            columnHeaderFile.Text = "파일명";
            columnHeaderPreprocess.Text = "전처리";
            columnHeaderStage1.Text = "Anoma";
            columnHeaderStage2.Text = "YOLO";
            columnHeaderFinal.Text = "최종 판정";
            columnHeaderScore.Text = "이상 점수";
        }

        private void StyleLabels()
        {
            labelPipeline.Text = "파이프라인";
            labelInput.Text = "입력 폴더";
            labelPackage.Text = "모델 패키지";
            labelBatch.Text = "공유 배치";
            buttonSelectBatch.Text = "경로";

            labelFile.Text = "파일";
            labelStage1.Text = "Anoma 판정";
            labelStage2.Text = "YOLO 결과";
            labelFinal.Text = "최종 판정";
            labelScore.Text = "Anoma 점수";
            labelDetections.Text = "검출 수";
            labelReasons.Text = "상세 사유";

            Label[] captions =
            {
                labelPipeline, labelInput, labelPackage, labelBatch,
                labelFile, labelStage1, labelStage2, labelFinal,
                labelScore, labelDetections, labelReasons
            };
            foreach (Label caption in captions)
            {
                caption.Font = _themeMediumFont;
                caption.ForeColor = ThemeTextMuted;
            }

            Label[] values =
            {
                labelValuePipeline, labelValueInput, labelValuePackage, labelValueBatch,
                labelValueFile, labelValueStage1, labelValueStage2, labelValueFinal,
                labelValueScore, labelValueDetections, labelValueReasons
            };
            foreach (Label value in values)
            {
                value.Font = _themeBodyFont;
                value.ForeColor = ThemeTextPrimary;
            }

            labelValuePipeline.Font = _themeMediumFont;
            labelValuePipeline.ForeColor = ThemePrimary;
            labelValueFinal.Font = _themeSectionFont;

            ConfigurePathLabel(labelValueInput);
            ConfigurePathLabel(labelValuePackage);
            ConfigurePathLabel(labelValueBatch);
        }

        private void ConfigurePathLabel(Label label)
        {
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Height = Math.Max(18, labelValueInput.Height);
            label.UseMnemonic = false;
            label.TextChanged += delegate { _themePathToolTip.SetToolTip(label, label.Text); };
            _themePathToolTip.SetToolTip(label, label.Text);
        }

        private void StyleButtons()
        {
            StyleButton(buttonSelectInput, ThemePrimary, Color.White, ThemePrimary);
            StyleButton(buttonSelectPackage, ThemePrimary, Color.White, ThemePrimary);
            StyleButton(buttonSelectBatch, ThemePrimary, Color.White, ThemePrimary);

            StyleButton(buttonOpenInput, ThemeSurface, ThemeTextPrimary, ThemeBorder);
            StyleButton(buttonOpenPackage, ThemeSurface, ThemeTextPrimary, ThemeBorder);
            StyleButton(buttonOpenBatch, ThemeSurface, ThemeTextPrimary, ThemeBorder);

            StyleButton(buttonRefreshInput, ThemeSurfaceMuted, ThemeTextPrimary, ThemeBorder);
            StyleButton(buttonStatistics, ThemeNavy, Color.White, ThemeNavy);
            StyleButton(buttonZoomIn, ThemeSurface, ThemeTextPrimary, ThemeBorder);
            StyleButton(buttonZoomOut, ThemeSurface, ThemeTextPrimary, ThemeBorder);
            StyleButton(buttonZoomFit, ThemeSurface, ThemeTextPrimary, ThemeBorder);
            StyleButton(button2, ThemePrimary, Color.White, ThemePrimary);
            button2.Font = _themeMediumFont;

            StyleAutoCloseToggle();
        }

        private void StyleButton(ButtonBase button, Color background, Color foreground, Color border)
        {
            button.Font = _themeMediumFont;
            button.BackColor = background;
            button.ForeColor = foreground;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = background == ThemePrimary
                ? ThemePrimaryHover
                : ControlPaint.Light(background, 0.03F);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background, 0.05F);
            button.UseVisualStyleBackColor = false;
            button.Cursor = Cursors.Hand;
        }

        private void StyleAutoCloseToggle()
        {
            Color background = _isAutoClosePaused ? ThemeDangerSurface : ThemeSuccessSurface;
            Color foreground = _isAutoClosePaused ? ThemeDanger : ThemeSuccess;
            StyleButton(buttonToggleAutoClose, background, foreground, foreground);
            buttonToggleAutoClose.FlatAppearance.CheckedBackColor = ThemeSuccessSurface;
        }

        private void StyleContextMenu()
        {
            _resultContextMenu.BackColor = ThemeSurface;
            _resultContextMenu.ForeColor = ThemeTextPrimary;
            _resultContextMenu.Font = _themeBodyFont;
            _resultContextMenu.ShowImageMargin = false;
            _resultContextMenu.Padding = new Padding(4);
            foreach (ToolStripItem item in _resultContextMenu.Items)
            {
                item.BackColor = ThemeSurface;
                item.ForeColor = ThemeTextPrimary;
                item.Padding = new Padding(8, 3, 8, 3);
            }
        }

        private void ConfigureResponsiveLayout()
        {
            _themePathToolTip.AutoPopDelay = 12000;
            _themePathToolTip.InitialDelay = 350;
            _themePathToolTip.ReshowDelay = 100;
            _themePathToolTip.ShowAlways = true;

            groupBoxBatch.Resize += delegate { LayoutRuntimePathRows(); };
            listViewResults.Resize += delegate { UpdateResultListColumnWidths(); };
            LayoutRuntimePathRows();
            UpdateResultListColumnWidths();
        }

        private void LayoutRuntimePathRows()
        {
            LayoutRuntimePathRow(labelValueInput, buttonSelectInput, buttonOpenInput);
            LayoutRuntimePathRow(labelValuePackage, buttonSelectPackage, buttonOpenPackage);
            LayoutRuntimePathRow(labelValueBatch, buttonSelectBatch, buttonOpenBatch);
        }

        private void LayoutRuntimePathRow(Label value, Button selectButton, Button openButton)
        {
            const int rightPadding = 14;
            const int gap = 6;
            const int valueGap = 10;

            openButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            selectButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            openButton.Left = Math.Max(value.Left + 120, groupBoxBatch.ClientSize.Width - rightPadding - openButton.Width);
            selectButton.Left = openButton.Left - gap - selectButton.Width;
            value.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            value.Width = Math.Max(80, selectButton.Left - valueGap - value.Left);
        }

        private void UpdateResultListColumnWidths()
        {
            int available = Math.Max(0, listViewResults.ClientSize.Width - 4);
            int numberWidth = 48;
            int preprocessWidth = 78;
            int anomaWidth = 78;
            int yoloWidth = 78;
            int finalWidth = 88;
            int scoreWidth = 88;
            int fixedWidth = numberWidth + preprocessWidth + anomaWidth + yoloWidth + finalWidth + scoreWidth;

            columnHeaderNo.Width = numberWidth;
            columnHeaderPreprocess.Width = preprocessWidth;
            columnHeaderStage1.Width = anomaWidth;
            columnHeaderStage2.Width = yoloWidth;
            columnHeaderFinal.Width = finalWidth;
            columnHeaderScore.Width = scoreWidth;
            columnHeaderFile.Width = Math.Max(170, available - fixedWidth);
        }

        private void UpdateThemeHeaderStatus()
        {
            if (_themeHeaderStatusLabel == null)
                return;

            bool ready = _servicesInitialized;
            _themeHeaderStatusLabel.Text = ready ? "SYSTEM READY" : "SETUP REQUIRED";
            _themeHeaderStatusLabel.BackColor = ready
                ? Color.FromArgb(20, 83, 65)
                : Color.FromArgb(96, 52, 45);
            _themeHeaderStatusLabel.ForeColor = ready
                ? Color.FromArgb(167, 243, 208)
                : Color.FromArgb(254, 202, 202);
        }

        private void ApplySelectedResultStatusColors(InspectionResultViewModel result)
        {
            labelValueStage1.ForeColor = ResolveStatusColor(result.Stage1);
            labelValueStage2.ForeColor = ResolveStatusColor(result.Stage2);
            labelValueFinal.ForeColor = ResolveStatusColor(result.Final);
        }

        private void ResetSelectedResultStatusColors()
        {
            labelValueStage1.ForeColor = ThemeTextPrimary;
            labelValueStage2.ForeColor = ThemeTextPrimary;
            labelValueFinal.ForeColor = ThemeTextPrimary;
        }
    }
}
