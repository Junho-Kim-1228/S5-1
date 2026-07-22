using CoilTrainingUI.Models.Review;
using CoilTrainingUI.Models.InferenceBatch;
using CoilTrainingUI.Services;
using CoilTrainingUI.Services.Review;
using CoilInspectionApp;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length == 1 && string.Equals(args[0], "--self-test", StringComparison.OrdinalIgnoreCase))
{
    RunInferenceContractSelfTest();
    return 0;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: LegacyBatchMigration <training_inbox> [--apply] [batch-folder ...] | --self-test");
    return 2;
}

string inboxRoot = Path.GetFullPath(args[0]);
bool apply = args.Any(arg => string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase));
var requestedBatches = args.Skip(1)
    .Where(arg => !string.Equals(arg, "--apply", StringComparison.OrdinalIgnoreCase))
    .ToList();

if (!Directory.Exists(inboxRoot))
{
    Console.Error.WriteLine("training_inbox not found: " + inboxRoot);
    return 2;
}

List<string> batchRoots = requestedBatches.Count > 0
    ? requestedBatches.Select(name => Path.GetFullPath(Path.Combine(inboxRoot, name))).ToList()
    : Directory.GetDirectories(inboxRoot)
        .Where(path => !string.Equals(Path.GetFileName(path), "_train_runs", StringComparison.OrdinalIgnoreCase))
        .Where(path => Directory.EnumerateFiles(path, "*.state.json", SearchOption.AllDirectories).Any())
        .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
        .ToList();

var repository = new ReviewRepository();
var migrationService = new LegacyReviewMigrationService(repository);
int totalImages = 0;
int totalLegacy = 0;
int totalConverted = 0;
int totalAmbiguous = 0;
int totalSeedConfirmed = 0;
int totalFailures = 0;

Console.WriteLine(apply ? "MODE: APPLY" : "MODE: DRY-RUN");
foreach (string batchRoot in batchRoots)
{
    string batchName = Path.GetFileName(batchRoot);
    string manifestPath = Path.Combine(batchRoot, "meta", "manifest.json");
    if (!File.Exists(manifestPath))
    {
        Console.WriteLine($"SKIP {batchName}: manifest.json missing");
        continue;
    }

    List<string> imagePaths;
    try
    {
        imagePaths = ReadManifestImagePaths(batchRoot, manifestPath);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"FAIL {batchName}: {ex.Message}");
        totalFailures++;
        continue;
    }

    int legacyCount = imagePaths.Count(path => File.Exists(ImageStateService.GetStatePath(path)));
    int existingReviewCount = imagePaths.Count(repository.HasReviewFile);
    bool trustedManualSeedNormal = IsTrustedManualSeedNormalBatch(manifestPath, imagePaths);
    totalImages += imagePaths.Count;
    totalLegacy += legacyCount;

    if (!apply)
    {
        PreviewResult preview = PreviewLegacyConversions(imagePaths, trustedManualSeedNormal);
        totalAmbiguous += preview.Ambiguous;
        totalSeedConfirmed += preview.SeedConfirmed;
        totalFailures += preview.Failed;
        Console.WriteLine(
            $"DRY {batchName}: images={imagePaths.Count}, legacy={legacyCount}, review={existingReviewCount}, " +
            $"normal={preview.ConfirmedNormal}, defect={preview.ConfirmedDefect}, " +
            $"reviewing={preview.Reviewing}, unreviewed={preview.Unreviewed}, " +
            $"seed_confirmed={preview.SeedConfirmed}, ambiguous={preview.Ambiguous}, failed={preview.Failed}");
        continue;
    }

    ReviewMigrationReport report = migrationService.Migrate(imagePaths);
    int seedConfirmed = trustedManualSeedNormal
        ? PromoteTrustedManualSeedNormals(imagePaths, repository)
        : 0;
    totalConverted += report.Converted;
    totalAmbiguous += report.Ambiguous;
    totalSeedConfirmed += seedConfirmed;
    totalFailures += report.Failed;

    int reviewCountAfter = imagePaths.Count(repository.HasReviewFile);
    if (report.Failed == 0 && reviewCountAfter == legacyCount)
    {
        UpdateLegacyManifest(manifestPath);
        ManifestDto migratedManifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
        if (!string.Equals(
                migratedManifest.InferenceContext?.Status,
                "not_applicable",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("migrated manifest inference_context is invalid");
        }
    }
    else
    {
        totalFailures++;
        Console.WriteLine(
            $"FAIL {batchName}: post-check legacy={legacyCount}, review={reviewCountAfter}, migration_failed={report.Failed}");
    }

    var decisions = imagePaths
        .Where(repository.HasReviewFile)
        .Select(path => repository.Load(path).State.Decision)
        .GroupBy(decision => decision)
        .ToDictionary(group => group.Key, group => group.Count());

    Console.WriteLine(
        $"APPLY {batchName}: converted={report.Converted}, already={report.AlreadyMigrated}, " +
        $"seed_confirmed={seedConfirmed}, ambiguous={report.Ambiguous}, failed={report.Failed}, " +
        $"normal={GetCount(decisions, ImageReviewDecision.ConfirmedNormal)}, " +
        $"defect={GetCount(decisions, ImageReviewDecision.ConfirmedDefect)}, " +
        $"reviewing={GetCount(decisions, ImageReviewDecision.Reviewing)}, " +
        $"unreviewed={GetCount(decisions, ImageReviewDecision.Unreviewed)}");

    foreach (string failure in report.Failures.Take(10))
        Console.WriteLine("  - " + failure);
}

