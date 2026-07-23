namespace CoilTrainingUI.Models;

public sealed class TrainingImageInput
{
    public string ImagePath { get; set; } = "";
    public string InferJsonPath { get; set; } = "";
    public bool RequiresInfer { get; set; }
    public string ExpectedInferenceContextId { get; set; } = "";
    public string BatchKey { get; set; } = "";
    public string BatchRoot { get; set; } = "";
}
