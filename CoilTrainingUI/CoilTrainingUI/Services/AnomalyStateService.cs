using CoilTrainingUI.Services;

namespace CoilTrainingUI.Services
{
    public class AnomalyStateService
    {
        private readonly ImageStateService _state = new();

        public void Save(string imagePath, bool isNormal)
        {
            var s = _state.Load(imagePath);

            // 상태 통합 파일에 저장 (수동으로 변경된 값이라는 표시를 남긴다)
            s.IsNormal = isNormal;
            s.HasManualAnomalyDecision = true;

            _state.Save(imagePath, s);
        }

        public bool Load(string imagePath)
        {
            var s = _state.Load(imagePath);

            // 기존 기본값 정책 유지: 파일 없거나 값 없으면 정상(true)
            if (s.IsNormal == null)
                return true;

            return s.IsNormal.Value;
        }

        public bool HasState(string imagePath) => _state.HasState(imagePath);
    }
}
