# Mask ONNX development tools

These tools are for model export and parity verification on a development PC.
They are not copied to the deployed CoilInspectionApp output.

Use a dedicated Python 3.12 virtual environment. Do not install the pinned
`segmentation-models-pytorch` dependencies into the YOLO or Dinomaly training
environments because they require a different `timm` version.

```powershell
python -m venv .venv_mask_export
.\.venv_mask_export\Scripts\python.exe -m pip install -r .\mask-runtime\requirements_export.txt
```

Export and verify the checkpoint:

```powershell
.\.venv_mask_export\Scripts\python.exe .\mask-runtime\tools\export_mask_onnx.py `
  --checkpoint .\mask-runtime\models\coil_unetpp_effb4_scratch_v8_best.pt `
  --output <coil-ai>\outputs\mask\coil_unetpp_effb4_scratch_v8\mask.onnx `
  --verification-json <coil-ai>\outputs\mask\coil_unetpp_effb4_scratch_v8\export_verification.json
```

`mask.onnx` has the following deployment contract:

- input `images`: float32 `[N, 3, 512, 512]`, RGB with ImageNet normalization
- output `probability`: float32 `[N, 1, 512, 512]`, sigmoid already applied

Copy the verified file to `InferencePackage/models/mask.onnx`. The training UI
uses `MaskInfer.ModelPath` from `config/appsettings.json` as the canonical model
source when it builds a new inference package.