Console.WriteLine(
    $"TOTAL batches={batchRoots.Count}, images={totalImages}, legacy={totalLegacy}, " +
    $"converted={totalConverted}, seed_confirmed={totalSeedConfirmed}, " +
    $"ambiguous={totalAmbiguous}, failures={totalFailures}");
return totalFailures == 0 ? 0 : 1;

static void RunInferenceContractSelfTest()
{
    string testRoot = Path.Combine(Path.GetTempPath(), "coil-inference-contract-" + Guid.NewGuid().ToString("N"));
    string packageRoot = Path.Combine(testRoot, "InferencePackage");
    string configDirectory = Path.Combine(packageRoot, "config");
    string modelsDirectory = Path.Combine(packageRoot, "models");
    Directory.CreateDirectory(configDirectory);
    Directory.CreateDirectory(modelsDirectory);

    try
    {
        string pipelineJson = """
        {
          "schema_version": 1,
          "pipeline": {
            "mode": "anoma_then_yolo",
            "required_models": ["anoma", "yolo"],
            "skip_yolo_when_stage1_normal": true
          },
          "anoma": {
            "model": "models/anoma.onnx",
            "mode": "crop",
            "input_size": 640,
            "score_thres": 12.527
          },
          "yolo": {
            "model": "models/yolo.onnx",
            "imgsz": 1280,
            "conf_thres": 0.25,
            "iou_thres": 0.45
          }
        }
        """;
        File.WriteAllText(Path.Combine(configDirectory, "pipeline.json"), pipelineJson);
        File.WriteAllBytes(Path.Combine(modelsDirectory, "anoma.onnx"), new byte[] { 1, 2, 3, 4 });
        File.WriteAllBytes(Path.Combine(modelsDirectory, "yolo.onnx"), new byte[] { 5, 6, 7, 8 });

        var config = JsonSerializer.Deserialize<PipelinePackageConfig>(
            pipelineJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("test pipeline config deserialization failed");
        InferenceContextInfo context = InferenceContextFactory.Create(packageRoot, config);

        AssertContract(context.status == "recorded", "context status");
        AssertContract(context.context_id.StartsWith("ctx_", StringComparison.Ordinal), "context id");
        AssertContract(context.package_fingerprint.Length == 64, "package fingerprint");
        AssertContract(context.pipeline_sha256.Length == 64, "pipeline hash");
        AssertContract(context.anoma?.model_sha256.Length == 64, "anoma model hash");
        AssertContract(context.yolo?.model_sha256.Length == 64, "yolo model hash");
        AssertContract(Math.Abs((context.anoma?.score_threshold ?? 0) - 12.527f) < 0.0001f, "anoma threshold");
        AssertContract(Math.Abs((context.yolo?.confidence_threshold ?? 0) - 0.25f) < 0.0001f, "yolo threshold");

        string manifestPath = Path.Combine(testRoot, "manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(new
        {
            schema_version = 3,
            batch_type = "inference",
            batch_id = "contract_test",
            created_at = DateTime.UtcNow.ToString("O"),
            inference_context = context,
            items = new[]
            {
                new { id = "sample", processed_image = "images/sample.bmp", raw_image = "raw/sample.bmp", infer_json = "inference/sample.infer.json" }
            }
        }));

        string inferPath = Path.Combine(testRoot, "sample.infer.json");
        File.WriteAllText(inferPath, JsonSerializer.Serialize(new
        {
            schema_version = 2,
            image_id = "sample",
            inference_context_id = context.context_id,
            image_size = new { w = 640, h = 640 },
            yolo = new
            {
                executed = true,
                confidence_threshold = context.yolo?.confidence_threshold,
                model_sha256 = context.yolo?.model_sha256,
                detections = Array.Empty<object>()
            },
            anoma = new
            {
                executed = true,
                score = 14.0,
                score_threshold = context.anoma?.score_threshold,
                model_sha256 = context.anoma?.model_sha256,
                decision = "anomaly"
            },
            final = new { is_defect = true, reason = new[] { "stage1_abnormal" } }
        }));

        ManifestDto manifest = InferenceBatchSchemaParser.ParseManifest(manifestPath);
        InferResultDto infer = InferenceBatchSchemaParser.ParseInferResult(inferPath);
        AssertContract(manifest.InferenceContext?.ContextId == context.context_id, "manifest context parse");
        AssertContract(infer.InferenceContextId == context.context_id, "infer context parse");
        AssertContract(Math.Abs((infer.Anoma.ScoreThreshold ?? 0) - 12.527) < 0.0001, "infer threshold parse");

        string legacyManifestPath = Path.Combine(testRoot, "legacy-manifest.json");
        File.WriteAllText(legacyManifestPath, JsonSerializer.Serialize(new
        {
            schema_version = 2,
            batch_type = "inference",
            batch_id = "legacy_contract_test",
            created_at = DateTime.UtcNow.ToString("O"),
            items = new[] { new { id = "legacy", processed_image = "images/legacy.bmp" } }
        }));
        string legacyInferPath = Path.Combine(testRoot, "legacy.infer.json");
        File.WriteAllText(legacyInferPath, JsonSerializer.Serialize(new
        {
            schema_version = 1,
            image_id = "legacy",
            image_size = new { w = 640, h = 640 },
            yolo = new { detections = Array.Empty<object>() },
            anoma = new { score = 10.0, decision = "normal" },
            final = new { is_defect = false, reason = Array.Empty<string>() }
        }));
        AssertContract(InferenceBatchSchemaParser.ParseManifest(legacyManifestPath).SchemaVersion == 2, "legacy manifest compatibility");
        AssertContract(InferenceBatchSchemaParser.ParseInferResult(legacyInferPath).SchemaVersion == 1, "legacy infer compatibility");

        Console.WriteLine("SELF-TEST PASS: inference context, schema contracts, and legacy compatibility");
    }
    finally
    {
        if (Directory.Exists(testRoot))
            Directory.Delete(testRoot, recursive: true);
    }
}

