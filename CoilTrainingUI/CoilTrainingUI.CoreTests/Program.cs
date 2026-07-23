using CoilTrainingUI.Converters;
using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Imaging;
using CoilTrainingUI.Services.Review;
using System.Globalization;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
            Run("legacy migration is backed up and idempotent", () => MigrationIsSafeAndIdempotent(root));
            Run("ambiguous legacy data stays reviewing", () => AmbiguousMigrationStaysReviewing(root));
            Run("selection stays inside supplied batch scope", () => SelectionStaysInScope(root));
            Run("projection flags match persisted state", () => ProjectionFlagsMatchState(root));
            Run("image list colors distinguish pending box review", ImageListColorsDistinguishPendingBoxes);
            Run("Anoma alone controls accepted AI decision", AcceptAiDecisionUsesAnomaOnly);
            Run("pipeline contract is Anoma then YOLO without fusion", PipelineContractIsCorrect);
            Run("inference context mismatch is rejected", () => InferenceContextMismatchIsRejected(root));
            Run("final training commands are explicit", FinalTrainingCommandsAreExplicit);
            Run("fine-tune command uses warm-start policy", FineTuneCommandUsesWarmStartPolicy);
            Run("Python environment validation follows selected pipeline", () => PythonEnvironmentValidationFollowsPipeline(root));
            Run("model registry tracks lifecycle and lineage", () => ModelRegistryTracksLifecycle(root));
            Run("inference package deployment is validated and backed up", () => InferencePackageDeploymentIsSafe(root));
            Run("inference package rejects a missing mask model", () => MissingMaskModelIsRejected(root));
            Run("image cache reuses and invalidates frozen bitmaps", () => ImageCacheIsBoundedAndFresh(root));
            Run("auto review accepts high-confidence normal", () => AutoReviewAcceptsNormal(root));
            Run("auto review confirms only high-confidence boxes", () => AutoReviewConfirmsHighConfidenceBoxes(root));
            Run("auto-reviewed boxless defect stays Anoma-only", () => AutoReviewBoxlessDefectIsAnomaOnly(root));
            Run("auto review leaves gray-zone prediction untouched", AutoReviewLeavesGrayZoneUntouched);
            Run("auto review audit sample remains unreviewed", AutoReviewAuditSampleRemainsUnreviewed);
            Run("auto review protects existing user state", AutoReviewProtectsExistingState);
            Run("prediction-only boxes stay out of editable layer", PredictionOnlyBoxesStayOutOfEditableLayer);
            Console.WriteLine($"PASS: {_passed}/29 core review tests");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private void AutoReviewAcceptsNormal(string root)
    {
        string image = NewImage(root, "auto_normal.bmp");
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            NormalPrediction(score: 0.005, threshold: 0.02),
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

    private void AutoReviewLeavesGrayZoneUntouched()
    {
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            DefectPredictionWithScore(score: 0.03, threshold: 0.02),
            AutoPolicy(),
            "batch/gray");
        Assert(!evaluation.ShouldPersist && evaluation.Disposition == AutoReviewDisposition.NotApplied,
            "gray-zone prediction changed review state");
    }

    private void AutoReviewAuditSampleRemainsUnreviewed()
    {
        AutoReviewPolicy policy = AutoPolicy(auditSampleRate: 1.0);
        AutoReviewEvaluation evaluation = _autoReview.Evaluate(
            new ReviewStateLoadResult(),
            NormalPrediction(score: 0.001, threshold: 0.02),
            policy,
            "batch/audit");
        Assert(evaluation.Disposition == AutoReviewDisposition.AuditHeld,
            "audit sample was not held");
        Assert(evaluation.StateToPersist?.Decision == ImageReviewDecision.Unreviewed,
            "audit sample must remain unreviewed");
        Assert(evaluation.StateToPersist?.AutoReview?.HeldForAudit == true,
            "audit metadata is missing");

        ReviewState manuallyConfirmed = _workflow.ConfirmNormal(
            evaluation.StateToPersist ?? throw new InvalidOperationException("audit state is missing"),
            useAsYoloBackground: true);
        var manualLoad = new ReviewStateLoadResult
        {
            HasReviewFile = true,
            State = manuallyConfirmed
        };
        ImageReviewProjection projection = new ReviewProjectionService().Create(
            manualLoad,
            NormalPrediction(score: 0.001, threshold: 0.02),
            _selector.Evaluate(manualLoad));
        Assert(!projection.IsAutoReviewAudit && projection.IsConfirmedNormal,
            "completed audit remained displayed as pending");
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
        Assert(StatusColor(new ImageItem { IsAutoReviewAudit = true }) ==
               Color.FromRgb(232, 221, 255),
            "audit sample must use its own highlight color");
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
        Assert(pipeline.GetProperty("required_models").EnumerateArray()
            .Any(item => item.GetString() == "mask"), "Mask must be a required package model");
        JsonElement autoReview = root.GetProperty("auto_review");
        Assert(autoReview.GetProperty("enabled").GetBoolean(), "auto review must be enabled");
        Assert(autoReview.GetProperty("anoma_normal_threshold_multiplier").GetDouble() == 0.5,
            "auto normal multiplier mismatch");
        Assert(autoReview.GetProperty("anoma_defect_threshold_multiplier").GetDouble() == 2.0,
            "auto defect multiplier mismatch");
        Assert(autoReview.GetProperty("yolo_box_min_confidence").GetDouble() == 0.85,
            "auto YOLO confidence mismatch");
        Assert(!root.TryGetProperty("fusion", out _), "legacy fusion section must not be emitted");
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
        AnomaNormalThresholdMultiplier = 0.5,
        AnomaDefectThresholdMultiplier = 2.0,
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
