using System.IO;
using System.Text.Json;

namespace CoilTrainingUI.Services
{
    public class AnomalyStateService
    {
        public void Save(string imagePath, bool isNormal)
        {
            string path = Path.ChangeExtension(imagePath, ".anomaly.json");

            var obj = new
            {
                IsNormal = isNormal
            };

            string json = JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(path, json);
        }

        public bool Load(string imagePath)
        {
            string path = Path.ChangeExtension(imagePath, ".anomaly.json");

            if (!File.Exists(path))
                return true; // 기본값: 정상

            try
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);

                return doc.RootElement
                          .GetProperty("IsNormal")
                          .GetBoolean();
            }
            catch
            {
                // 파일이 깨졌어도 UI는 살아야 한다
                return true;
            }
        }
    }
}
