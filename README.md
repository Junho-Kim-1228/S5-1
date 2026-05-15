# S5-1
한국공학대학교 컴퓨터공학과 종합 설계 S5-1팀

## CoilTrainingUI Standalone 실행

`git pull`만으로는 `CoilTrainingUI` 학습 기능이 바로 동작하지 않는다.  
학습 UI는 WPF 코드 외에 Python 런타임과 AI 학습 스크립트를 같이 필요로 한다.

### 추가로 함께 전달해야 하는 폴더

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

### Git에 포함되지 않는 것

- `CoilTrainingUI/python_env/`
- `CoilTrainingUI/coil-ai-runtime/`
- `CoilTrainingUI/config/appsettings.local.json`

즉 다른 사람이 `git pull`만 해서는 학습이 실행되지 않는다.  
위 3개 런타임 자산은 별도로 복사하거나 압축해서 같이 전달해야 한다.

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
