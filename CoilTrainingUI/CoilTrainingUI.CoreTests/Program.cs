using CoilTrainingUI.Converters;
using CoilTrainingUI.Managers;
using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Imaging;
using CoilTrainingUI.Services.Review;
using CoilTrainingUI.Services.Automation;
using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Rectangle = System.Windows.Shapes.Rectangle;

var suite = new CoreReviewTests();
suite.RunAll();

internal sealed class CoreReviewTests
{
    private readonly ReviewRepository _repository = new();
    private readonly ReviewWorkflowService _workflow = new();
    private readonly AutoReviewService _autoReview = new();
    private readonly TrainingDatasetSelector _selector;
    private int _passed;

    public CoreReviewTests()
    {
        _selector = new TrainingDatasetSelector(_repository);
    }

    public void RunAll()
    {
        string root = Path.Combine(Path.GetTempPath(), "coil-review-core-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Run("batch manifest reader remains read-only", () => BatchManifestReaderIsReadOnly(root));
            Run("confirmed normal survives reload", () => ConfirmedNormalSurvivesReload(root));
            Run("confirmed defect survives reload", () => ConfirmedDefectSurvivesReload(root));
            Run("accepted and edited boxes survive reload", () => BoxesSurviveReload(root));
            Run("confirmed zero boxes never falls back", () => ConfirmedZeroBoxesSurviveReload(root));
            Run("defect without boxes is Anoma-only", () => DefectWithoutBoxesIsAnomaOnly(root));
            Run("unreviewed is excluded from all training", () => UnreviewedIsExcluded(root));
            Run("training use toggle preserves decision and boxes", () => TrainingUseTogglePreservesReview(root));
            Run("legacy migration is backed up and idempotent", () => MigrationIsSafeAndIdempotent(root));
            Run("ambiguous legacy data stays reviewing", () => AmbiguousMigrationStaysReviewing(root));
            Run("selection stays inside supplied batch scope", () => SelectionStaysInScope(root));
            Run("YOLO backgrounds are balanced automatically", () => YoloBackgroundsAreBalancedAutomatically(root));
            Run("projection flags match persisted state", () => ProjectionFlagsMatchState(root));
            Run("image list colors distinguish pending box review", ImageListColorsDistinguishPendingBoxes);
            Run("Anoma alone controls accepted AI decision", AcceptAiDecisionUsesAnomaOnly);
            Run("pipeline contract is Anoma then YOLO without fusion", PipelineContractIsCorrect);
            Run("Anoma package uses exported resize calibration", () => AnomaPackageUsesExportedResizeCalibration(root));
            Run("inference context mismatch is rejected", () => InferenceContextMismatchIsRejected(root));
            Run("inference result inconsistencies are rejected", () => InferenceResultInconsistenciesAreRejected(root));
            Run("final training commands are explicit", FinalTrainingCommandsAreExplicit);
            Run("fine-tune command uses warm-start policy", FineTuneCommandUsesWarmStartPolicy);
            Run("training ETA tracks Anoma steps and YOLO epochs", TrainingEtaTracksAnomaAndYolo);
            Run("Python runner serializes stdout and stderr logs", () => PythonRunnerSerializesLogs(root));
            Run("Python environment validation follows selected pipeline", () => PythonEnvironmentValidationFollowsPipeline(root));
            Run("model registry tracks lifecycle and lineage", () => ModelRegistryTracksLifecycle(root));
            Run("inference package deployment is validated and backed up", () => InferencePackageDeploymentIsSafe(root));
            Run("inference package rejects a missing mask model", () => MissingMaskModelIsRejected(root));
            Run("image cache reuses and invalidates frozen bitmaps", () => ImageCacheIsBoundedAndFresh(root));
            Run("bounding box edges resize within image bounds", BoundingBoxEdgesResizeWithinBounds);
            Run("auto review accepts high-confidence normal", () => AutoReviewAcceptsNormal(root));
            Run("auto review confirms only high-confidence boxes", () => AutoReviewConfirmsHighConfidenceBoxes(root));
            Run("auto-reviewed boxless defect stays Anoma-only", () => AutoReviewBoxlessDefectIsAnomaOnly(root));
            Run("YOLO exclusions distinguish low-confidence and missing boxes", YoloExclusionsDistinguishLowConfidenceAndMissingBoxes);
            Run("auto review leaves gray-zone prediction untouched", AutoReviewLeavesGrayZoneUntouched);
            Run("auto review never holds audit samples", AutoReviewNeverHoldsAuditSamples);
            Run("auto review protects existing user state", AutoReviewProtectsExistingState);
            Run("prediction-only boxes stay out of editable layer", PredictionOnlyBoxesStayOutOfEditableLayer);
            Run("automation imports completed batches exactly once", () => AutomationImportsCompletedBatchExactlyOnce(root));
            Run("automation rejects batch id conflicts", () => AutomationRejectsBatchIdConflict(root));
            Run("automation cleans failed batch staging", () => AutomationCleansFailedBatchStaging(root));
            Run("full model release is immutable and deterministic", () => FullModelReleaseIsImmutable(root));
            Run("activation requests are single-pending and cancellable", () => ActivationRequestsAreSinglePending(root));
            Run("activation result alone advances model reference", () => ActivationResultAdvancesReference(root));
            Run("release path traversal is rejected", () => ReleasePathTraversalIsRejected(root));
            Console.WriteLine($"PASS: {_passed} core review tests");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void TrainingEtaTracksAnomaAndYolo()
    {
        var startedAt = new DateTimeOffset(2026, 7, 24, 9, 0, 0, TimeSpan.Zero);
        var anomaEstimator = new TrainingEtaEstimator();

        TrainingProgressSnapshot firstStep = anomaEstimator.ObserveDinomalyStep(
            "Dinomaly step 1/5000 loss=1.0",
            startedAt) ?? throw new InvalidOperationException("first Dinomaly step was not parsed");
        Assert(firstStep.CurrentUnit == 1 && firstStep.TotalUnits == 5000 &&
               firstStep.EstimatedRemaining == null,
            "Anoma ETA should wait for a measured step interval");

        TrainingProgressSnapshot laterStep = anomaEstimator.ObserveDinomalyStep(
            "Dinomaly step 101/5000 loss=0.5",
            startedAt.AddSeconds(50)) ?? throw new InvalidOperationException("later Dinomaly step was not parsed");
        Assert(laterStep.Percent == 2 && laterStep.Elapsed == TimeSpan.FromSeconds(50),
            "Anoma step progress was calculated incorrectly");
        Assert(laterStep.EstimatedRemaining.HasValue &&
               Math.Abs(laterStep.EstimatedRemaining.Value.TotalSeconds - 2449.5) < 0.01,
            "Anoma remaining time was calculated incorrectly");

        var yoloEstimator = new TrainingEtaEstimator();
        TrainingProgressSnapshot firstEpoch = yoloEstimator.ObserveYoloEpoch(
            "[ERR]        1/100       12.3G",
            expectedTotalEpochs: 100,
            observedAt: startedAt) ?? throw new InvalidOperationException("first YOLO epoch was not parsed");
        TrainingProgressSnapshot laterEpoch = yoloEstimator.ObserveYoloEpoch(
            "[ERR]       11/100       12.3G",
            expectedTotalEpochs: 100,
            observedAt: startedAt.AddMinutes(5)) ?? throw new InvalidOperationException("later YOLO epoch was not parsed");
        Assert(firstEpoch.Percent == 1 && laterEpoch.Percent == 11,
            "YOLO epoch progress was calculated incorrectly");
        Assert(laterEpoch.EstimatedRemaining.HasValue &&
               Math.Abs(laterEpoch.EstimatedRemaining.Value.TotalMinutes - 44.5) < 0.01,
            "YOLO remaining time was calculated incorrectly");
        Assert(yoloEstimator.ObserveYoloEpoch("158/158 batches", 100, startedAt) == null,
            "YOLO batch progress was mistaken for epoch progress");

        var shortYoloEstimator = new TrainingEtaEstimator();
        TrainingProgressSnapshot shortFirstEpoch = shortYoloEstimator.ObserveYoloEpoch(
            "[ERR] \u001b[K      1/5      3.29G      1.693      16.65",
            expectedTotalEpochs: 5,
            observedAt: startedAt) ?? throw new InvalidOperationException("ANSI YOLO epoch was not parsed");
        Assert(shortFirstEpoch.Percent == 20,
            "short YOLO epoch progress was calculated incorrectly");
        Assert(shortYoloEstimator.ObserveYoloEpoch(
                "[ERR] \u001b[K Class Images Instances: 100% 5/5 1.7s",
                expectedTotalEpochs: 5,
                observedAt: startedAt.AddMinutes(1)) == null,
            "YOLO validation batch progress was mistaken for final epoch progress");
        TrainingProgressSnapshot shortSecondEpoch = shortYoloEstimator.ObserveYoloEpoch(
            "[ERR] \u001b[K      2/5      3.29G      1.512      12.30",
            expectedTotalEpochs: 5,
            observedAt: startedAt.AddMinutes(2)) ?? throw new InvalidOperationException("second short YOLO epoch was not parsed");
        Assert(shortSecondEpoch.Percent == 40,
            "YOLO progress did not continue after validation output");
        Assert(TrainingEtaEstimator.FormatDuration(TimeSpan.FromSeconds(3661)) == "01:01:01",
            "training duration formatting is incorrect");
    }

    private static void PythonRunnerSerializesLogs(string root)
    {
        const int linesPerStream = 250;
        string testRoot = Path.Combine(root, "python-runner-log-test");
        Directory.CreateDirectory(testRoot);
        string scriptPath = Path.Combine(testRoot, "emit-output.cmd");
        string logPath = Path.Combine(testRoot, "combined.log");
        File.WriteAllText(
            scriptPath,
            "@echo off" + Environment.NewLine +
            $"for /L %%i in (1,1,{linesPerStream}) do (" + Environment.NewLine +
            "  echo out-%%i" + Environment.NewLine +
            "  echo err-%%i 1>&2" + Environment.NewLine +
            ")" + Environment.NewLine);

        var observed = new System.Collections.Concurrent.ConcurrentBag<string>();
        var runner = new PythonRunner();
        int exitCode = runner.RunAsync(
                pythonExe: Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe",
                scriptPath: "/d",
                args: $"/c call \"{scriptPath}\"",
                workingDir: testRoot,
                logPath: logPath,
                ct: CancellationToken.None,
                onOutputLine: observed.Add)
            .GetAwaiter()
            .GetResult();

        Assert(exitCode == 0, "PythonRunner test process failed");
        string[] logLines = File.ReadAllLines(logPath);
        Assert(logLines.Length == linesPerStream * 2,
            $"PythonRunner log lost output: expected {linesPerStream * 2}, actual {logLines.Length}");
        Assert(observed.Count == linesPerStream * 2,
            $"PythonRunner callback lost output: expected {linesPerStream * 2}, actual {observed.Count}");
        Assert(logLines.Any(line => line.Contains("out-250", StringComparison.Ordinal)),
            "PythonRunner stdout tail was not logged");
        Assert(logLines.Any(line => line.Contains("[ERR] err-250", StringComparison.Ordinal)),
            "PythonRunner stderr tail was not logged");
    }

    private static void ImageCacheIsBoundedAndFresh(string root)
    {
        string imagePath = Path.Combine(root, "cache_test.bmp");
        WriteBmp(imagePath, width: 8, height: 8, blue: 10);

        using var cache = new ImageBitmapCache(capacity: 3);
        BitmapSource first = cache.LoadCachedAsync(imagePath, CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        BitmapSource second = cache.LoadCachedAsync(imagePath, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(first.IsFrozen, "cached bitmap must be frozen for cross-thread UI use");
        Assert(ReferenceEquals(first, second), "unchanged image was decoded more than once");

        WriteBmp(imagePath, width: 9, height: 8, blue: 20);
        BitmapSource refreshed = cache.LoadCachedAsync(imagePath, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        Assert(!ReferenceEquals(first, refreshed), "changed image reused a stale cache entry");
        Assert(refreshed.PixelWidth == 9 && refreshed.PixelHeight == 8,
            "changed image dimensions were not reloaded");
    }

    private static void BoundingBoxEdgesResizeWithinBounds()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                BoundingBoxEdgesResizeWithinBoundsOnStaThread();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure != null)
            throw new InvalidOperationException("STA bounding-box resize test failed", failure);
    }

    private static void BoundingBoxEdgesResizeWithinBoundsOnStaThread()
    {
        var canvas = new Canvas { Width = 200, Height = 200 };
        var manager = new BoundingBoxManager(canvas);
        var box = new BoundingBox
        {
            ClassName = "dent",
            X = 0.5,
            Y = 0.5,
            Width = 0.4,
            Height = 0.4
        };
        manager.AddFromModel(box, 200, 200);
        Rectangle rect = canvas.Children.OfType<Rectangle>().Single();

        manager.UpdateHoverCursor(rect, new Point(140, 100));
        Assert(canvas.Cursor == Cursors.SizeWE,
            "right edge did not show the horizontal resize cursor");

        manager.Select(rect, new Point(140, 100));
        manager.Drag(new Point(175, 100));
        Assert(manager.EndDrag(200, 200), "right-edge resize was not detected");
        Assert(Math.Abs(Canvas.GetLeft(rect) - 60) < 0.001 &&
               Math.Abs(rect.Width - 115) < 0.001,
            "right-edge resize changed the wrong side of the box");

        manager.Select(rect, new Point(60, 60));
        manager.Drag(new Point(-100, -100));
        Assert(manager.EndDrag(200, 200), "corner resize was not detected");
        Assert(Canvas.GetLeft(rect) >= 0 && Canvas.GetTop(rect) >= 0,
            "corner resize moved the box outside the image");

        double right = Canvas.GetLeft(rect) + rect.Width;
        manager.Select(rect, new Point(Canvas.GetLeft(rect), 100));
        manager.Drag(new Point(right + 500, 100));
        Assert(manager.EndDrag(200, 200), "minimum-size resize was not detected");
        Assert(rect.Width >= 8 && rect.Height >= 8,
            "resize allowed the box to collapse below its minimum size");
        Assert(box.X is >= 0 and <= 1 && box.Y is >= 0 and <= 1 &&
               box.Width is > 0 and <= 1 && box.Height is > 0 and <= 1,
            "resized normalized coordinates are outside the valid range");
    }

    private void AutoReviewAcceptsNormal(string root)
    {
        string image = NewImage(root, "auto_normal.bmp");
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            NormalPrediction(score: 0.018, threshold: 0.02),
            AutoPolicy(),
            "batch/auto_normal");

        Assert(evaluation.Disposition == AutoReviewDisposition.AcceptedNormal,
            "high-confidence normal was not accepted");
        _repository.Save(image, evaluation.StateToPersist!);
        ReviewStateLoadResult loaded = _repository.Load(image);
        Assert(loaded.State.Decision == ImageReviewDecision.ConfirmedNormal,
            "auto normal decision was not persisted");
        Assert(loaded.State.DecisionSource == ReviewDecisionSource.AutoAcceptedAiPrediction,
            "auto normal source was not preserved");
        Assert(loaded.State.AutoReview?.DecisionAutoAccepted == true,
            "auto normal metadata is missing");
        Assert(_selector.Evaluate(loaded).AnomaTraining,
            "auto normal must be eligible for Anoma training");
    }

    private void AutoReviewConfirmsHighConfidenceBoxes(string root)
    {
        string image = NewImage(root, "auto_boxes.bmp");
        PredictionSnapshot prediction = DefectPrediction(
            PredictionBox("dent", 0.94),
            PredictionBox("loose", 0.91));
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            prediction,
            AutoPolicy(),
            "batch/auto_boxes");

        Assert(evaluation.Disposition == AutoReviewDisposition.AcceptedDefectWithBoxes,
            "high-confidence boxes were not auto-confirmed");
        _repository.Save(image, evaluation.StateToPersist!);
        ReviewStateLoadResult loaded = _repository.Load(image);
        Assert(loaded.State.Decision == ImageReviewDecision.ConfirmedDefect,
            "auto defect decision was not persisted");
        Assert(loaded.State.BoxReview == BoxReviewDecision.Confirmed &&
               loaded.State.BoxReviewSource == BoxReviewSource.AutoAcceptedAiPrediction,
            "auto box source/status was not preserved");
        Assert(_selector.Evaluate(loaded).YoloPositive,
            "auto-confirmed boxes must be eligible for YOLO training");

        AutoReviewEvaluation lowBox = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            DefectPrediction(PredictionBox("dent", 0.84)),
            AutoPolicy(),
            "batch/low_box");
        Assert(lowBox.StateToPersist?.Decision == ImageReviewDecision.ConfirmedDefect,
            "low-confidence box must not block the Anoma defect decision");
        ReviewState lowBoxState = lowBox.StateToPersist ??
                                  throw new InvalidOperationException("low-confidence state is missing");
        Assert(lowBoxState.BoxReview == BoxReviewDecision.Predicted &&
               lowBoxState.BoxReviewSource == BoxReviewSource.AiPrediction,
            "low-confidence box was incorrectly confirmed");
        Assert(lowBoxState.Boxes.Count == 0,
            "low-confidence AI boxes leaked into confirmed review boxes");
    }

