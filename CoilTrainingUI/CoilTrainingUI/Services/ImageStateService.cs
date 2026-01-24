using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using CoilTrainingUI.Models;
using CoilTrainingUI.Services;

namespace CoilTrainingUI.Services
{
    public class ImageStateService
    {
        public ImageStateDto Load(string imagePath)
        {
            var path = GetStatePath(imagePath);
            if (!File.Exists(path))
                return new ImageStateDto(); // 기본값

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<ImageStateDto>(json);
                return data ?? new ImageStateDto();
            }
            catch
            {
                // 깨진 파일이어도 UI 죽이면 안 됨
                return new ImageStateDto();
            }
        }

        public void Save(string imagePath, ImageStateDto state)
        {
            var path = GetStatePath(imagePath);

            // ✅ 기본값 정책 고정: 설정 안 된 상태는 정상(true)
            if (state.IsNormal == null)
                state.IsNormal = true;

            state.UpdatedAt = DateTime.UtcNow;

            var json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        public bool HasState(string imagePath) => File.Exists(GetStatePath(imagePath));

        public static string GetStatePath(string imagePath)
            => Path.ChangeExtension(imagePath, ".state.json");
    }

    public class ImageStateDto
    {
        public string RoiType { get; set; } = CoilTrainingUI.Models.RoiType.None.ToString();
        public bool? IsNormal { get; set; } = null;

        // ✅ 라벨을 클래스 이름으로 저장
        public List<LabelDto> Labels { get; set; } = new();

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class LabelDto
    {
        public string ClassName { get; set; } = ""; // "dent" / "loose"
        public double X { get; set; }               // normalized
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

}
