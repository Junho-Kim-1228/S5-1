using System;
using System.IO;
using System.Text;

namespace CoilInspectionApp.Logging
{
    public class CsvLogger
    {
        private string _logPath = @"C:\InspectionTest\logs\inspection_log.csv";

        public void SaveResult(string fileName, string result, float score)
        {
            try
            {
                // 파일이 없으면 헤더 생성
                if (!File.Exists(_logPath))
                {
                    File.WriteAllText(_logPath, "DateTime,FileName,Result,Score\n", Encoding.UTF8);
                }

                // 결과 데이터 작성
                string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss},{fileName},{result},{score:F4}\n";
                File.AppendAllText(_logPath, line, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"로그 저장 실패: {ex.Message}");
            }
        }
    }
}