    private void AutoReviewBoxlessDefectIsAnomaOnly(string root)
    {
        string image = NewImage(root, "auto_boxless.bmp");
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            DefectPrediction(),
            AutoPolicy(),
            "batch/auto_boxless");
        _repository.Save(image, evaluation.StateToPersist!);

        ReviewStateLoadResult loaded = _repository.Load(image);
        TrainingEligibility eligibility = _selector.Evaluate(loaded);
        Assert(loaded.State.Decision == ImageReviewDecision.ConfirmedDefect,
            "boxless auto-reviewed image must remain defect");
        Assert(eligibility.AnomaEvaluation, "boxless defect must be eligible for Anoma evaluation");
        Assert(!eligibility.YoloPositive && eligibility.YoloExcludedDefectWithoutBoxes,
            "boxless defect must be excluded from YOLO training");
    }

    private void YoloExclusionsDistinguishLowConfidenceAndMissingBoxes()
    {
        PredictionSnapshot lowConfidencePrediction = DefectPrediction(
            PredictionBox("loose", 0.2833411));
        AutoReviewEvaluation lowConfidenceEvaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            lowConfidencePrediction,
            AutoPolicy(),
            "batch/low_confidence_display");
        var lowConfidenceLoad = new ReviewStateLoadResult
        {
            HasReviewFile = true,
            State = lowConfidenceEvaluation.StateToPersist ??
                    throw new InvalidOperationException("low-confidence state is missing")
        };

        TrainingEligibility lowConfidence = _selector.Evaluate(
            lowConfidenceLoad,
            lowConfidencePrediction);
        Assert(lowConfidence.YoloLowConfidencePredictionReviewRequired,
            "low-confidence AI boxes were not classified as requiring box review");
        Assert(!lowConfidence.YoloExcludedDefectWithoutBoxes,
            "low-confidence AI boxes were incorrectly classified as no-box defects");
        Assert(lowConfidence.ExclusionReason.Contains("0.283", StringComparison.Ordinal) &&
               lowConfidence.ExclusionReason.Contains("0.850", StringComparison.Ordinal),
            "low-confidence reason does not show the prediction and auto-confirm thresholds");

        PredictionSnapshot noBoxPrediction = DefectPrediction();
        AutoReviewEvaluation noBoxEvaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            noBoxPrediction,
            AutoPolicy(),
            "batch/no_box_display");
        var noBoxLoad = new ReviewStateLoadResult
        {
            HasReviewFile = true,
            State = noBoxEvaluation.StateToPersist ??
                    throw new InvalidOperationException("boxless state is missing")
        };

        TrainingEligibility noBox = _selector.Evaluate(noBoxLoad, noBoxPrediction);
        Assert(noBox.YoloExcludedDefectWithoutBoxes,
            "zero-detection defect was not classified as a no-box defect");
        Assert(!noBox.YoloLowConfidencePredictionReviewRequired,
            "zero-detection defect was incorrectly classified as low-confidence boxes");
        Assert(noBox.ExclusionReason.Contains("미검출", StringComparison.Ordinal),
            "zero-detection reason does not identify the YOLO miss");

        ReviewState confirmedZero = _workflow.ConfirmBoxes(
            _workflow.ConfirmDefect(new ReviewState()));
        TrainingEligibility confirmedZeroEligibility = _selector.Evaluate(
            new ReviewStateLoadResult { HasReviewFile = true, State = confirmedZero },
            noBoxPrediction);
        Assert(confirmedZeroEligibility.YoloExcludedDefectWithoutBoxes &&
               confirmedZeroEligibility.ExclusionReason.Contains("0개로 검수 완료", StringComparison.Ordinal),
            "explicitly confirmed zero boxes were not distinguished from an unreviewed miss");
    }

    private void AutoReviewLeavesGrayZoneUntouched()
    {
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            DefectPredictionWithScore(score: 0.03018, threshold: 0.02),
            AutoPolicy(),
            "batch/gray");
        Assert(!evaluation.ShouldPersist && evaluation.Disposition == AutoReviewDisposition.NotApplied,
            "gray-zone prediction changed review state");
    }

    private void AutoReviewNeverHoldsAuditSamples()
    {
        // A non-zero value can still arrive from an older inference context.
        // It must be ignored now that sampling has been removed.
        AutoReviewPolicy policy = AutoPolicy(auditSampleRate: 1.0);
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            NormalPrediction(score: 0.001, threshold: 0.02),
            policy,
            "batch/legacy_audit_rate");
        Assert(evaluation.Disposition == AutoReviewDisposition.AcceptedNormal,
            "legacy audit rate still held an automatic decision");
        Assert(evaluation.StateToPersist?.Decision == ImageReviewDecision.ConfirmedNormal,
            "high-confidence normal was not auto-accepted");
        Assert(evaluation.StateToPersist?.AutoReview?.HeldForAudit == false &&
               evaluation.StateToPersist.AutoReview.AuditSampleRate == 0,
            "removed sampling state was written to review metadata");
    }

    private void AutoReviewProtectsExistingState()
    {
        var existing = new ReviewStateLoadResult
        {
            HasReviewFile = true,
            State = _workflow.ConfirmNormal(new ReviewState(), useAsYoloBackground: true)
        };
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            existing,
            DefectPrediction(),
            AutoPolicy(),
            "batch/protected");
        Assert(!evaluation.ShouldPersist,
            "auto review attempted to overwrite existing user state");
        Assert(existing.State.Decision == ImageReviewDecision.ConfirmedNormal &&
               existing.State.DecisionSource == ReviewDecisionSource.Manual,
            "existing user state was changed in memory");
    }

    private void PredictionOnlyBoxesStayOutOfEditableLayer()
    {
        PredictionSnapshot prediction = DefectPrediction(PredictionBox("loose", 0.70));
        var existingPredictionState = new ReviewState
        {
            Decision = ImageReviewDecision.ConfirmedDefect,
            BoxReview = BoxReviewDecision.Predicted,
            BoxReviewSource = BoxReviewSource.AiPrediction,
            Boxes = prediction.YoloBoxes.Select(box => box.Clone()).ToList()
        };

        Assert(ReviewBoxLayerPolicy.GetEditableBoxes(existingPredictionState).Count == 0,
            "prediction-only boxes were exposed as editable boxes");
        var predictionLoad = new ReviewStateLoadResult
        {
            HasReviewFile = true,
            State = existingPredictionState
        };
        ImageReviewProjection predictionProjection = new ReviewProjectionService().Create(
            predictionLoad,
            prediction,
            _selector.Evaluate(predictionLoad));
        Assert(predictionProjection.BoxStatusText.Contains("1개", StringComparison.Ordinal),
            "prediction-only box count was lost from the list projection");

        ReviewState accepted = _workflow.AcceptPredictionBoxes(existingPredictionState, prediction);
        Assert(accepted.BoxReview == BoxReviewDecision.Edited &&
               accepted.BoxReviewSource == BoxReviewSource.AcceptedAiPrediction,
            "explicit AI box acceptance was not recorded separately");
        Assert(ReviewBoxLayerPolicy.GetEditableBoxes(accepted).Count == 1,
            "explicitly accepted boxes were not exposed to the editor");

        ReviewState acceptedAndConfirmed = _workflow.AcceptAndConfirmPredictionBoxes(
            existingPredictionState,
            prediction);
        Assert(acceptedAndConfirmed.BoxReview == BoxReviewDecision.Confirmed &&
               acceptedAndConfirmed.BoxReviewSource == BoxReviewSource.AcceptedAiPrediction &&
               acceptedAndConfirmed.BoxesConfirmedAtUtc.HasValue,
            "one-click YOLO acceptance did not confirm the prediction boxes");
        Assert(!ReviewBoxLayerPolicy.CanSaveEditedBoxes(acceptedAndConfirmed),
            "box save remained enabled after YOLO acceptance confirmed the boxes");
        Assert(ReviewBoxLayerPolicy.CanAcceptPredictionBoxes(
                hasUsablePrediction: true,
                yoloExecuted: true,
                predictionIsDefect: true,
                isConfirmedNormal: false,
                isConfirmedDefect: false,
                isExcluded: false,
                isBoxConfirmed: false,
                isBoxEdited: false),
            "YOLO acceptance was not available for an unaccepted defect prediction");
        Assert(!ReviewBoxLayerPolicy.CanAcceptPredictionBoxes(
                true, true, true, false, true, false, true, false),
            "YOLO acceptance remained available after boxes were confirmed");
        Assert(!ReviewBoxLayerPolicy.CanAcceptPredictionBoxes(
                true, true, true, false, true, false, false, true),
            "YOLO acceptance remained available after manual box editing started");
        Assert(!ReviewBoxLayerPolicy.CanAcceptPredictionBoxes(
                true, true, true, true, false, false, false, false),
            "YOLO acceptance remained available after the image was confirmed normal");
        Assert(!ReviewBoxLayerPolicy.CanAcceptPredictionBoxes(
                true, false, false, false, true, false, false, false),
            "YOLO acceptance was available even though YOLO did not execute");

        ReviewState changedAfterConfirmation = _workflow.ReplaceBoxesAfterEdit(
            acceptedAndConfirmed,
            acceptedAndConfirmed.Boxes);
        Assert(ReviewBoxLayerPolicy.CanSaveEditedBoxes(changedAfterConfirmation),
            "box save did not become available after a confirmed box was edited");

        AutoReviewEvaluation highConfidence = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            DefectPrediction(PredictionBox("dent", 0.91)),
            AutoPolicy(),
            "batch/confirmed_layer");
        Assert(ReviewBoxLayerPolicy.GetEditableBoxes(highConfidence.StateToPersist!).Count == 1,
            "auto-confirmed high-confidence boxes were hidden from the editor");
    }

    private static void WriteBmp(string path, int width, int height, byte blue)
    {
        const int bytesPerPixel = 3;
        int stride = width * bytesPerPixel;
        var pixels = new byte[stride * height];
        for (int index = 0; index < pixels.Length; index += bytesPerPixel)
        {
            pixels[index] = blue;
            pixels[index + 1] = 30;
            pixels[index + 2] = 40;
        }

        BitmapSource source = BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgr24,
            palette: null,
            pixels,
            stride);
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void FinalTrainingCommandsAreExplicit()
    {
        var settings = new AppSettings();
        string workspace = TrainingCommandBuilder.BuildYoloWorkspaceArgs(settings, "raw data", "yolo workspace");
        string yolo = TrainingCommandBuilder.BuildYoloArgs(settings, "workspace", "out", false, null);
        string anoma = TrainingCommandBuilder.BuildAnomaArgs(settings, "raw", "out", "run_001");

        Assert(workspace.Contains("--augment-class \"all\""), "defect augmentation is missing");
        Assert(!workspace.Contains("--max-background"), "background images must not be capped");
        Assert(yolo.Contains("--model \"yolo26n.pt\""), "fresh YOLO26n model is missing");
        Assert(yolo.Contains("--epochs 100") && yolo.Contains("--imgsz 1280"), "final YOLO settings are missing");
        Assert(anoma.Contains("--model \"dinomaly\""), "Dinomaly model is missing");
        Assert(anoma.Contains("--dataset-name \"run_001\""), "run-specific anomaly dataset is missing");
        Assert(anoma.Contains("vit_large_patch14_reg4_dinov2"), "ViT-Large encoder is missing");
        Assert(anoma.Contains("--image-size 448") && anoma.Contains("--target-recall 0.9"),
            "final Dinomaly settings are missing");
    }

    private static void FineTuneCommandUsesWarmStartPolicy()
    {
        var settings = new AppSettings();
        string args = TrainingCommandBuilder.BuildYoloArgs(
            settings,
            "workspace",
            "out",
            true,
            @"C:\models\parent_best.pt");

        Assert(args.Contains("parent_best.pt"), "parent checkpoint is missing");
        Assert(args.Contains("--epochs 40"), "fine-tune epoch policy is missing");
        Assert(args.Contains("--lr0 0.001"), "fine-tune learning rate is missing");
    }

    private static void PythonEnvironmentValidationFollowsPipeline(string root)
    {
        string yoloPython = Path.Combine(root, "fake-yolo-python.exe");
        File.WriteAllBytes(yoloPython, new byte[] { 1 });
        var settings = new AppSettings
        {
            YoloPythonExe = yoloPython,
            AnomaPythonExe = ""
        };

        AppSettingsLoader.ValidateRequiredPythonEnvironments(
            settings,
            requireYoloPython: true,
            requireAnomaPython: false);

        bool rejectedMissingAnoma = false;
        try
        {
            AppSettingsLoader.ValidateRequiredPythonEnvironments(
                settings,
                requireYoloPython: false,
                requireAnomaPython: true);
        }
        catch (InvalidOperationException)
        {
            rejectedMissingAnoma = true;
        }
        Assert(rejectedMissingAnoma, "Anoma-only validation accepted a missing Anoma environment");
    }

    private static void ModelRegistryTracksLifecycle(string root)
    {
        string registryRoot = Path.Combine(root, "model_registry");
        var registry = new ModelRegistryService(registryRoot);

        ModelRegistryEntry first = RegisterFakeModel(registry, root, "run_first", 0.30, "");
        Assert(first.YoloBestPtPath.EndsWith(Path.Combine("training", "yolo_best.pt")),
            "package checkpoint was not preferred");
        File.Delete(Path.Combine(root, "run_first", "yolo_out", "best.pt"));
        Assert(registry.Load().Single(model => model.Id == first.Id).HasYoloCheckpoint,
            "package checkpoint was not retained after yolo_out checkpoint removal");

        registry.SetReference(first.Id);
        Assert(registry.Load().Single(model => model.Id == first.Id).Status == ModelLifecycleStatus.Reference,
            "first model was not selected as reference");
        Assert(File.Exists(registry.ReferencePointerPath), "reference pointer is missing");

        ModelRegistryEntry second = RegisterFakeModel(registry, root, "run_second", 0.35, first.Id);
        registry.SetReference(second.Id);
        IReadOnlyList<ModelRegistryEntry> models = registry.Load();
        Assert(models.Single(model => model.Id == second.Id).Status == ModelLifecycleStatus.Reference,
            "second model was not selected as reference");
        Assert(models.Single(model => model.Id == first.Id).Status == ModelLifecycleStatus.Candidate,
            "previous reference model did not return to candidate status");
        Assert(models.Single(model => model.Id == second.Id).ParentModelId == first.Id,
            "fine-tune lineage was not stored");
    }

    private static void InferencePackageDeploymentIsSafe(string root)
    {
        string source = Path.Combine(root, "deployment_source");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(source, "models"));
        File.WriteAllBytes(Path.Combine(source, "models", "mask.onnx"), new byte[] { 9, 8, 7 });
        File.WriteAllBytes(Path.Combine(source, "models", "anoma.onnx"), new byte[] { 4, 5, 6 });
        File.WriteAllBytes(Path.Combine(source, "models", "yolo.onnx"), new byte[] { 1, 2, 3 });
        File.WriteAllText(
            Path.Combine(source, "config", "pipeline.json"),
            JsonSerializer.Serialize(new
            {
                schema_version = 3,
                pipeline = new
                {
                    mode = "anoma_then_yolo",
                    skip_yolo_when_stage1_normal = true,
                    required_models = new[] { "mask", "anoma", "yolo" }
                },
                mask = new
                {
                    model = "models/mask.onnx",
                    input_size = 512,
                    resize_mode = "letterbox",
                    image_mean = new[] { 0.485, 0.456, 0.406 },
                    image_std = new[] { 0.229, 0.224, 0.225 }
                },
                anoma = new { model = "models/anoma.onnx", input_size = 448 },
                yolo = new { model = "models/yolo.onnx", imgsz = 1280 }
            }));

        string target = Path.Combine(root, "inspection_app", "InferencePackage");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "old-package.txt"), "old");

        var service = new InferencePackageDeploymentService();
        InferencePackageDeploymentResult result = service.Deploy(source, target);
        Assert(File.Exists(Path.Combine(target, "models", "yolo.onnx")),
            "new package was not deployed");
        Assert(!File.Exists(Path.Combine(target, "old-package.txt")),
            "old package content leaked into the new package");
        Assert(Directory.Exists(result.BackupDirectory)
               && File.Exists(Path.Combine(result.BackupDirectory, "old-package.txt")),
            "previous package was not backed up");
    }

    private static void MissingMaskModelIsRejected(string root)
    {
        string source = Path.Combine(root, "deployment_missing_mask");
        Directory.CreateDirectory(Path.Combine(source, "config"));
        Directory.CreateDirectory(Path.Combine(source, "models"));
        File.WriteAllText(
            Path.Combine(source, "config", "pipeline.json"),
            JsonSerializer.Serialize(new
            {
                schema_version = 3,
                pipeline = new
                {
                    mode = "anoma_then_yolo",
                    skip_yolo_when_stage1_normal = true,
                    required_models = new[] { "mask", "anoma", "yolo" }
                },
                mask = new
                {
                    model = "models/mask.onnx",
                    input_size = 512,
                    resize_mode = "letterbox",
                    image_mean = new[] { 0.485, 0.456, 0.406 },
                    image_std = new[] { 0.229, 0.224, 0.225 }
                },
                anoma = new { model = "models/anoma.onnx", input_size = 448 },
                yolo = new { model = "models/yolo.onnx", imgsz = 1280 }
            }));

        bool rejected = false;
        try
        {
            new InferencePackageDeploymentService().ValidatePackageOrThrow(source);
        }
        catch (FileNotFoundException)
        {
            rejected = true;
        }
        Assert(rejected, "package validation accepted a missing mask.onnx");
    }

    private static void AutomationImportsCompletedBatchExactlyOnce(string root)
    {
        string testRoot = Path.Combine(root, "automation_import_once");
        string exchange = Path.Combine(testRoot, "exchange");
        string library = Path.Combine(testRoot, "library");
        string source = CreateAutomationBatch(AutomationPaths.Outbox(exchange), "batch-once", "image-a");
        string unfinished = CreateAutomationBatch(AutomationPaths.Outbox(exchange), "batch-unfinished", "image-b", done: false);

        var reconciler = new BatchInboxReconciler(exchange, library);
        BatchReconcileResult first = reconciler.Reconcile();
        BatchReconcileResult second = reconciler.Reconcile();

        Assert(first.ImportedCount == 1 && Directory.Exists(Path.Combine(library, "batch-once")),
            "completed batch was not imported");
        Assert(second.ImportedCount == 0 && second.DuplicateCount == 1,
            "duplicate watcher reconciliation imported a second copy");
        Assert(Directory.Exists(source) && Directory.Exists(unfinished),
            "outbox source was modified or deleted");
        Assert(!Directory.Exists(Path.Combine(library, "batch-unfinished")),
            "batch without DONE.flag was imported");
        Assert(Directory.GetDirectories(library)
                .Count(path => Path.GetFileName(path).StartsWith("batch-once", StringComparison.OrdinalIgnoreCase)) == 1,
            "idempotent import created a suffixed batch");
        Assert(Directory.GetFiles(AutomationPaths.Receipts(exchange), "*.json").Length == 1 &&
               !Directory.GetFiles(AutomationPaths.Receipts(exchange), "*.tmp-*", SearchOption.TopDirectoryOnly).Any(),
            "receipt was not atomically recorded");
    }

    private static void AutomationRejectsBatchIdConflict(string root)
    {
        string testRoot = Path.Combine(root, "automation_import_conflict");
        string exchange = Path.Combine(testRoot, "exchange");
        string outbox = AutomationPaths.Outbox(exchange);
        CreateAutomationBatch(outbox, "source-a", "image-a", manifestBatchId: "same-id");
        CreateAutomationBatch(outbox, "source-b", "image-b", manifestBatchId: "same-id");

        BatchReconcileResult result = new BatchInboxReconciler(exchange, Path.Combine(testRoot, "library")).Reconcile();
        Assert(result.ImportedCount == 1 && result.ConflictCount == 1,
            "same batch_id with a different manifest was not reported as a conflict");
        Assert(!Directory.Exists(Path.Combine(testRoot, "library", "same-id_2")),
            "conflicting batch was silently renamed");
    }

    private static void AutomationCleansFailedBatchStaging(string root)
    {
        string testRoot = Path.Combine(root, "automation_import_copy_failure");
        string exchange = Path.Combine(testRoot, "exchange");
        string library = Path.Combine(testRoot, "library");
        CreateAutomationBatch(AutomationPaths.Outbox(exchange), "batch-fail", "image-fail");
        int copies = 0;
        var reconciler = new BatchInboxReconciler(exchange, library, (source, destination) =>
        {
            if (++copies > 1) throw new IOException("simulated copy failure");
            File.Copy(source, destination);
        });

        BatchReconcileResult result = reconciler.Reconcile();
        string staging = Path.Combine(library, "_importing");
        Assert(result.FailedCount == 1 && !Directory.Exists(Path.Combine(library, "batch-fail")),
            "copy failure exposed a partial final batch");
        Assert(!Directory.Exists(staging) || !Directory.EnumerateFileSystemEntries(staging).Any(),
            "failed staging directory was not cleaned");
        Assert(new BatchLibraryService().Scan(library, includeHidden: true).Batches.Count == 0,
            "staging batch leaked into the library scan");
    }

    private static void FullModelReleaseIsImmutable(string root)
    {
        string testRoot = Path.Combine(root, "automation_release");
        string exchange = Path.Combine(testRoot, "exchange");
        string package = CreateFullInferencePackage(Path.Combine(testRoot, "run-a", "inference_package"));
        var entry = new ModelRegistryEntry
        {
            Id = "model-a",
            PipelineMode = InferencePipelineConfigBuilder.AnomaThenYolo,
            RunDirectory = Path.Combine(testRoot, "run-a"),
            InferencePackageDirectory = package
        };
        var publisher = new ModelReleasePublisher(exchange);
        ModelPublishResult first = publisher.Publish(entry);
        ModelPublishResult second = publisher.Publish(entry);

        string releaseManifest = Path.Combine(first.ReleaseDirectory, "release.json");
        Assert(!first.AlreadyPublished && second.AlreadyPublished,
            "identical model release was not idempotent");
        Assert(File.Exists(releaseManifest) &&
               !File.Exists(Path.Combine(first.PackageDirectory, "release.json")),
            "release.json must be outside InferencePackage");
        Assert(first.PackageHash == AutomationHash.PackageSha256(first.PackageDirectory),
            "published package hash was not deterministic");

        File.WriteAllBytes(Path.Combine(package, "models", "yolo.onnx"), new byte[] { 9, 9, 9, 9 });
        bool conflict = false;
        try { publisher.Publish(entry); } catch (IOException) { conflict = true; }
        Assert(conflict, "same model-id with different package content was overwritten");

        var partial = new ModelRegistryEntry
        {
            Id = "model-partial",
            PipelineMode = "yolo_only",
            RunDirectory = testRoot,
            InferencePackageDirectory = package
        };
        bool partialRejected = false;
        try { publisher.Publish(partial); } catch (InvalidOperationException) { partialRejected = true; }
        Assert(partialRejected, "partial pipeline model was auto-published");
    }

    private static void ActivationRequestsAreSinglePending(string root)
    {
        string testRoot = Path.Combine(root, "automation_request");
        string exchange = Path.Combine(testRoot, "exchange");
        string package = CreateFullInferencePackage(Path.Combine(testRoot, "run", "inference_package"));
        var entry = new ModelRegistryEntry
        {
            Id = "model-request",
            PipelineMode = InferencePipelineConfigBuilder.AnomaThenYolo,
            RunDirectory = Path.Combine(testRoot, "run"),
            InferencePackageDirectory = package
        };
        var publisher = new ModelReleasePublisher(exchange);
        publisher.Publish(entry);
        var requests = new ActivationRequestService(exchange, publisher);
        ActivationRequest first = requests.Create(entry.Id);
        bool blocked = false;
        try { requests.Create(entry.Id); } catch (PendingActivationRequestException) { blocked = true; }
        Assert(blocked, "pending activation request was overwritten");
        Assert(requests.CancelPending(out _) && !File.Exists(AutomationPaths.ActivationRequest(exchange)),
            "pending activation request was not explicitly cancelled");
        ActivationRequest second = requests.Create(entry.Id);
        Assert(first.RequestId != second.RequestId, "new activation request did not receive a new request_id");
    }

    private static void ActivationResultAdvancesReference(string root)
    {
        string testRoot = Path.Combine(root, "automation_reference_sync");
        string exchange = Path.Combine(testRoot, "exchange");
        string registryRoot = Path.Combine(testRoot, "registry");
        string package = CreateFullInferencePackage(Path.Combine(testRoot, "run", "inference_package"));
        var registry = new ModelRegistryService(registryRoot);
        ModelRegistryEntry entry = registry.Register(new ModelRegistrationContext
        {
            RunDirectory = Path.Combine(testRoot, "run"),
            InferencePackageDirectory = package,
            PipelineMode = InferencePipelineConfigBuilder.AnomaThenYolo,
            SourceBatches = new[] { "batch-a" }
        });
        var publisher = new ModelReleasePublisher(exchange);
        publisher.Publish(entry);
        var requests = new ActivationRequestService(exchange, publisher);
        ActivationRequest request = requests.Create(entry.Id);

        new ActivationResultSynchronizer(requests, registry).Reconcile();
        Assert(registry.Find(entry.Id)?.Status == ModelLifecycleStatus.Candidate,
            "request creation changed the reference before inspection applied it");

        AtomicJsonFile.Write(AutomationPaths.ActivationResult(exchange), new ActivationResult
        {
            RequestId = request.RequestId,
            ModelId = request.ModelId,
            PackageHash = request.PackageHash,
            Status = "applied",
            Message = "ok",
            AppliedAtUtc = DateTime.UtcNow
        });
        new ActivationResultSynchronizer(requests, registry).Reconcile();
        Assert(registry.Find(entry.Id)?.Status == ModelLifecycleStatus.Reference,
            "matching applied result did not advance the model reference");
    }

    private static void ReleasePathTraversalIsRejected(string root)
    {
        bool absoluteRejected = false;
        bool traversalRejected = false;
        try { AutomationPaths.ResolveReleasePackagePath(root, Path.Combine(root, "outside")); }
        catch (InvalidDataException) { absoluteRejected = true; }
        try { AutomationPaths.ResolveReleasePackagePath(root, "../outside/InferencePackage"); }
        catch (InvalidDataException) { traversalRejected = true; }
        Assert(absoluteRejected && traversalRejected, "unsafe activation package path was accepted");
    }

    private static string CreateAutomationBatch(
        string outbox,
        string folderName,
        string imageId,
        bool done = true,
        string? manifestBatchId = null)
    {
        string batch = Path.Combine(outbox, folderName);
        Directory.CreateDirectory(Path.Combine(batch, "images"));
        Directory.CreateDirectory(Path.Combine(batch, "meta"));
        File.WriteAllBytes(Path.Combine(batch, "images", imageId + ".bmp"), new byte[] { 0x42, 0x4D });
        File.WriteAllText(Path.Combine(batch, "meta", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema_version = 2,
            batch_type = "no_infer",
            batch_id = manifestBatchId ?? folderName,
            created_at = "2026-07-25T00:00:00",
            items = new[] { new { id = imageId, processed_image = "images/" + imageId + ".bmp" } }
        }));
        if (done) File.WriteAllText(Path.Combine(batch, "meta", "DONE.flag"), "READY_FOR_TRAINING_UI");
        return batch;
    }

    private static string CreateFullInferencePackage(string package)
    {
        Directory.CreateDirectory(Path.Combine(package, "config"));
        Directory.CreateDirectory(Path.Combine(package, "models"));
        File.WriteAllBytes(Path.Combine(package, "models", "mask.onnx"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(package, "models", "anoma.onnx"), new byte[] { 4, 5, 6 });
        File.WriteAllBytes(Path.Combine(package, "models", "yolo.onnx"), new byte[] { 7, 8, 9 });
        File.WriteAllText(Path.Combine(package, "config", "pipeline.json"), JsonSerializer.Serialize(new
        {
            schema_version = 3,
            pipeline = new
            {
                mode = "anoma_then_yolo",
                skip_yolo_when_stage1_normal = true,
                required_models = new[] { "mask", "anoma", "yolo" }
            },
            mask = new
            {
                model = "models/mask.onnx",
                input_size = 512,
                resize_mode = "letterbox",
                image_mean = new[] { 0.485, 0.456, 0.406 },
                image_std = new[] { 0.229, 0.224, 0.225 }
            },
            anoma = new { model = "models/anoma.onnx", input_size = 448 },
            yolo = new { model = "models/yolo.onnx", imgsz = 1280 }
        }));
        return package;
    }

    private static ModelRegistryEntry RegisterFakeModel(
        ModelRegistryService registry,
        string root,
        string runName,
        double map50,
        string parentModelId)
    {
        string run = Path.Combine(root, runName);
        string yoloOut = Path.Combine(run, "yolo_out");
        string package = Path.Combine(run, "inference_package");
        Directory.CreateDirectory(yoloOut);
        Directory.CreateDirectory(Path.Combine(package, "training"));
        Directory.CreateDirectory(Path.Combine(package, "models"));
        File.WriteAllBytes(Path.Combine(yoloOut, "best.pt"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(yoloOut, "yolo.onnx"), new byte[] { 4, 5, 6 });
        File.WriteAllBytes(Path.Combine(package, "training", "yolo_best.pt"), new byte[] { 7, 8, 9 });
        File.WriteAllBytes(Path.Combine(package, "models", "yolo.onnx"), new byte[] { 4, 5, 6 });
        File.WriteAllText(
            Path.Combine(yoloOut, "train_summary.json"),
            JsonSerializer.Serialize(new
            {
                metrics = new
                {
                    precision = 0.6,
                    recall = 0.4,
                    map50,
                    map = 0.2
                }
            }));

        return registry.Register(new ModelRegistrationContext
        {
            RunDirectory = run,
            InferencePackageDirectory = package,
            PipelineMode = "yolo_only",
            TrainingMode = string.IsNullOrWhiteSpace(parentModelId) ? "fresh" : "fine_tune",
            ParentModelId = parentModelId,
            SourceBatches = new[] { "batch-a" },
            TotalImages = 10,
            NormalImages = 6,
            YoloModel = "yolo26n.pt",
            YoloOutDirectory = yoloOut
        });
    }

    private void BatchManifestReaderIsReadOnly(string root)
    {
        string inbox = Path.Combine(root, "read_only_inbox");
        string batch = Path.Combine(inbox, "batch-a");
        string images = Path.Combine(batch, "images");
        string meta = Path.Combine(batch, "meta");
        Directory.CreateDirectory(images);
        Directory.CreateDirectory(meta);
        string image = NewImage(images, "read_only.bmp");
        File.WriteAllText(Path.Combine(meta, "DONE.flag"), "done");
        File.WriteAllText(
            Path.Combine(meta, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                schema_version = 2,
                batch_type = "no_infer",
                batch_id = "batch-a",
                created_at = "2026-01-01T00:00:00",
                items = new[]
                {
                    new { id = "read_only", processed_image = "images/read_only.bmp" }
                }
            }));

        string reviewPath = ReviewRepository.GetReviewPath(image);
        var batchLoader = new BatchImportService(new BatchLibraryService());
        BatchImportLoadResult batchLoad = batchLoader.LoadLibrary(inbox);
        Assert(batchLoad.Images.Count == 1, "batch image was not loaded");
        ReviewStateLoadResult load = _repository.Load(batchLoad.Images[0].ProcessedPath);
        Assert(load.State.Decision == ImageReviewDecision.Unreviewed, "new state must be unreviewed");
        Assert(!File.Exists(reviewPath), "loading must not create review.json");
        Assert(!File.Exists(ImageStateService.GetStatePath(image)), "loading must not create legacy state.json");

        TrainingDatasetSelection selection = _selector.Select(new[] { Input(image, "batch-a") });
        Assert(selection.AnomaInputs.Count == 0 && selection.YoloInputs.Count == 0,
            "unreviewed selection must be empty");
        Assert(!File.Exists(reviewPath), "selection must not create review.json");
    }

    private void ConfirmedNormalSurvivesReload(string root)
    {
        string image = NewImage(root, "normal.bmp");
        ReviewState state = _workflow.ConfirmNormal(new ReviewState(), useAsYoloBackground: true);
        _repository.Save(image, state);
        ReviewState reloaded = _repository.Load(image).State;
        Assert(reloaded.Decision == ImageReviewDecision.ConfirmedNormal, "normal decision lost");
        Assert(reloaded.UseAsYoloBackground, "background selection lost");
        Assert(reloaded.DecisionSource == ReviewDecisionSource.Manual, "manual source lost");
    }

    private void ConfirmedDefectSurvivesReload(string root)
    {
        string image = NewImage(root, "defect.bmp");
        _repository.Save(image, _workflow.ConfirmDefect(new ReviewState()));
        ReviewState reloaded = _repository.Load(image).State;
        Assert(reloaded.Decision == ImageReviewDecision.ConfirmedDefect, "defect decision lost");
        Assert(reloaded.DecisionSource == ReviewDecisionSource.Manual, "manual source lost");
    }

    private void BoxesSurviveReload(string root)
    {
        string image = NewImage(root, "boxes.bmp");
        var prediction = DefectPrediction(new ReviewBox
        {
            ClassName = "dent", X = 0.5, Y = 0.5, Width = 0.2, Height = 0.2,
            Source = "ai_prediction", PredictionConfidence = 0.91
        });
        ReviewState state = _workflow.AcceptPredictionBoxes(new ReviewState(), prediction);
        state = _workflow.ConfirmDefect(state);
        state = _workflow.ReplaceBoxesAfterEdit(state, new[]
        {
            new ReviewBox { ClassName = "loose", X = 0.4, Y = 0.6, Width = 0.3, Height = 0.1 }
        });
        state = _workflow.ConfirmBoxes(state);
        _repository.Save(image, state);

        ReviewState reloaded = _repository.Load(image).State;
        Assert(reloaded.BoxReview == BoxReviewDecision.Confirmed, "box confirmation lost");
        Assert(reloaded.Boxes.Count == 1 && reloaded.Boxes[0].ClassName == "loose", "edited box lost");
    }

    private void ConfirmedZeroBoxesSurviveReload(string root)
    {
        string image = NewImage(root, "zero_boxes.bmp");
        ReviewState state = _workflow.ConfirmDefect(new ReviewState());
        state = _workflow.ReplaceBoxesAfterEdit(state, Array.Empty<ReviewBox>());
        state = _workflow.ConfirmBoxes(state);
        _repository.Save(image, state);

        ReviewState reloaded = _repository.Load(image).State;
        Assert(reloaded.Decision == ImageReviewDecision.ConfirmedDefect, "defect decision lost");
        Assert(reloaded.BoxReview == BoxReviewDecision.Confirmed, "zero-box confirmation lost");
        Assert(reloaded.Boxes.Count == 0, "AI boxes must not be restored into user boxes");
    }

    private void DefectWithoutBoxesIsAnomaOnly(string root)
    {
        string image = NewImage(root, "anoma_only_defect.bmp");
        ReviewState state = _workflow.ConfirmBoxes(_workflow.ConfirmDefect(new ReviewState()));
        _repository.Save(image, state);

        TrainingDatasetSelection selection = _selector.Select(new[] { Input(image, "batch-a") });
        Assert(selection.AnomaInputs.Count == 1, "defect must be included in Anoma evaluation");
        Assert(selection.YoloInputs.Count == 0, "zero-box defect must not be YOLO background");
        Assert(selection.ExcludedDefectWithoutBoxes == 1, "zero-box exclusion count mismatch");
    }

    private void UnreviewedIsExcluded(string root)
    {
        string image = NewImage(root, "unreviewed.bmp");
        TrainingEligibility eligibility = _selector.Evaluate(_repository.Load(image));
        Assert(!eligibility.AnomaTraining && !eligibility.AnomaEvaluation &&
               !eligibility.YoloBackground && !eligibility.YoloPositive,
            "unreviewed image became trainable");
    }

    private void TrainingUseTogglePreservesReview(string root)
    {
        string image = NewImage(root, "training_toggle.bmp");
        ReviewState confirmed = _workflow.ConfirmDefect(new ReviewState());
        confirmed = _workflow.ReplaceBoxesAfterEdit(confirmed, new[]
        {
            new ReviewBox
            {
                ClassName = "dent",
                X = 0.5,
                Y = 0.5,
                Width = 0.2,
                Height = 0.2
            }
        });
        confirmed = _workflow.ConfirmBoxes(confirmed);

        ReviewState disabled = _workflow.SetTrainingEnabled(confirmed, enabled: false);
        Assert(!disabled.IncludeInTraining &&
               disabled.Decision == ImageReviewDecision.ConfirmedDefect &&
               disabled.BoxReview == BoxReviewDecision.Confirmed &&
               disabled.Boxes.Count == 1,
            "turning training OFF changed the confirmed review data");
        _repository.Save(image, disabled);

        ReviewStateLoadResult disabledLoad = _repository.Load(image);
        TrainingEligibility disabledEligibility = _selector.Evaluate(disabledLoad);
        ImageReviewProjection disabledProjection = new ReviewProjectionService().Create(
            disabledLoad,
            new PredictionSnapshot(),
            disabledEligibility);
        Assert(disabledLoad.State.SchemaVersion == ReviewState.CurrentSchemaVersion,
            "training toggle state did not persist with the current schema");
        Assert(!disabledEligibility.AnyTrainingUse &&
               disabledEligibility.ExclusionReason.Contains("OFF", StringComparison.Ordinal),
            "training OFF image remained eligible for training");
        Assert(disabledProjection.IsExcluded && disabledProjection.IsConfirmedDefect,
            "training OFF projection lost the preserved defect decision");

        ReviewState enabled = _workflow.SetTrainingEnabled(disabledLoad.State, enabled: true);
        var enabledLoad = new ReviewStateLoadResult { HasReviewFile = true, State = enabled };
        TrainingEligibility enabledEligibility = _selector.Evaluate(enabledLoad);
        Assert(enabled.IncludeInTraining &&
               enabled.Decision == ImageReviewDecision.ConfirmedDefect &&
               enabled.BoxReview == BoxReviewDecision.Confirmed &&
               enabled.Boxes.Count == 1 &&
               enabledEligibility.AnomaEvaluation &&
               enabledEligibility.YoloPositive,
            "turning training ON did not restore eligibility from the preserved review");

        ReviewState legacyExcluded = _workflow.SetTrainingEnabled(
            _workflow.Exclude(new ReviewState(), "legacy"),
            enabled: true);
        Assert(legacyExcluded.Decision == ImageReviewDecision.Reviewing,
            "legacy Excluded state was guessed as a confirmed decision");
    }

    private void MigrationIsSafeAndIdempotent(string root)
    {
        string image = NewImage(root, "legacy_normal.bmp");
        var legacy = new ImageStateDto
        {
            IsNormal = true,
            HasManualAnomalyDecision = true,
            ReviewStatus = ReviewStatus.ReviewDone,
            DecisionSource = "manual"
        };
        string legacyPath = ImageStateService.GetStatePath(image);
        string original = JsonSerializer.Serialize(legacy, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(legacyPath, original);

        ReviewStateLoadResult projected = _repository.Load(image);
        Assert(projected.IsLegacyProjection && !File.Exists(ReviewRepository.GetReviewPath(image)),
            "legacy load must remain read-only");

        var migration = new LegacyReviewMigrationService(_repository);
        ReviewMigrationReport first = migration.Migrate(new[] { image });
        Assert(first.Converted == 1 && first.Failed == 0, "first migration failed");
        Assert(File.Exists(LegacyReviewMigrationService.GetLegacyBackupPath(image)), "legacy backup missing");
        Assert(File.ReadAllText(legacyPath) == original, "legacy source was modified");

        ReviewMigrationReport second = migration.Migrate(new[] { image });
        Assert(second.Converted == 0 && second.AlreadyMigrated == 1, "migration is not idempotent");
    }

    private void AmbiguousMigrationStaysReviewing(string root)
    {
        string image = NewImage(root, "legacy_ambiguous.bmp");
        var legacy = new ImageStateDto
        {
            IsNormal = true,
            HasManualAnomalyDecision = true,
            ReviewStatus = ReviewStatus.ReviewDone,
            Labels = new List<LabelDto>
            {
                new() { ClassName = "dent", X = 0.5, Y = 0.5, Width = 0.2, Height = 0.2 }
            }
        };
        File.WriteAllText(ImageStateService.GetStatePath(image), JsonSerializer.Serialize(legacy));
        ReviewMigrationReport report = new LegacyReviewMigrationService(_repository).Migrate(new[] { image });
        Assert(report.Ambiguous == 1, "ambiguous count mismatch");
        Assert(_repository.Load(image).State.Decision == ImageReviewDecision.Reviewing,
            "ambiguous legacy state must not become normal");
    }

    private void SelectionStaysInScope(string root)
    {
        string aNormal = NewImage(root, "scope_a_normal.bmp");
        string aDefect = NewImage(root, "scope_a_defect.bmp");
        string bNormal = NewImage(root, "scope_b_normal.bmp");
        _repository.Save(aNormal, _workflow.ConfirmNormal(new ReviewState(), true));
        ReviewState defect = _workflow.ConfirmDefect(new ReviewState());
        defect = _workflow.ReplaceBoxesAfterEdit(defect, new[]
        {
            new ReviewBox { ClassName = "dent", X = 0.5, Y = 0.5, Width = 0.2, Height = 0.2 }
        });
        _repository.Save(aDefect, _workflow.ConfirmBoxes(defect));
        _repository.Save(bNormal, _workflow.ConfirmNormal(new ReviewState(), true));

        TrainingDatasetSelection selection = _selector.Select(new[]
        {
            Input(aNormal, "batch-a"), Input(aDefect, "batch-a")
        });
        Assert(selection.AnomaInputs.All(input => input.BatchKey == "batch-a"), "other batch entered Anoma");
        Assert(selection.YoloInputs.All(input => input.BatchKey == "batch-a"), "other batch entered YOLO");
        Assert(selection.AnomaInputs.All(input => input.ImagePath != bNormal), "unselected batch image included");
    }

    private void YoloBackgroundsAreBalancedAutomatically(string root)
    {
        string normal1 = NewImage(root, "balance_normal_1.bmp");
        string normal2 = NewImage(root, "balance_normal_2.bmp");
        string normal3 = NewImage(root, "balance_normal_3.bmp");
        string disabledNormal = NewImage(root, "balance_normal_disabled.bmp");
        string defect1 = NewImage(root, "balance_defect_1.bmp");
        string defect2 = NewImage(root, "balance_defect_2.bmp");

        foreach (string normal in new[] { normal1, normal2, normal3 })
            _repository.Save(normal, _workflow.ConfirmNormal(new ReviewState(), useAsYoloBackground: false));
        ReviewState excludedBackground = _workflow.SetTrainingEnabled(
            _workflow.ConfirmNormal(new ReviewState(), useAsYoloBackground: false),
            enabled: false);
        _repository.Save(disabledNormal, excludedBackground);

        foreach (string defect in new[] { defect1, defect2 })
        {
            ReviewState state = _workflow.ConfirmDefect(new ReviewState());
            state = _workflow.ReplaceBoxesAfterEdit(state, new[]
            {
                new ReviewBox
                {
                    ClassName = "dent",
                    X = 0.5,
                    Y = 0.5,
                    Width = 0.2,
                    Height = 0.2
                }
            });
            _repository.Save(defect, _workflow.ConfirmBoxes(state));
        }

        var inputs = new[]
        {
            Input(normal1, "batch-a"),
            Input(normal2, "batch-a"),
            Input(normal3, "batch-b"),
            Input(disabledNormal, "batch-b"),
            Input(defect1, "batch-a"),
            Input(defect2, "batch-b")
        };
        TrainingDatasetSelection selection = _selector.Select(inputs, yoloBackgroundToPositiveRatio: 1.0);

        Assert(selection.YoloPositiveInputCount == 2 &&
               selection.YoloBackgroundCandidateCount == 3 &&
               selection.YoloBackgroundSelectedCount == 2 &&
               selection.ExcludedNormalBackgroundByBalance == 1 &&
               selection.YoloInputs.Count == 4,
            "YOLO 1:1 positive/background balancing counts are incorrect");
        Assert(selection.YoloInputs
                .Where(input => _repository.Load(input.ImagePath).State.Decision == ImageReviewDecision.ConfirmedNormal)
                .All(input => !_repository.Load(input.ImagePath).State.UseAsYoloBackground),
            "automatic backgrounds still depended on the legacy per-image checkbox");
        Assert(selection.YoloInputs.All(input => input.ImagePath != disabledNormal),
            "training-disabled normal entered the background sample");

        string[] firstBackgrounds = selection.YoloInputs
            .Where(input => _repository.Load(input.ImagePath).State.Decision == ImageReviewDecision.ConfirmedNormal)
            .Select(input => input.ImagePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] repeatedBackgrounds = _selector.Select(inputs.Reverse().ToArray(), 1.0).YoloInputs
            .Where(input => _repository.Load(input.ImagePath).State.Decision == ImageReviewDecision.ConfirmedNormal)
            .Select(input => input.ImagePath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert(firstBackgrounds.SequenceEqual(repeatedBackgrounds, StringComparer.OrdinalIgnoreCase),
            "automatic background selection changed when candidate order changed");

        TrainingDatasetSelection noPositive = _selector.Select(
            inputs.Where(input => input.ImagePath != defect1 && input.ImagePath != defect2).ToArray(),
            1.0);
        Assert(noPositive.YoloBackgroundCandidateCount == 3 &&
               noPositive.YoloBackgroundSelectedCount == 0 &&
               noPositive.YoloInputs.Count == 0,
            "normal backgrounds were selected without a YOLO-positive image");
    }

    private void ProjectionFlagsMatchState(string root)
    {
        string normal = NewImage(root, "projection_normal.bmp");
        string defect = NewImage(root, "projection_defect.bmp");
        string excluded = NewImage(root, "projection_excluded.bmp");
        _repository.Save(normal, _workflow.ConfirmNormal(new ReviewState(), true));
        _repository.Save(defect, _workflow.ConfirmDefect(new ReviewState()));
        _repository.Save(excluded, _workflow.Exclude(new ReviewState(), "test"));

        var projection = new ReviewProjectionService();
        var normalView = projection.Create(_repository.Load(normal), new PredictionSnapshot(),
            _selector.Evaluate(_repository.Load(normal)));
        var defectView = projection.Create(_repository.Load(defect), new PredictionSnapshot(),
            _selector.Evaluate(_repository.Load(defect)));
        var excludedView = projection.Create(_repository.Load(excluded), new PredictionSnapshot(),
            _selector.Evaluate(_repository.Load(excluded)));
        Assert(normalView.IsConfirmedNormal && defectView.IsConfirmedDefect && excludedView.IsExcluded,
            "projection summary flags mismatch");
        Assert(normalView.StatusColorMeaningText.StartsWith("초록색", StringComparison.Ordinal) &&
               defectView.StatusColorMeaningText.StartsWith("주황색", StringComparison.Ordinal) &&
               excludedView.StatusColorMeaningText.StartsWith("회색", StringComparison.Ordinal),
            "projection color explanations do not match review state");
    }

    private static void ImageListColorsDistinguishPendingBoxes()
    {
        Assert(StatusColor(new ImageItem
        {
            IsReviewConfirmedDefect = true,
            IsBoxReviewConfirmed = false
        }) == Color.FromRgb(255, 226, 179),
            "defect with pending box review must be orange");

        Assert(StatusColor(new ImageItem
        {
            IsReviewConfirmedDefect = true,
            IsBoxReviewConfirmed = true
        }) == Color.FromRgb(255, 220, 220),
            "defect with confirmed boxes must be red");

        Assert(StatusColor(new ImageItem { IsReviewConfirmedNormal = true }) ==
               Color.FromRgb(220, 255, 225),
            "confirmed normal must be green");
        Assert(StatusColor(new ImageItem()) == Color.FromRgb(255, 247, 220),
            "unreviewed image must be yellow");
    }

    private static Color StatusColor(ImageItem item)
    {
        var converter = new ImageStatusToColorConverter();
        return ((SolidColorBrush)converter.Convert(
            item,
            typeof(Brush),
            parameter: null!,
            CultureInfo.InvariantCulture)).Color;
    }

    private void AcceptAiDecisionUsesAnomaOnly()
    {
        PredictionSnapshot anomaDefectNoYolo = DefectPrediction();
        ReviewState accepted = _workflow.AcceptAiDecision(new ReviewState(), anomaDefectNoYolo);
        Assert(accepted.Decision == ImageReviewDecision.ConfirmedDefect,
            "Anoma defect with zero YOLO boxes must remain defect");
        Assert(accepted.DecisionSource == ReviewDecisionSource.AcceptedAiPrediction,
            "AI acceptance source must be distinct from manual");
    }

    private static void PipelineContractIsCorrect()
    {
        object config = InferencePipelineConfigBuilder.Build(
            new AppSettings(),
            InferencePipelineConfigBuilder.AnomaThenYolo,
            "2-stage");
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(config));
        JsonElement root = document.RootElement;
        JsonElement pipeline = root.GetProperty("pipeline");
        Assert(root.GetProperty("schema_version").GetInt32() == 4, "pipeline schema version mismatch");
        Assert(pipeline.GetProperty("mode").GetString() == "anoma_then_yolo", "pipeline mode mismatch");
        Assert(pipeline.GetProperty("skip_yolo_when_stage1_normal").GetBoolean(), "YOLO skip flag mismatch");
        Assert(root.GetProperty("yolo").GetProperty("imgsz").GetInt32() == 1280,
            "YOLO inference size must match the exported 1280 model");
        Assert(root.GetProperty("mask").GetProperty("input_size").GetInt32() == 512,
            "Mask inference size must be 512");
        JsonElement anoma = root.GetProperty("anoma");
        Assert(anoma.GetProperty("mode").GetString() == "stretch",
            "Anoma inference fallback must match exported stretch preprocessing");
        Assert(anoma.GetProperty("crop_padding_px").GetInt32() == 0,
            "stretch preprocessing must not emit crop padding");
        Assert(pipeline.GetProperty("required_models").EnumerateArray()
            .Any(item => item.GetString() == "mask"), "Mask must be a required package model");
        JsonElement autoReview = root.GetProperty("auto_review");
        Assert(autoReview.GetProperty("enabled").GetBoolean(), "auto review must be enabled");
        Assert(autoReview.GetProperty("policy_version").GetString() == "auto_review_v2_no_audit",
            "audit-free auto review policy version mismatch");
        Assert(autoReview.GetProperty("anoma_normal_threshold_multiplier").GetDouble() == 0.95,
            "auto normal multiplier mismatch");
        Assert(autoReview.GetProperty("anoma_defect_threshold_multiplier").GetDouble() == 1.6,
            "auto defect multiplier mismatch");
        Assert(autoReview.GetProperty("yolo_box_min_confidence").GetDouble() == 0.85,
            "auto YOLO confidence mismatch");
        Assert(autoReview.GetProperty("audit_sample_rate").GetDouble() == 0,
            "removed audit sampling must remain disabled in exported packages");
        Assert(!root.TryGetProperty("fusion", out _), "legacy fusion section must not be emitted");
    }

    private static void AnomaPackageUsesExportedResizeCalibration(string root)
    {
        string outDirectory = Path.Combine(root, "anoma_calibration");
        Directory.CreateDirectory(outDirectory);
        File.WriteAllText(
            Path.Combine(outDirectory, "inference_config.json"),
            """
            {
              "schema_version": 2,
              "input_size": 448,
              "score_threshold": 0.03125,
              "preprocessing": {
                "resize": "stretch"
              }
            }
            """);

        AnomaInferenceCalibration calibration =
            AnomaInferenceCalibrationReader.TryLoad(outDirectory)
            ?? throw new InvalidOperationException("Anoma calibration was not loaded");
        Assert(calibration.ResizeMode == "stretch", "exported resize mode was not loaded");
        Assert(calibration.CropPaddingPx == 0, "stretch calibration must clear crop padding");

        var settings = new AppSettings
        {
            AnomaInfer = new AnomaInferSection
            {
                Mode = "crop",
                InputSize = 640,
                ScoreThres = 0.5,
                CropPaddingPx = 8
            }
        };
        object config = InferencePipelineConfigBuilder.Build(
            settings,
            InferencePipelineConfigBuilder.AnomaThenYolo,
            "2-stage",
            calibration.InputSize,
            calibration.ScoreThreshold,
            calibration.ResizeMode,
            calibration.CropPaddingPx);
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(config));
        JsonElement anoma = document.RootElement.GetProperty("anoma");
        Assert(anoma.GetProperty("mode").GetString() == "stretch",
            "package ignored exported Anoma resize mode");
        Assert(anoma.GetProperty("input_size").GetInt32() == 448,
            "package ignored calibrated Anoma input size");
        Assert(Math.Abs(anoma.GetProperty("score_thres").GetDouble() - 0.03125) < 1e-12,
            "package ignored calibrated Anoma threshold");
        Assert(anoma.GetProperty("crop_padding_px").GetInt32() == 0,
            "stretch package retained crop padding");
    }

    private void InferenceResultInconsistenciesAreRejected(string root)
    {
        const string contextId = "ctx_result_consistency";
        var reader = new PredictionReader();

        string consistentDefectPath = Path.Combine(root, "consistent_defect.infer.json");
        WriteInfer(
            consistentDefectPath,
            imageId: "consistent_defect",
            yoloExecuted: true,
            detections: Array.Empty<object>(),
            score: 0.0275331,
            threshold: 0.02580488,
            decision: "anomaly",
            finalIsDefect: true);

        PredictionSnapshot consistentDefect = reader.Read(
            consistentDefectPath,
            contextId,
            "consistent_defect");
        Assert(!consistentDefect.ParseFailed &&
               consistentDefect.AnomaIsDefect &&
               consistentDefect.YoloExecuted &&
               consistentDefect.YoloDetectionCount == 0,
            "consistent Anoma defect with zero YOLO detections was rejected");

        var emptyReview = new ReviewStateLoadResult();
        ImageReviewProjection defectProjection = new ReviewProjectionService().Create(
            emptyReview,
            consistentDefect,
            _selector.Evaluate(emptyReview, consistentDefect));
        Assert(defectProjection.AiAnomaText.Contains("불량", StringComparison.Ordinal) &&
               defectProjection.AiYoloText == "YOLO 0개",
            "projection did not use the persisted Anoma decision and YOLO execution state");

        string consistentNormalPath = Path.Combine(root, "consistent_normal.infer.json");
        WriteInfer(
            consistentNormalPath,
            imageId: "consistent_normal",
            yoloExecuted: false,
            detections: Array.Empty<object>(),
            score: 0.018,
            threshold: 0.02580488,
            decision: "normal",
            finalIsDefect: false);
        PredictionSnapshot consistentNormal = reader.Read(
            consistentNormalPath,
            contextId,
            "consistent_normal");
        ImageReviewProjection normalProjection = new ReviewProjectionService().Create(
            emptyReview,
            consistentNormal,
            _selector.Evaluate(emptyReview, consistentNormal));
        Assert(!consistentNormal.ParseFailed &&
               !consistentNormal.YoloExecuted &&
               normalProjection.AiYoloText == "YOLO 미실행",
            "projection did not show the actual skipped YOLO state");

        string scoreMismatchPath = Path.Combine(root, "score_decision_mismatch.infer.json");
        WriteInfer(
            scoreMismatchPath,
            imageId: "score_decision_mismatch",
            yoloExecuted: false,
            detections: Array.Empty<object>(),
            score: 0.03,
            threshold: 0.02,
            decision: "normal",
            finalIsDefect: false);
        PredictionSnapshot scoreMismatch = reader.Read(scoreMismatchPath, contextId);
        Assert(scoreMismatch.ParseFailed &&
               scoreMismatch.Error.Contains("score/decision mismatch", StringComparison.OrdinalIgnoreCase),
            "Anoma score/decision mismatch was accepted");

        string finalMismatchPath = Path.Combine(root, "anoma_final_mismatch.infer.json");
        WriteInfer(
            finalMismatchPath,
            imageId: "anoma_final_mismatch",
            yoloExecuted: true,
            detections: Array.Empty<object>(),
            score: 0.03,
            threshold: 0.02,
            decision: "anomaly",
            finalIsDefect: false);
        PredictionSnapshot finalMismatch = reader.Read(finalMismatchPath, contextId);
        Assert(finalMismatch.ParseFailed &&
               finalMismatch.Error.Contains("Anoma/final decision mismatch", StringComparison.OrdinalIgnoreCase),
            "Anoma/final decision mismatch was accepted");

        string yoloMismatchPath = Path.Combine(root, "yolo_execution_mismatch.infer.json");
        WriteInfer(
            yoloMismatchPath,
            imageId: "yolo_execution_mismatch",
            yoloExecuted: false,
            detections: new object[]
            {
                new
                {
                    class_name = "dent",
                    conf = 0.9,
                    bbox_xywh_norm = new[] { 0.5, 0.5, 0.2, 0.2 }
                }
            },
            score: 0.01,
            threshold: 0.02,
            decision: "normal",
            finalIsDefect: false);
        PredictionSnapshot yoloMismatch = reader.Read(yoloMismatchPath, contextId);
        Assert(yoloMismatch.ParseFailed &&
               yoloMismatch.Error.Contains("YOLO detections", StringComparison.OrdinalIgnoreCase),
            "YOLO execution/detection mismatch was accepted");

        PredictionSnapshot imageMismatch = reader.Read(
            consistentNormalPath,
            contextId,
            "different_image_id");
        Assert(imageMismatch.ParseFailed &&
               imageMismatch.Error.Contains("image_id mismatch", StringComparison.OrdinalIgnoreCase),
            "manifest/infer image_id mismatch was accepted");

        void WriteInfer(
            string path,
            string imageId,
            bool yoloExecuted,
            object[] detections,
            double score,
            double threshold,
            string decision,
            bool finalIsDefect)
        {
            File.WriteAllText(path, JsonSerializer.Serialize(new
            {
                schema_version = 2,
                image_id = imageId,
                inference_context_id = contextId,
                image_size = new { w = 2448, h = 2048 },
                yolo = new
                {
                    executed = yoloExecuted,
                    skipped_reason = yoloExecuted ? "" : "stage1_normal",
                    confidence_threshold = 0.25,
                    model_sha256 = "yolo_hash",
                    detections
                },
                anoma = new
                {
                    executed = true,
                    score,
                    score_threshold = threshold,
                    model_sha256 = "anoma_hash",
                    decision
                },
                final = new { is_defect = finalIsDefect, reason = Array.Empty<string>() }
            }));
        }
    }

    private void InferenceContextMismatchIsRejected(string root)
    {
        string inferPath = Path.Combine(root, "context_mismatch.infer.json");
        File.WriteAllText(inferPath, JsonSerializer.Serialize(new
        {
            schema_version = 2,
            image_id = "context_mismatch",
            inference_context_id = "ctx_actual",
            image_size = new { w = 640, h = 640 },
            yolo = new
            {
                executed = false,
                confidence_threshold = 0.25,
                model_sha256 = "yolo_hash",
                detections = Array.Empty<object>()
            },
            anoma = new
            {
                executed = true,
                score = 0.9,
                score_threshold = 0.5,
                model_sha256 = "anoma_hash",
                decision = "anomaly"
            },
            final = new { is_defect = true, reason = new[] { "stage1_abnormal" } }
        }));

        var reader = new PredictionReader();
        PredictionSnapshot matching = reader.Read(inferPath, "ctx_actual");
        PredictionSnapshot mismatch = reader.Read(inferPath, "ctx_expected");
        Assert(!matching.ParseFailed && matching.HasAnomaDecision, "matching context was rejected");
        Assert(mismatch.ParseFailed && mismatch.Error.Contains("mismatch", StringComparison.OrdinalIgnoreCase),
            "mismatched context was accepted");

        string batchRoot = Path.Combine(root, "context_mismatch_batch");
        Directory.CreateDirectory(Path.Combine(batchRoot, "images"));
        Directory.CreateDirectory(Path.Combine(batchRoot, "inference"));
        Directory.CreateDirectory(Path.Combine(batchRoot, "meta"));
        File.WriteAllBytes(Path.Combine(batchRoot, "images", "context_mismatch.bmp"), new byte[] { 0x42, 0x4D });
        File.Copy(inferPath, Path.Combine(batchRoot, "inference", "context_mismatch.infer.json"));
        File.WriteAllText(Path.Combine(batchRoot, "meta", "DONE.flag"), "done");
        File.WriteAllText(Path.Combine(batchRoot, "meta", "manifest.json"), JsonSerializer.Serialize(new
        {
            schema_version = 3,
            batch_type = "inference",
            batch_id = "context_mismatch_batch",
            created_at = "2026-01-01T00:00:00",
            inference_context = new
            {
                status = "recorded",
                context_id = "ctx_expected",
                pipeline_mode = "anoma_then_yolo",
                package_fingerprint = "fingerprint",
                pipeline_sha256 = "pipeline_hash"
            },
            items = new[]
            {
                new
                {
                    id = "context_mismatch",
                    processed_image = "images/context_mismatch.bmp",
                    infer_json = "inference/context_mismatch.infer.json"
                }
            }
        }));
        BatchFolderValidationResult batchValidation = BatchFolderValidationService.Validate(batchRoot);
        Assert(!batchValidation.IsValid &&
               batchValidation.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase),
            "batch validation did not reject mismatched context");

        string image = NewImage(root, "context_mismatch.bmp");
        _repository.Save(image, _workflow.ConfirmNormal(new ReviewState(), useAsYoloBackground: false));
        var selection = new TrainingDatasetSelection { TotalCandidates = 1 };
        selection.AnomaInputs.Add(new TrainingImageInput
        {
            ImagePath = image,
            InferJsonPath = inferPath,
            RequiresInfer = true,
            ExpectedInferenceContextId = "ctx_expected",
            BatchKey = "context-test"
        });

        DatasetValidationResult validation = new TrainingDatasetValidator(_repository, _selector)
            .Validate(selection, trainAnoma: true, trainYolo: false);
        Assert(validation.Errors.Any(error => error.Contains("mismatch", StringComparison.OrdinalIgnoreCase)),
            "training validation did not reject mismatched context");
    }

    private static PredictionSnapshot DefectPrediction(params ReviewBox[] boxes)
        => DefectPredictionWithScore(score: 0.10, threshold: 0.02, boxes: boxes);

    private static PredictionSnapshot DefectPredictionWithScore(
        double score,
        double threshold,
        params ReviewBox[] boxes) => new()
    {
        HasFile = true,
        HasAnomaDecision = true,
        AnomaIsDefect = true,
        AnomaScore = score,
        AnomaScoreThreshold = threshold,
        InferenceContextId = "ctx_auto_review_test",
        YoloBoxes = boxes
    };

    private static PredictionSnapshot NormalPrediction(double score, double threshold) => new()
    {
        HasFile = true,
        HasAnomaDecision = true,
        AnomaIsDefect = false,
        AnomaScore = score,
        AnomaScoreThreshold = threshold,
        InferenceContextId = "ctx_auto_review_test"
    };

    private static AutoReviewPolicy AutoPolicy(double auditSampleRate = 0) => new()
    {
        Enabled = true,
        PolicyVersion = "auto_review_test_v1",
        AnomaNormalThresholdMultiplier = 0.95,
        AnomaDefectThresholdMultiplier = 1.6,
        YoloBoxMinConfidence = 0.85,
        AuditSampleRate = auditSampleRate
    };

    private static ReviewBox PredictionBox(string className, double confidence) => new()
    {
        ClassName = className,
        X = 0.5,
        Y = 0.5,
        Width = 0.2,
        Height = 0.2,
        Source = "ai_prediction",
        PredictionConfidence = confidence
    };

    private static TrainingImageInput Input(string imagePath, string batchKey) => new()
    {
        ImagePath = imagePath,
        BatchKey = batchKey,
        RequiresInfer = false
    };

    private static string NewImage(string root, string name)
    {
        string path = Path.Combine(root, name);
        File.WriteAllBytes(path, new byte[] { 0x42, 0x4D });
        return path;
    }

    private void Run(string name, Action test)
    {
        test();
        _passed++;
        Console.WriteLine("[PASS] " + name);
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