static void AssertContract(bool condition, string name)
{
    if (!condition)
        throw new InvalidDataException("contract assertion failed: " + name);
}

static List<string> ReadManifestImagePaths(string batchRoot, string manifestPath)
{
    JsonNode root = JsonNode.Parse(File.ReadAllText(manifestPath))
                    ?? throw new InvalidDataException("manifest is empty");
    JsonArray items = root["items"]?.AsArray()
                      ?? throw new InvalidDataException("manifest.items missing");
    var result = new List<string>();
    string normalizedRoot = Path.GetFullPath(batchRoot)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

    foreach (JsonNode? item in items)
    {
        string relative = item?["processed_image"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(relative))
            throw new InvalidDataException("processed_image is empty");

        string fullPath = Path.GetFullPath(Path.Combine(batchRoot, relative));
        if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("processed_image escapes batch root: " + relative);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("processed image missing", fullPath);
        result.Add(fullPath);
    }

    return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
}

static void UpdateLegacyManifest(string manifestPath)
{
    JsonObject root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
                      ?? throw new InvalidDataException("manifest is empty");
    int currentSchema = root["schema_version"]?.GetValue<int>() ?? 0;
    string currentStatus = root["inference_context"]?["status"]?.GetValue<string>() ?? "";
    int currentReviewSchema = root["meta"]?["review_schema_version"]?.GetValue<int>() ?? 0;
    if (currentSchema >= 3 &&
        currentReviewSchema == ReviewState.CurrentSchemaVersion &&
        string.Equals(currentStatus, "not_applicable", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    string backupPath = Path.Combine(
        Path.GetDirectoryName(manifestPath)!,
        "manifest.v2.backup.json");
    if (!File.Exists(backupPath))
        File.Copy(manifestPath, backupPath, overwrite: false);

    root["schema_version"] = 3;
    if (string.IsNullOrWhiteSpace(root["batch_type"]?.GetValue<string>()))
        root["batch_type"] = "no_infer";
    root["inference_context"] = new JsonObject
    {
        ["status"] = "not_applicable",
        ["reason"] = "manual_seed_no_inference"
    };

    JsonObject meta;
    if (root["meta"] is JsonObject existingMeta)
        meta = existingMeta;
    else
    {
        meta = new JsonObject();
        root["meta"] = meta;
    }
    meta["review_schema_version"] = ReviewState.CurrentSchemaVersion;
    meta["legacy_review_migrated_at_utc"] = DateTime.UtcNow.ToString("O");

    File.WriteAllText(
        manifestPath,
        root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static int GetCount(Dictionary<ImageReviewDecision, int> counts, ImageReviewDecision decision)
    => counts.TryGetValue(decision, out int value) ? value : 0;

static PreviewResult PreviewLegacyConversions(
    IEnumerable<string> imagePaths,
    bool trustedManualSeedNormal)
{
    var result = new PreviewResult();
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    foreach (string imagePath in imagePaths)
    {
        string legacyPath = ImageStateService.GetStatePath(imagePath);
        if (!File.Exists(legacyPath))
            continue;

        try
        {
            ImageStateDto legacy = JsonSerializer.Deserialize<ImageStateDto>(
                File.ReadAllText(legacyPath), options) ?? throw new InvalidDataException("legacy state is empty");
            LegacyReviewConversion conversion = LegacyReviewConverter.Convert(legacy, legacyPath);
            if (trustedManualSeedNormal &&
                conversion.State.Decision == ImageReviewDecision.Unreviewed &&
                legacy.IsNormal == true &&
                (legacy.Labels?.Count ?? 0) == 0)
            {
                conversion.State.Decision = ImageReviewDecision.ConfirmedNormal;
                result.SeedConfirmed++;
            }
            if (conversion.IsAmbiguous)
                result.Ambiguous++;

            switch (conversion.State.Decision)
            {
                case ImageReviewDecision.ConfirmedNormal:
                    result.ConfirmedNormal++;
                    break;
                case ImageReviewDecision.ConfirmedDefect:
                    result.ConfirmedDefect++;
                    break;
                case ImageReviewDecision.Reviewing:
                    result.Reviewing++;
                    break;
                default:
                    result.Unreviewed++;
                    break;
            }
        }
        catch
        {
            result.Failed++;
        }
    }
    return result;
}

static bool IsTrustedManualSeedNormalBatch(string manifestPath, IEnumerable<string> imagePaths)
{
    try
    {
        JsonObject root = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject()
                          ?? throw new InvalidDataException("manifest is empty");
        string batchId = root["batch_id"]?.GetValue<string>() ?? "";
        string preprocessId = root["meta"]?["preprocess_id"]?.GetValue<string>() ?? "";
        if (!batchId.Contains("정상", StringComparison.Ordinal) ||
            !string.Equals(preprocessId, "manual_seed", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (string imagePath in imagePaths)
        {
            string statePath = ImageStateService.GetStatePath(imagePath);
            ImageStateDto state = JsonSerializer.Deserialize<ImageStateDto>(
                File.ReadAllText(statePath), options) ?? throw new InvalidDataException("legacy state is empty");
            if (state.IsNormal != true || (state.Labels?.Count ?? 0) != 0)
                return false;
        }
        return true;
    }
    catch
    {
        return false;
    }
}

static int PromoteTrustedManualSeedNormals(
    IEnumerable<string> imagePaths,
    ReviewRepository repository)
{
    int promoted = 0;
    foreach (string imagePath in imagePaths)
    {
        ReviewStateLoadResult load = repository.Load(imagePath);
        if (!load.HasReviewFile || load.ParseFailed ||
            load.State.Decision != ImageReviewDecision.Unreviewed)
        {
            continue;
        }

        ReviewState next = load.State.Clone();
        next.Decision = ImageReviewDecision.ConfirmedNormal;
        next.DecisionSource = ReviewDecisionSource.LegacyManual;
        next.BoxReview = BoxReviewDecision.NotApplicable;
        next.Boxes.Clear();
        next.DecisionConfirmedAtUtc = next.UpdatedAtUtc;
        next.Migration ??= new ReviewMigrationMetadata();
        if (!next.Migration.Notes.Contains("manual_seed_batch_normal", StringComparer.OrdinalIgnoreCase))
            next.Migration.Notes.Add("manual_seed_batch_normal");
        repository.Save(imagePath, next);
        promoted++;
    }
    return promoted;
}

sealed class PreviewResult
{
    public int ConfirmedNormal { get; set; }
    public int ConfirmedDefect { get; set; }
    public int Reviewing { get; set; }
    public int Unreviewed { get; set; }
    public int Ambiguous { get; set; }
    public int SeedConfirmed { get; set; }
    public int Failed { get; set; }
}
