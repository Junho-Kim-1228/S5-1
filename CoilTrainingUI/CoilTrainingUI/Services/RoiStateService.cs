using System;
using System.IO;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Services
{
    public class RoiStateService
    {
        private readonly ImageStateService _state = new();

        public void Save(string imagePath, RoiType roiType)
        {
            var s = _state.Load(imagePath);
            s.RoiType = roiType.ToString();
            _state.Save(imagePath, s);
        }

        public RoiType Load(string imagePath)
        {
            var s = _state.Load(imagePath);

            if (Enum.TryParse<RoiType>(s.RoiType, ignoreCase: true, out var roi))
                return roi;

            return RoiType.None;
        }

        public bool HasState(string imagePath) => _state.HasState(imagePath);

        // (선택) 과거 .roi.json을 이미 많이 만들었다면 “1회 이관”용 메서드
        public void MigrateLegacyIfNeeded(string imagePath)
        {
            // state.json에 값이 이미 있으면 건드리지 않음
            var s = _state.Load(imagePath);
            if (Enum.TryParse<RoiType>(s.RoiType, out var existing) && existing != RoiType.None)
                return;

            // legacy 파일이 있으면 읽어서 state.json에 넣고 저장
            string legacy = Path.ChangeExtension(imagePath, ".roi.json");
            if (!File.Exists(legacy)) return;

            try
            {
                // 기존 RoiStateService 로직이 있었다면 여기서 그대로 parse해서 roiType 추출
                // 지금은 구조를 모르니, 당신 legacy 포맷이 { "RoiType": "A" } 라는 전제하에 예시:
                var json = File.ReadAllText(legacy);
                var key = "\"RoiType\"";
                var idx = json.IndexOf(key, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                {
                    // 아주 단순 파싱(가능하면 JsonSerializer로 DTO 만들어도 됨)
                    // 예: "RoiType": "A"
                    var q1 = json.IndexOf('"', idx + key.Length);
                    var q2 = json.IndexOf('"', q1 + 1);
                    var q3 = json.IndexOf('"', q2 + 1);
                    var q4 = json.IndexOf('"', q3 + 1);
                    if (q3 >= 0 && q4 > q3)
                    {
                        var roiStr = json.Substring(q3 + 1, q4 - q3 - 1);
                        if (Enum.TryParse<RoiType>(roiStr, ignoreCase: true, out var roi) && roi != RoiType.None)
                        {
                            s.RoiType = roi.ToString();
                            _state.Save(imagePath, s);
                        }
                    }
                }
            }
            catch { /* 실패해도 UI 죽이면 안 됨 */ }
        }
    }
}
