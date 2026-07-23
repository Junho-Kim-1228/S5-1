using CoilTrainingUI.Models;
using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Review;
using System.Text.Json;

var suite = new CoreReviewTests();
suite.RunAll();

internal sealed class CoreReviewTests
{
    private readonly ReviewRepository _repository = new();
    private readonly ReviewWorkflowService _workflow = new();
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
            Run("load and selection are read-only", () => LoadAndSelectionAreReadOnly(root));
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
            Run("Anoma alone controls accepted AI decision", AcceptAiDecisionUsesAnomaOnly);
            Run("pipeline contract is Anoma then YOLO without fusion", PipelineContractIsCorrect);
            Run("inference context mismatch is rejected", () => InferenceContextMismatchIsRejected(root));
            Console.WriteLine($"PASS: {_passed}/14 core review tests");
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void LoadAndSelectionAreReadOnly(string root)
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
        Assert(pipeline.GetProperty("mode").GetString() == "anoma_then_yolo", "pipeline mode mismatch");
        Assert(pipeline.GetProperty("skip_yolo_when_stage1_normal").GetBoolean(), "YOLO skip flag mismatch");
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

    private static PredictionSnapshot DefectPrediction(params ReviewBox[] boxes) => new()
    {
        HasFile = true,
        HasAnomaDecision = true,
        AnomaIsDefect = true,
        AnomaScore = 0.9,
        YoloBoxes = boxes
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
