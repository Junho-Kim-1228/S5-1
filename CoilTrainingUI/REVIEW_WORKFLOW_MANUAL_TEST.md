# CoilTrainingUI 검수 코어 수동 테스트

## 사전 조건

- 테스트 전 대상 배치 폴더를 별도 복사해 두거나, 실제 데이터 중 소수의 테스트 배치를 사용한다.
- `manifest.json`과 `infer.json`은 AI 원본이므로 테스트 전후 해시 또는 수정 시간을 비교한다.
- 기존 `*.state.json`, `raw_data`, `datasets`, `TrainingBatches`는 삭제하지 않는다.
- 새 사용자 상태는 이미지 옆 `*.review.json`에 저장된다.

## 상태 저장과 복원

1. 아직 `*.review.json`이 없는 배치를 연다.
2. 이미지 선택, 필터 변경, 새로고침만 수행한다.
3. `*.review.json`과 `*.state.json`이 새로 생기지 않았고 AI 판정이 사용자 확정으로 바뀌지 않았는지 확인한다.
4. 한 이미지를 `Normal`로 확정하고 프로그램을 종료한 뒤 다시 실행한다.
5. `ConfirmedNormal`과 `decision_source=Manual`이 복원되는지 확인한다.
6. 다른 이미지를 `Abnormal`로 확정하고 재실행해 `ConfirmedDefect`가 유지되는지 확인한다.
7. AI 판정 수락을 사용한 이미지는 `decision_source=AcceptedAiPrediction`으로 기록되어 수동 확정과 구분되는지 확인한다.

## 박스 검수

1. Anoma 불량 이미지에서 `AI 박스 수락`을 누른다.
2. 박스를 이동하거나 클래스를 변경하고 재실행해 편집 결과가 유지되는지 확인한다.
3. 박스를 모두 삭제한 뒤 `박스 검수 완료`를 누른다.
4. 재실행 후 사용자 박스가 0개이고 `box_review=Confirmed`인지 확인한다.
5. AI 예측 표시 체크박스가 자동으로 켜지지 않으며, AI 박스가 사용자 확정 박스로 복원되지 않는지 확인한다.
6. 필요할 때만 `AI 예측 박스 표시`를 직접 켜서 파란 점선 보조 박스를 비교한다.

## 학습 포함 규칙과 요약

1. `ConfirmedNormal`은 Anoma 학습 가능으로 집계되는지 확인한다.
2. 같은 이미지의 `YOLO 정상 배경으로 사용`을 켰을 때만 YOLO 배경으로 선택되는지 확인한다.
3. `ConfirmedDefect + Confirmed box 1개 이상`만 YOLO 양성으로 집계되는지 확인한다.
4. `ConfirmedDefect + box 0개`는 Anoma 평가에는 포함되고 YOLO에는 포함되지 않는지 확인한다.
5. 위 항목이 `YOLO 박스 없는 불량 제외` 개수에 반영되는지 확인한다.
6. `Unreviewed`, `Reviewing`, `Excluded`가 모든 학습 입력에서 제외되는지 확인한다.
7. 각 검수 상태 필터의 표시 개수와 상단 요약 개수를 대조한다.

## 기존 상태 마이그레이션

1. `*.state.json`만 있는 테스트 배치를 열고 목록에 마이그레이션 필요가 표시되는지 확인한다.
2. 단순 로드만으로 `*.review.json`이나 백업이 생성되지 않는지 확인한다.
3. `Review > 기존 state.json 안전 마이그레이션`을 실행한다.
4. 변환 예정/성공/실패/모호 개수를 확인한다.
5. `*.state.v1.backup.json`과 `*.review.json`이 생성되고 원본 `*.state.json` 내용이 바뀌지 않았는지 확인한다.
6. 같은 마이그레이션을 다시 실행해 중복 파일이나 상태 손상이 없고 `이미 변환됨`으로 집계되는지 확인한다.
7. 정상 판정과 박스가 동시에 있거나 확정 여부가 불명확한 레거시 항목이 `Reviewing`으로 남는지 확인한다.

## 선택 배치 학습과 패키지

1. 배치 관리에서 둘 이상의 배치 중 하나만 선택해 학습을 실행한다.
2. 진행 로그의 `[DATASET]` 행에서 후보/Anoma/YOLO/박스 없는 불량 제외 개수를 확인한다.
3. 생성된 `staged_raw`와 `anoma_staged_raw`에 선택하지 않은 배치 키가 없는지 확인한다.
4. YOLO workspace의 `manifest.json`에서 `excluded_defect_without_boxes`를 확인한다.
5. 생성된 `inference_package/config/pipeline.json`에서 다음을 확인한다.
   - `pipeline.mode`가 `anoma_then_yolo`
   - `pipeline.stage1`이 `anoma`
   - `pipeline.stage2`가 `yolo`
   - `pipeline.skip_yolo_when_stage1_normal`이 `true`
   - 이전 `fusion` 섹션이 없음

## 추론 UI 계약 확인

`CoilInspectionApp`에서 Anoma 정상은 YOLO를 건너뛰고 최종 정상으로, Anoma 불량은 YOLO 검출 수와 무관하게 최종 불량으로 표시되는지 확인한다. 특히 Anoma 불량이면서 YOLO 0개인 샘플의 최종 결과가 불량이어야 한다.
