# S5-1
한국공학대학교 컴퓨터공학과 종합 설계 S5-1팀

## CoilTrainingUI Standalone 실행

학습 UI는 WPF 코드 외에 Python 런타임과 AI 학습 스크립트를 같이 필요로 한다.
소스 저장소에서 `CoilTrainingUI`를 빌드하면 최신 `coil-ai` 학습 소스와 Mask 모델을
`CoilTrainingUI/coil-ai-runtime`으로 자동 동기화한다.

### 독립 배포 시 함께 전달해야 하는 폴더

`CoilTrainingUI` 아래에 다음 폴더가 같이 있어야 한다.

```text
CoilTrainingUI/
  config/
    appsettings.json
    appsettings.local.json   # 선택
  python_env/
    Scripts/
      python.exe
    ...
  coil-ai-runtime/
    anoma/
    common/
    scripts/
    ultralytics/
    yolo/
    assets/
      weights/
        yolov8n.pt
        yolov8l.pt
    requirements-train.txt
    sitecustomize.py
```

### Git에 포함되는 것

- `CoilTrainingUI` WPF 코드
- `CoilTrainingUI/config/appsettings.json`
- 런타임 자동 동기화 대상인 `coil-ai` 학습 소스와 Mask ONNX

### Git에 포함되지 않는 것

- `CoilTrainingUI/python_env/`
- `CoilTrainingUI/coil-ai-runtime/`
- `CoilTrainingUI/config/appsettings.local.json`

소스 체크아웃에서는 `git pull`, `git lfs pull`, `CoilTrainingUI` 빌드 순서로
`coil-ai-runtime`을 최신화한다. Python 가상환경은 Git에 포함되지 않으므로 최초 1회
별도로 구성해야 한다. 독립 배포본에는 빌드로 생성된 `coil-ai-runtime`과 Python
환경을 함께 포함한다.

### 기본 설정

`CoilTrainingUI/config/appsettings.json`

```json
{
  "PythonExe": "python_env\\Scripts\\python.exe",
  "AiProjectRoot": "coil-ai-runtime"
}
```

상대경로 기준은 `CoilTrainingUI` 루트다.

### 최소 확인 항목

아래 4개가 있으면 학습 UI가 AI 런타임을 찾을 수 있다.

- `CoilTrainingUI/python_env/Scripts/python.exe`
- `CoilTrainingUI/coil-ai-runtime/scripts/train_anoma.py`
- `CoilTrainingUI/coil-ai-runtime/scripts/train_yolo.py`
- `CoilTrainingUI/coil-ai-runtime/assets/weights/yolov8n.pt`
