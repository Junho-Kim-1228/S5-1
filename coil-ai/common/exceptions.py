class CoilAIError(Exception):
    pass


class TrainingError(CoilAIError):
    pass


class WorkspaceValidationError(CoilAIError):
    pass


class ExportError(CoilAIError):
    pass
