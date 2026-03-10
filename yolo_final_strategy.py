import argparse
import os
import json
import glob
from ultralytics import YOLO

def convert_json_to_yolo(workspace):
    print("🔄 [PREPROCESS] Searching for .bmp and .json files...")
    # .bmp 파일을 찾도록 수정 (하위 폴더 모두 탐색)
    image_paths = glob.glob(os.path.join(workspace, "**", "*.bmp"), recursive=True)
    print(f"📸 Found {len(image_paths)} images.")
    
    count = 0
    for img_path in image_paths:
        # .json 파일 경로 매칭 (파일명 끝이 .masked.state.json 인 경우 대응)
        json_path = img_path.replace(".bmp", ".state.json")
        txt_path = img_path.replace(".bmp", ".txt")
        
        if os.path.exists(json_path):
            with open(json_path, 'r') as f:
                data = json.load(f)
            
            # JSON 내의 'IsNormal' 값 읽기
            class_id = 0 if data.get("IsNormal", True) else 1
            with open(txt_path, 'w') as f:
                f.write(f"{class_id} 0.5 0.5 1.0 1.0\n")
            count += 1
    print(f"✅ Successfully converted {count} labels.")

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--workspace", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()

    print("🚀 [STRATEGY] TOTAL DEEP LEARNING SYSTEM START")
    os.makedirs(args.out, exist_ok=True)

    # 1. 데이터 전처리 (BMP -> TXT 변환)
    convert_json_to_yolo(args.workspace)

    # 2. 모델 로드 및 학습
    model = YOLO('yolov8m.pt') 
    model.train(
        data=os.path.join(args.workspace, "data.yaml"),
        epochs=50,
        imgsz=640, # BMP 파일 크기를 고려하여 조정
        batch=16,  # 메모리 안전을 위해 조정
        device=0,
        project=args.out,
        name="coil_final_run"
    )

if __name__ == "__main__":
    main()