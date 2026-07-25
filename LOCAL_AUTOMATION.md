# 로컬 배치·모델 자동화

추론 UI(`CoilInspectionApp`)와 학습 UI(`CoilTrainingUI`)는 서버 없이 같은 PC의 `ExchangeRoot`를 통해 배치와 모델 적용 요청을 교환한다.

## 설정

기본 `ExchangeRoot`는 `%LOCALAPPDATA%\CoilInspectionAutomation`이다. 기본 경로는 Windows 사용자별 경로이므로 두 앱을 같은 사용자 계정으로 실행해야 한다. 서로 다른 계정으로 실행한다면 두 계정 모두 읽기·쓰기 가능한 별도 로컬 폴더를 양쪽 앱에 동일하게 설정한다.

- 학습 UI: `Automation` 메뉴에서 자동화 ON/OFF, 경로 선택, 기본 경로 재설정, 즉시 동기화를 수행한다.
- 추론 UI: 하단 자동화 상태 표시줄을 우클릭해 같은 작업을 수행한다.
- 학습 UI 기본값은 `config/appsettings.json`의 `Automation` 절에서 지정할 수 있다.
- 사용자 변경은 각각 `%LOCALAPPDATA%\CoilTrainingUI\automation_settings.json`과 `%LOCALAPPDATA%\CoilInspectionApp\automation_settings.json`에 저장된다.

자동화 설정 필드:

- `Enabled`
- `ExchangeRoot`
- `AutoImportBatches`
- `AutoPublishModels`
- `AutoApplyApprovedModels`
- `ReconcileIntervalSeconds`

## 동작 흐름

1. 추론 UI는 자동화 ON/OFF와 관계없이 완성 배치를 `batches\outbox`에 남기고 마지막에 `meta\DONE.flag`를 기록한다. `ExchangeRoot`가 유일한 배치 출력 경로 설정이다.
2. 학습 UI는 Watcher를 깨우기 신호로만 사용하고, 시작 시·이벤트 디바운스 후·주기적으로 Reconcile한다.
3. 배치는 학습 라이브러리의 `_importing`에서 복사와 재검증을 마친 뒤 최종 폴더로 원자 이동한다. 가져오기 성공 또는 이미 가져온 동일 배치는 `batches\archive`로 이동하며, 실패·충돌 배치는 outbox에 유지한다.
4. 전체 Anoma → YOLO 학습 성공 시 모델은 `models\releases\<model-id>\InferencePackage`로 불변 발행된다. `release.json`은 패키지 밖인 `<model-id>\release.json`에 저장되며 패키지 해시 대상에 포함되지 않는다.
5. 모델 관리 창에서 사용자가 `운영 적용 요청`을 눌러야 `activation_request.json`이 만들어진다. pending 요청이 있으면 새 요청은 차단되며, `대기 요청 취소` 후 다시 요청할 수 있다.
6. 추론 UI는 현재 배치와 전처리·추론·마감 작업이 모두 비었을 때만 요청을 적용한다. 사용 중이면 pending 결과를 기록하고 배치 마감 후 다시 처리한다.
7. 새 패키지 해시·설정·필수 모델·ONNX 세션·새 exporter를 모두 준비하고 RuntimePathSettings 저장까지 성공한 뒤 기존 세션을 폐기한다. 실패 시 기존 모델을 유지한다.
8. 추론 UI의 `applied` 결과가 request/model/hash와 모두 일치할 때만 학습 UI 모델 레지스트리의 reference가 변경된다.

배치 가져오기, 모델 발행, 모델 적용은 `models\control\locks`의 프로세스 간 lock file로 중복 실행을 막는다. 자동화 데이터는 사용자 로컬 경로에만 생성되며 저장소에 포함하지 않는다.

`batches\outbox`는 아직 학습 UI가 처리하지 못한 완료 배치와 조사해야 할 실패·충돌 배치만 보관한다. `batches\archive`는 가져오기 완료 원본을 보관하며, 추론 UI 통계는 outbox와 archive를 함께 조회한다.

## 노트북 Smoke Test

자동화 흐름만 확인할 때는 운영 데이터와 모델을 사용하지 말고 저장소의 `.smoke` 아래에 별도 `ExchangeRoot`와 학습 라이브러리를 둔다. `CoilTrainingUI/config/appsettings.local.json`에서는 `Workspace`, `YoloTraining`, `AnomaTraining`, `YoloInfer`, `AnomaInfer`, `Automation` 절을 로컬 값으로 덮어쓸 수 있다.

권장 최소 설정은 Dinomaly base encoder, 입력 `224`, batch `1`, 5 steps, CPU와 YOLO 입력 `320`, batch `1`, epoch `1`, CPU다. 최소한 정상 이미지와 불량 이미지를 모두 포함한 테스트 배치를 사용한다. Smoke test에서 발행된 모델은 운영 `InferencePackage`에 적용하지 말고, 모델 발행과 적용 요청 생성까지만 확인하거나 복사한 테스트 패키지에서 적용한다.
