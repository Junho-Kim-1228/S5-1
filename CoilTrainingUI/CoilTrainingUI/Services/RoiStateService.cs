using System;
using System.IO;
using System.Text.Json;
using CoilTrainingUI.Models;

namespace CoilTrainingUI.Services
{
    public class RoiStateService
    {
        public void Save(string imagePath, RoiType roiType)
        {
            string path = GetJsonPath(imagePath);

            var data = new RoiStateDto
            {
                RoiType = roiType.ToString()
            };

            string json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        public RoiType Load(string imagePath)
        {
            string path = GetJsonPath(imagePath);

            if (!File.Exists(path))
                return RoiType.None;

            try
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<RoiStateDto>(json);

                if (Enum.TryParse<RoiType>(data?.RoiType, out var roi))
                    return roi;
            }
            catch
            {
                // 깨져 있어도 UI 죽이면 안 됨
            }

            return RoiType.None;
        }

        public bool HasState(string imagePath)
        {
            return File.Exists(GetJsonPath(imagePath));
        }

        private static string GetJsonPath(string imagePath)
        {
            return Path.ChangeExtension(imagePath, ".roi.json");
        }

        private class RoiStateDto
        {
            public string RoiType { get; set; } = "";
        }
    }
}
