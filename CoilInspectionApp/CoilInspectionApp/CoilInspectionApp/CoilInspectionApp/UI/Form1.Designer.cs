namespace CoilInspectionApp
{
    partial class Form1
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.splitContainerMain = new System.Windows.Forms.SplitContainer();
            this.groupBoxBatch = new System.Windows.Forms.GroupBox();
            this.labelValueBatch = new System.Windows.Forms.Label();
            this.labelBatch = new System.Windows.Forms.Label();
            this.labelValuePackage = new System.Windows.Forms.Label();
            this.labelPackage = new System.Windows.Forms.Label();
            this.buttonOpenPackage = new System.Windows.Forms.Button();
            this.labelValueInput = new System.Windows.Forms.Label();
            this.buttonOpenInput = new System.Windows.Forms.Button();
            this.labelInput = new System.Windows.Forms.Label();
            this.labelValuePipeline = new System.Windows.Forms.Label();
            this.labelPipeline = new System.Windows.Forms.Label();
            this.buttonOpenBatch = new System.Windows.Forms.Button();
            this.listViewResults = new System.Windows.Forms.ListView();
            this.columnHeaderNo = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderFile = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderPreprocess = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderStage1 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderStage2 = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderFinal = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.columnHeaderScore = ((System.Windows.Forms.ColumnHeader)(new System.Windows.Forms.ColumnHeader()));
            this.labelRecentResults = new System.Windows.Forms.Label();
            this.pictureBox1 = new System.Windows.Forms.PictureBox();
            this.groupBoxResult = new System.Windows.Forms.GroupBox();
            this.labelValueReasons = new System.Windows.Forms.Label();
            this.labelReasons = new System.Windows.Forms.Label();
            this.labelValueDetections = new System.Windows.Forms.Label();
            this.labelDetections = new System.Windows.Forms.Label();
            this.labelValueScore = new System.Windows.Forms.Label();
            this.labelScore = new System.Windows.Forms.Label();
            this.labelValueFinal = new System.Windows.Forms.Label();
            this.labelFinal = new System.Windows.Forms.Label();
            this.labelValueStage2 = new System.Windows.Forms.Label();
            this.labelStage2 = new System.Windows.Forms.Label();
            this.labelValueStage1 = new System.Windows.Forms.Label();
            this.labelStage1 = new System.Windows.Forms.Label();
            this.labelValueFile = new System.Windows.Forms.Label();
            this.labelFile = new System.Windows.Forms.Label();
            this.panelPipelineProgress = new System.Windows.Forms.Panel();
            this.labelInputProgress = new System.Windows.Forms.Label();
            this.buttonToggleAutoClose = new System.Windows.Forms.CheckBox();
            this.labelPreprocessProgress = new System.Windows.Forms.Label();
            this.progressBarPreprocess = new System.Windows.Forms.ProgressBar();
            this.progressBarInference = new System.Windows.Forms.ProgressBar();
            this.labelInferenceProgress = new System.Windows.Forms.Label();
            this.labelAutoCloseProgress = new System.Windows.Forms.Label();
            this.buttonRefreshInput = new System.Windows.Forms.Button();
            this.buttonStatistics = new System.Windows.Forms.Button();
            this.buttonZoomIn = new System.Windows.Forms.Button();
            this.buttonZoomOut = new System.Windows.Forms.Button();
            this.buttonZoomFit = new System.Windows.Forms.Button();
            this.button2 = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).BeginInit();
            this.splitContainerMain.Panel1.SuspendLayout();
            this.splitContainerMain.Panel2.SuspendLayout();
            this.splitContainerMain.SuspendLayout();
            this.groupBoxBatch.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).BeginInit();
            this.groupBoxResult.SuspendLayout();
            this.panelPipelineProgress.SuspendLayout();
            this.SuspendLayout();
            // 
            // splitContainerMain
            // 
            this.splitContainerMain.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainerMain.Location = new System.Drawing.Point(0, 0);
            this.splitContainerMain.Name = "splitContainerMain";
            // 
            // splitContainerMain.Panel1
            // 
            this.splitContainerMain.Panel1.Controls.Add(this.listViewResults);
            this.splitContainerMain.Panel1.Controls.Add(this.labelRecentResults);
            this.splitContainerMain.Panel1.Controls.Add(this.groupBoxBatch);
            // 
            // splitContainerMain.Panel2
            // 
            this.splitContainerMain.Panel2.Controls.Add(this.panelPipelineProgress);
            this.splitContainerMain.Panel2.Controls.Add(this.buttonRefreshInput);
            this.splitContainerMain.Panel2.Controls.Add(this.buttonStatistics);
            this.splitContainerMain.Panel2.Controls.Add(this.buttonToggleAutoClose);
            this.splitContainerMain.Panel2.Controls.Add(this.buttonZoomIn);
            this.splitContainerMain.Panel2.Controls.Add(this.buttonZoomOut);
            this.splitContainerMain.Panel2.Controls.Add(this.buttonZoomFit);
            this.splitContainerMain.Panel2.Controls.Add(this.button2);
            this.splitContainerMain.Panel2.Controls.Add(this.groupBoxResult);
            this.splitContainerMain.Panel2.Controls.Add(this.pictureBox1);
            this.splitContainerMain.Size = new System.Drawing.Size(1434, 861);
            this.splitContainerMain.SplitterDistance = 570;
            this.splitContainerMain.TabIndex = 0;
            // 
            // groupBoxBatch
            // 
            this.groupBoxBatch.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxBatch.Controls.Add(this.labelValueBatch);
            this.groupBoxBatch.Controls.Add(this.labelBatch);
            this.groupBoxBatch.Controls.Add(this.labelValuePackage);
            this.groupBoxBatch.Controls.Add(this.labelPackage);
            this.groupBoxBatch.Controls.Add(this.buttonOpenPackage);
            this.groupBoxBatch.Controls.Add(this.labelValueInput);
            this.groupBoxBatch.Controls.Add(this.buttonOpenInput);
            this.groupBoxBatch.Controls.Add(this.labelInput);
            this.groupBoxBatch.Controls.Add(this.labelValuePipeline);
            this.groupBoxBatch.Controls.Add(this.labelPipeline);
            this.groupBoxBatch.Controls.Add(this.buttonOpenBatch);
            this.groupBoxBatch.Location = new System.Drawing.Point(12, 12);
            this.groupBoxBatch.Name = "groupBoxBatch";
            this.groupBoxBatch.Size = new System.Drawing.Size(546, 173);
            this.groupBoxBatch.TabIndex = 0;
            this.groupBoxBatch.TabStop = false;
            this.groupBoxBatch.Text = "실행 정보";
            // 
            // labelValueBatch
            // 
            this.labelValueBatch.AutoEllipsis = true;
            this.labelValueBatch.Location = new System.Drawing.Point(101, 110);
            this.labelValueBatch.Name = "labelValueBatch";
            this.labelValueBatch.Size = new System.Drawing.Size(397, 48);
            this.labelValueBatch.TabIndex = 7;
            this.labelValueBatch.Text = "-";
            // 
            // labelBatch
            // 
            this.labelBatch.AutoSize = true;
            this.labelBatch.Location = new System.Drawing.Point(18, 110);
            this.labelBatch.Name = "labelBatch";
            this.labelBatch.Size = new System.Drawing.Size(65, 12);
            this.labelBatch.TabIndex = 6;
            this.labelBatch.Text = "배치 출력 :";
            // 
            // labelValuePackage
            // 
            this.labelValuePackage.AutoEllipsis = true;
            this.labelValuePackage.Location = new System.Drawing.Point(101, 70);
            this.labelValuePackage.Name = "labelValuePackage";
            this.labelValuePackage.Size = new System.Drawing.Size(397, 36);
            this.labelValuePackage.TabIndex = 5;
            this.labelValuePackage.Text = "-";
            // 
            // labelPackage
            // 
            this.labelPackage.AutoSize = true;
            this.labelPackage.Location = new System.Drawing.Point(18, 70);
            this.labelPackage.Name = "labelPackage";
            this.labelPackage.Size = new System.Drawing.Size(77, 12);
            this.labelPackage.TabIndex = 4;
            this.labelPackage.Text = "패키지 경로 :";
            //
            // buttonOpenPackage
            //
            this.buttonOpenPackage.Location = new System.Drawing.Point(504, 68);
            this.buttonOpenPackage.Name = "buttonOpenPackage";
            this.buttonOpenPackage.Size = new System.Drawing.Size(28, 22);
            this.buttonOpenPackage.TabIndex = 10;
            this.buttonOpenPackage.Text = "...";
            this.buttonOpenPackage.UseVisualStyleBackColor = true;
            this.buttonOpenPackage.Click += new System.EventHandler(this.buttonOpenPackage_Click);
            // 
            // labelValueInput
            // 
            this.labelValueInput.AutoEllipsis = true;
            this.labelValueInput.Location = new System.Drawing.Point(101, 44);
            this.labelValueInput.Name = "labelValueInput";
            this.labelValueInput.Size = new System.Drawing.Size(397, 18);
            this.labelValueInput.TabIndex = 3;
            this.labelValueInput.Text = "-";
            // 
            // buttonOpenInput
            // 
            this.buttonOpenInput.Location = new System.Drawing.Point(504, 40);
            this.buttonOpenInput.Name = "buttonOpenInput";
            this.buttonOpenInput.Size = new System.Drawing.Size(28, 22);
            this.buttonOpenInput.TabIndex = 9;
            this.buttonOpenInput.Text = "...";
            this.buttonOpenInput.UseVisualStyleBackColor = true;
            this.buttonOpenInput.Click += new System.EventHandler(this.buttonOpenInput_Click);
            // 
            // labelInput
            // 
            this.labelInput.AutoSize = true;
            this.labelInput.Location = new System.Drawing.Point(18, 44);
            this.labelInput.Name = "labelInput";
            this.labelInput.Size = new System.Drawing.Size(77, 12);
            this.labelInput.TabIndex = 2;
            this.labelInput.Text = "입력 폴더 경로";
            // 
            // labelValuePipeline
            // 
            this.labelValuePipeline.AutoSize = true;
            this.labelValuePipeline.Location = new System.Drawing.Point(101, 22);
            this.labelValuePipeline.Name = "labelValuePipeline";
            this.labelValuePipeline.Size = new System.Drawing.Size(9, 12);
            this.labelValuePipeline.TabIndex = 1;
            this.labelValuePipeline.Text = "-";
            // 
            // labelPipeline
            // 
            this.labelPipeline.AutoSize = true;
            this.labelPipeline.Location = new System.Drawing.Point(18, 22);
            this.labelPipeline.Name = "labelPipeline";
            this.labelPipeline.Size = new System.Drawing.Size(77, 12);
            this.labelPipeline.TabIndex = 0;
            this.labelPipeline.Text = "파이프라인 :";
            // 
            // buttonOpenBatch
            // 
            this.buttonOpenBatch.Location = new System.Drawing.Point(504, 106);
            this.buttonOpenBatch.Name = "buttonOpenBatch";
            this.buttonOpenBatch.Size = new System.Drawing.Size(28, 22);
            this.buttonOpenBatch.TabIndex = 8;
            this.buttonOpenBatch.Text = "...";
            this.buttonOpenBatch.UseVisualStyleBackColor = true;
            this.buttonOpenBatch.Click += new System.EventHandler(this.buttonOpenBatch_Click);
            // 
            // listViewResults
            // 
            this.listViewResults.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.listViewResults.Columns.AddRange(new System.Windows.Forms.ColumnHeader[] {
            this.columnHeaderNo,
            this.columnHeaderFile,
            this.columnHeaderPreprocess,
            this.columnHeaderStage1,
            this.columnHeaderStage2,
            this.columnHeaderFinal,
            this.columnHeaderScore});
            this.listViewResults.FullRowSelect = true;
            this.listViewResults.GridLines = true;
            this.listViewResults.HideSelection = false;
            this.listViewResults.Location = new System.Drawing.Point(12, 214);
            this.listViewResults.MultiSelect = false;
            this.listViewResults.Name = "listViewResults";
            this.listViewResults.Size = new System.Drawing.Size(546, 635);
            this.listViewResults.TabIndex = 2;
            this.listViewResults.UseCompatibleStateImageBehavior = false;
            this.listViewResults.View = System.Windows.Forms.View.Details;
            this.listViewResults.MouseDown += new System.Windows.Forms.MouseEventHandler(this.listViewResults_MouseDown);
            this.listViewResults.SelectedIndexChanged += new System.EventHandler(this.listViewResults_SelectedIndexChanged);
            // 
            // columnHeaderNo
            //
            this.columnHeaderNo.Text = "번호";
            this.columnHeaderNo.Width = 38;
            //
            // columnHeaderFile
            // 
            this.columnHeaderFile.Text = "파일명";
            this.columnHeaderFile.Width = 160;
            //
            // columnHeaderPreprocess
            //
            this.columnHeaderPreprocess.Text = "전처리";
            this.columnHeaderPreprocess.Width = 60;
            // 
            // columnHeaderStage1
            // 
            this.columnHeaderStage1.Text = "이상 탐지";
            this.columnHeaderStage1.Width = 65;
            // 
            // columnHeaderStage2
            // 
            this.columnHeaderStage2.Text = "결함 검출";
            this.columnHeaderStage2.Width = 65;
            // 
            // columnHeaderFinal
            // 
            this.columnHeaderFinal.Text = "최종 판정";
            this.columnHeaderFinal.Width = 65;
            // 
            // columnHeaderScore
            // 
            this.columnHeaderScore.Text = "이상 점수";
            this.columnHeaderScore.Width = 60;
            // 
            // labelRecentResults
            // 
            this.labelRecentResults.AutoSize = true;
            this.labelRecentResults.Location = new System.Drawing.Point(12, 196);
            this.labelRecentResults.Name = "labelRecentResults";
            this.labelRecentResults.Size = new System.Drawing.Size(57, 12);
            this.labelRecentResults.TabIndex = 1;
            this.labelRecentResults.Text = "수신/추론 결과";
            //
            // panelPipelineProgress
            //
            this.panelPipelineProgress.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left)));
            this.panelPipelineProgress.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(245)))), ((int)(((byte)(247)))), ((int)(((byte)(250)))));
            this.panelPipelineProgress.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelPipelineProgress.Controls.Add(this.labelInputProgress);
            this.panelPipelineProgress.Controls.Add(this.labelAutoCloseProgress);
            this.panelPipelineProgress.Controls.Add(this.labelPreprocessProgress);
            this.panelPipelineProgress.Controls.Add(this.progressBarPreprocess);
            this.panelPipelineProgress.Controls.Add(this.labelInferenceProgress);
            this.panelPipelineProgress.Controls.Add(this.progressBarInference);
            this.panelPipelineProgress.Location = new System.Drawing.Point(14, 800);
            this.panelPipelineProgress.Name = "panelPipelineProgress";
            this.panelPipelineProgress.Size = new System.Drawing.Size(420, 44);
            this.panelPipelineProgress.TabIndex = 12;
            //
            // labelInputProgress
            //
            this.labelInputProgress.AutoSize = true;
            this.labelInputProgress.Font = new System.Drawing.Font("굴림", 9F, System.Drawing.FontStyle.Bold);
            this.labelInputProgress.Location = new System.Drawing.Point(8, 6);
            this.labelInputProgress.Name = "labelInputProgress";
            this.labelInputProgress.Size = new System.Drawing.Size(66, 12);
            this.labelInputProgress.TabIndex = 0;
            this.labelInputProgress.Text = "입력 0장";
            //
            // buttonToggleAutoClose
            //
            this.buttonToggleAutoClose.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonToggleAutoClose.Appearance = System.Windows.Forms.Appearance.Button;
            this.buttonToggleAutoClose.BackColor = System.Drawing.Color.Honeydew;
            this.buttonToggleAutoClose.Checked = true;
            this.buttonToggleAutoClose.CheckState = System.Windows.Forms.CheckState.Checked;
            this.buttonToggleAutoClose.Location = new System.Drawing.Point(612, 808);
            this.buttonToggleAutoClose.Name = "buttonToggleAutoClose";
            this.buttonToggleAutoClose.Size = new System.Drawing.Size(102, 34);
            this.buttonToggleAutoClose.TabIndex = 8;
            this.buttonToggleAutoClose.Text = "자동 마감 ON";
            this.buttonToggleAutoClose.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.buttonToggleAutoClose.UseVisualStyleBackColor = false;
            this.buttonToggleAutoClose.CheckedChanged += new System.EventHandler(this.buttonToggleAutoClose_CheckedChanged);
            //
            // labelAutoCloseProgress
            //
            this.labelAutoCloseProgress.AutoEllipsis = true;
            this.labelAutoCloseProgress.ForeColor = System.Drawing.Color.DimGray;
            this.labelAutoCloseProgress.Location = new System.Drawing.Point(8, 24);
            this.labelAutoCloseProgress.Name = "labelAutoCloseProgress";
            this.labelAutoCloseProgress.Size = new System.Drawing.Size(125, 14);
            this.labelAutoCloseProgress.TabIndex = 1;
            this.labelAutoCloseProgress.Text = "자동 마감 대기";
            //
            // labelPreprocessProgress
            //
            this.labelPreprocessProgress.AutoEllipsis = true;
            this.labelPreprocessProgress.Location = new System.Drawing.Point(140, 5);
            this.labelPreprocessProgress.Name = "labelPreprocessProgress";
            this.labelPreprocessProgress.Size = new System.Drawing.Size(125, 14);
            this.labelPreprocessProgress.TabIndex = 2;
            this.labelPreprocessProgress.Text = "전처리 0/0";
            //
            // progressBarPreprocess
            //
            this.progressBarPreprocess.Location = new System.Drawing.Point(140, 23);
            this.progressBarPreprocess.Name = "progressBarPreprocess";
            this.progressBarPreprocess.Size = new System.Drawing.Size(125, 13);
            this.progressBarPreprocess.TabIndex = 3;
            // 
            // progressBarInference
            // 
            this.progressBarInference.Location = new System.Drawing.Point(275, 23);
            this.progressBarInference.Name = "progressBarInference";
            this.progressBarInference.Size = new System.Drawing.Size(130, 13);
            this.progressBarInference.TabIndex = 6;
            // 
            // labelInferenceProgress
            // 
            this.labelInferenceProgress.AutoEllipsis = true;
            this.labelInferenceProgress.Location = new System.Drawing.Point(275, 5);
            this.labelInferenceProgress.Name = "labelInferenceProgress";
            this.labelInferenceProgress.Size = new System.Drawing.Size(130, 14);
            this.labelInferenceProgress.TabIndex = 7;
            this.labelInferenceProgress.Text = "추론 0/0";
            // 
            // buttonRefreshInput
            // 
            this.buttonRefreshInput.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonRefreshInput.Location = new System.Drawing.Point(440, 808);
            this.buttonRefreshInput.Name = "buttonRefreshInput";
            this.buttonRefreshInput.Size = new System.Drawing.Size(76, 34);
            this.buttonRefreshInput.TabIndex = 2;
            this.buttonRefreshInput.Text = "새로고침";
            this.buttonRefreshInput.UseVisualStyleBackColor = true;
            this.buttonRefreshInput.Click += new System.EventHandler(this.buttonRefreshInput_Click);
            //
            // buttonStatistics
            //
            this.buttonStatistics.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonStatistics.Location = new System.Drawing.Point(522, 808);
            this.buttonStatistics.Name = "buttonStatistics";
            this.buttonStatistics.Size = new System.Drawing.Size(84, 34);
            this.buttonStatistics.TabIndex = 13;
            this.buttonStatistics.Text = "통계 보기";
            this.buttonStatistics.UseVisualStyleBackColor = true;
            this.buttonStatistics.Click += new System.EventHandler(this.buttonStatistics_Click);
            // 
            // pictureBox1
            // 
            this.pictureBox1.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.pictureBox1.BackColor = System.Drawing.Color.Black;
            this.pictureBox1.Location = new System.Drawing.Point(14, 12);
            this.pictureBox1.Name = "pictureBox1";
            this.pictureBox1.Size = new System.Drawing.Size(832, 589);
            this.pictureBox1.SizeMode = System.Windows.Forms.PictureBoxSizeMode.Normal;
            this.pictureBox1.TabIndex = 0;
            this.pictureBox1.TabStop = true;
            this.pictureBox1.Paint += new System.Windows.Forms.PaintEventHandler(this.pictureBox1_Paint);
            this.pictureBox1.MouseDown += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseDown);
            this.pictureBox1.MouseEnter += new System.EventHandler(this.pictureBox1_MouseEnter);
            this.pictureBox1.MouseMove += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseMove);
            this.pictureBox1.MouseUp += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseUp);
            this.pictureBox1.MouseWheel += new System.Windows.Forms.MouseEventHandler(this.pictureBox1_MouseWheel);
            this.pictureBox1.Resize += new System.EventHandler(this.pictureBox1_Resize);
            // 
            // buttonZoomIn
            // 
            this.buttonZoomIn.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonZoomIn.Location = new System.Drawing.Point(696, 601);
            this.buttonZoomIn.Name = "buttonZoomIn";
            this.buttonZoomIn.Size = new System.Drawing.Size(42, 28);
            this.buttonZoomIn.TabIndex = 9;
            this.buttonZoomIn.Text = "+";
            this.buttonZoomIn.UseVisualStyleBackColor = true;
            this.buttonZoomIn.Click += new System.EventHandler(this.buttonZoomIn_Click);
            // 
            // buttonZoomOut
            // 
            this.buttonZoomOut.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonZoomOut.Location = new System.Drawing.Point(744, 601);
            this.buttonZoomOut.Name = "buttonZoomOut";
            this.buttonZoomOut.Size = new System.Drawing.Size(42, 28);
            this.buttonZoomOut.TabIndex = 10;
            this.buttonZoomOut.Text = "-";
            this.buttonZoomOut.UseVisualStyleBackColor = true;
            this.buttonZoomOut.Click += new System.EventHandler(this.buttonZoomOut_Click);
            // 
            // buttonZoomFit
            // 
            this.buttonZoomFit.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.buttonZoomFit.Location = new System.Drawing.Point(792, 601);
            this.buttonZoomFit.Name = "buttonZoomFit";
            this.buttonZoomFit.Size = new System.Drawing.Size(54, 28);
            this.buttonZoomFit.TabIndex = 11;
            this.buttonZoomFit.Text = "Fit";
            this.buttonZoomFit.UseVisualStyleBackColor = true;
            this.buttonZoomFit.Click += new System.EventHandler(this.buttonZoomFit_Click);
            // 
            // groupBoxResult
            // 
            this.groupBoxResult.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.groupBoxResult.Controls.Add(this.labelValueReasons);
            this.groupBoxResult.Controls.Add(this.labelReasons);
            this.groupBoxResult.Controls.Add(this.labelValueDetections);
            this.groupBoxResult.Controls.Add(this.labelDetections);
            this.groupBoxResult.Controls.Add(this.labelValueScore);
            this.groupBoxResult.Controls.Add(this.labelScore);
            this.groupBoxResult.Controls.Add(this.labelValueFinal);
            this.groupBoxResult.Controls.Add(this.labelFinal);
            this.groupBoxResult.Controls.Add(this.labelValueStage2);
            this.groupBoxResult.Controls.Add(this.labelStage2);
            this.groupBoxResult.Controls.Add(this.labelValueStage1);
            this.groupBoxResult.Controls.Add(this.labelStage1);
            this.groupBoxResult.Controls.Add(this.labelValueFile);
            this.groupBoxResult.Controls.Add(this.labelFile);
            this.groupBoxResult.Location = new System.Drawing.Point(14, 635);
            this.groupBoxResult.Name = "groupBoxResult";
            this.groupBoxResult.Size = new System.Drawing.Size(832, 157);
            this.groupBoxResult.TabIndex = 1;
            this.groupBoxResult.TabStop = false;
            this.groupBoxResult.Text = "현재 선택 결과";
            // 
            // labelValueReasons
            // 
            this.labelValueReasons.AutoEllipsis = true;
            this.labelValueReasons.Location = new System.Drawing.Point(95, 125);
            this.labelValueReasons.Name = "labelValueReasons";
            this.labelValueReasons.Size = new System.Drawing.Size(717, 25);
            this.labelValueReasons.TabIndex = 13;
            this.labelValueReasons.Text = "-";
            // 
            // labelReasons
            // 
            this.labelReasons.AutoSize = true;
            this.labelReasons.Location = new System.Drawing.Point(20, 125);
            this.labelReasons.Name = "labelReasons";
            this.labelReasons.Size = new System.Drawing.Size(41, 12);
            this.labelReasons.TabIndex = 12;
            this.labelReasons.Text = "사유 :";
            // 
            // labelValueDetections
            // 
            this.labelValueDetections.AutoSize = true;
            this.labelValueDetections.Location = new System.Drawing.Point(535, 103);
            this.labelValueDetections.Name = "labelValueDetections";
            this.labelValueDetections.Size = new System.Drawing.Size(9, 12);
            this.labelValueDetections.TabIndex = 11;
            this.labelValueDetections.Text = "-";
            // 
            // labelDetections
            // 
            this.labelDetections.AutoSize = true;
            this.labelDetections.Location = new System.Drawing.Point(442, 103);
            this.labelDetections.Name = "labelDetections";
            this.labelDetections.Size = new System.Drawing.Size(65, 12);
            this.labelDetections.TabIndex = 10;
            this.labelDetections.Text = "검출 개수 :";
            // 
            // labelValueScore
            // 
            this.labelValueScore.AutoSize = true;
            this.labelValueScore.Location = new System.Drawing.Point(95, 103);
            this.labelValueScore.Name = "labelValueScore";
            this.labelValueScore.Size = new System.Drawing.Size(9, 12);
            this.labelValueScore.TabIndex = 9;
            this.labelValueScore.Text = "-";
            // 
            // labelScore
            // 
            this.labelScore.AutoSize = true;
            this.labelScore.Location = new System.Drawing.Point(20, 103);
            this.labelScore.Name = "labelScore";
            this.labelScore.Size = new System.Drawing.Size(63, 12);
            this.labelScore.TabIndex = 8;
            this.labelScore.Text = "Anoma 점수";
            // 
            // labelValueFinal
            // 
            this.labelValueFinal.AutoSize = true;
            this.labelValueFinal.Font = new System.Drawing.Font("굴림", 9F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(129)));
            this.labelValueFinal.Location = new System.Drawing.Point(535, 67);
            this.labelValueFinal.Name = "labelValueFinal";
            this.labelValueFinal.Size = new System.Drawing.Size(10, 12);
            this.labelValueFinal.TabIndex = 7;
            this.labelValueFinal.Text = "-";
            // 
            // labelFinal
            // 
            this.labelFinal.AutoSize = true;
            this.labelFinal.Location = new System.Drawing.Point(442, 67);
            this.labelFinal.Name = "labelFinal";
            this.labelFinal.Size = new System.Drawing.Size(37, 12);
            this.labelFinal.TabIndex = 6;
            this.labelFinal.Text = "Final :";
            // 
            // labelValueStage2
            // 
            this.labelValueStage2.AutoSize = true;
            this.labelValueStage2.Location = new System.Drawing.Point(95, 67);
            this.labelValueStage2.Name = "labelValueStage2";
            this.labelValueStage2.Size = new System.Drawing.Size(9, 12);
            this.labelValueStage2.TabIndex = 5;
            this.labelValueStage2.Text = "-";
            // 
            // labelStage2
            // 
            this.labelStage2.AutoSize = true;
            this.labelStage2.Location = new System.Drawing.Point(20, 67);
            this.labelStage2.Name = "labelStage2";
            this.labelStage2.Size = new System.Drawing.Size(50, 12);
            this.labelStage2.TabIndex = 4;
            this.labelStage2.Text = "Stage2 :";
            // 
            // labelValueStage1
            // 
            this.labelValueStage1.AutoSize = true;
            this.labelValueStage1.Location = new System.Drawing.Point(535, 31);
            this.labelValueStage1.Name = "labelValueStage1";
            this.labelValueStage1.Size = new System.Drawing.Size(9, 12);
            this.labelValueStage1.TabIndex = 3;
            this.labelValueStage1.Text = "-";
            // 
            // labelStage1
            // 
            this.labelStage1.AutoSize = true;
            this.labelStage1.Location = new System.Drawing.Point(442, 31);
            this.labelStage1.Name = "labelStage1";
            this.labelStage1.Size = new System.Drawing.Size(50, 12);
            this.labelStage1.TabIndex = 2;
            this.labelStage1.Text = "Stage1 :";
            // 
            // labelValueFile
            // 
            this.labelValueFile.AutoEllipsis = true;
            this.labelValueFile.Location = new System.Drawing.Point(95, 31);
            this.labelValueFile.Name = "labelValueFile";
            this.labelValueFile.Size = new System.Drawing.Size(304, 18);
            this.labelValueFile.TabIndex = 1;
            this.labelValueFile.Text = "-";
            // 
            // labelFile
            // 
            this.labelFile.AutoSize = true;
            this.labelFile.Location = new System.Drawing.Point(20, 31);
            this.labelFile.Name = "labelFile";
            this.labelFile.Size = new System.Drawing.Size(61, 12);
            this.labelFile.TabIndex = 0;
            this.labelFile.Text = "현재 파일 :";
            // 
            // button2
            // 
            this.button2.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Bottom | System.Windows.Forms.AnchorStyles.Right)));
            this.button2.Location = new System.Drawing.Point(736, 808);
            this.button2.Name = "button2";
            this.button2.Size = new System.Drawing.Size(110, 34);
            this.button2.TabIndex = 5;
            this.button2.Text = "배치 마감";
            this.button2.UseVisualStyleBackColor = true;
            this.button2.Click += new System.EventHandler(this.button2_Click);
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(1434, 861);
            this.Controls.Add(this.splitContainerMain);
            this.MinimumSize = new System.Drawing.Size(1250, 800);
            this.Name = "Form1";
            this.Text = "Coil Inspection App";
            this.Load += new System.EventHandler(this.Form1_Load);
            this.splitContainerMain.Panel1.ResumeLayout(false);
            this.splitContainerMain.Panel1.PerformLayout();
            this.splitContainerMain.Panel2.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.splitContainerMain)).EndInit();
            this.splitContainerMain.ResumeLayout(false);
            this.groupBoxBatch.ResumeLayout(false);
            this.groupBoxBatch.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.pictureBox1)).EndInit();
            this.groupBoxResult.ResumeLayout(false);
            this.groupBoxResult.PerformLayout();
            this.panelPipelineProgress.ResumeLayout(false);
            this.panelPipelineProgress.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.SplitContainer splitContainerMain;
        private System.Windows.Forms.GroupBox groupBoxBatch;
        private System.Windows.Forms.Label labelValueBatch;
        private System.Windows.Forms.Label labelBatch;
        private System.Windows.Forms.Label labelValuePackage;
        private System.Windows.Forms.Label labelPackage;
        private System.Windows.Forms.Button buttonOpenPackage;
        private System.Windows.Forms.Label labelValueInput;
        private System.Windows.Forms.Button buttonOpenInput;
        private System.Windows.Forms.Label labelInput;
        private System.Windows.Forms.Label labelValuePipeline;
        private System.Windows.Forms.Label labelPipeline;
        private System.Windows.Forms.Button buttonOpenBatch;
        private System.Windows.Forms.ListView listViewResults;
        private System.Windows.Forms.ColumnHeader columnHeaderNo;
        private System.Windows.Forms.ColumnHeader columnHeaderFile;
        private System.Windows.Forms.ColumnHeader columnHeaderPreprocess;
        private System.Windows.Forms.ColumnHeader columnHeaderStage1;
        private System.Windows.Forms.ColumnHeader columnHeaderStage2;
        private System.Windows.Forms.ColumnHeader columnHeaderFinal;
        private System.Windows.Forms.ColumnHeader columnHeaderScore;
        private System.Windows.Forms.Label labelRecentResults;
        private System.Windows.Forms.PictureBox pictureBox1;
        private System.Windows.Forms.GroupBox groupBoxResult;
        private System.Windows.Forms.Label labelValueReasons;
        private System.Windows.Forms.Label labelReasons;
        private System.Windows.Forms.Label labelValueDetections;
        private System.Windows.Forms.Label labelDetections;
        private System.Windows.Forms.Label labelValueScore;
        private System.Windows.Forms.Label labelScore;
        private System.Windows.Forms.Label labelValueFinal;
        private System.Windows.Forms.Label labelFinal;
        private System.Windows.Forms.Label labelValueStage2;
        private System.Windows.Forms.Label labelStage2;
        private System.Windows.Forms.Label labelValueStage1;
        private System.Windows.Forms.Label labelStage1;
        private System.Windows.Forms.Label labelValueFile;
        private System.Windows.Forms.Label labelFile;
        private System.Windows.Forms.Panel panelPipelineProgress;
        private System.Windows.Forms.Label labelInputProgress;
        private System.Windows.Forms.CheckBox buttonToggleAutoClose;
        private System.Windows.Forms.Label labelPreprocessProgress;
        private System.Windows.Forms.ProgressBar progressBarPreprocess;
        private System.Windows.Forms.ProgressBar progressBarInference;
        private System.Windows.Forms.Label labelInferenceProgress;
        private System.Windows.Forms.Label labelAutoCloseProgress;
        private System.Windows.Forms.Button buttonRefreshInput;
        private System.Windows.Forms.Button buttonStatistics;
        private System.Windows.Forms.Button buttonZoomIn;
        private System.Windows.Forms.Button buttonZoomOut;
        private System.Windows.Forms.Button buttonZoomFit;
        private System.Windows.Forms.Button button2;
    }
}
