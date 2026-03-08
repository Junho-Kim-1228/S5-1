using System;
using System.IO;
using System.Threading;

namespace CoilInspectionApp.Watcher
{
    public class DirectoryWatcher
    {
        private FileSystemWatcher _watcher;
        public event Action<string> OnFileCreated;

        public void StartWatch(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"감시할 폴더가 없습니다: {path}");
            }

            _watcher = new FileSystemWatcher();
            _watcher.Path = path;
            _watcher.Filter = "*.*"; // JPG, PNG 모두 감시 가능하도록 확장 

            _watcher.Created += (s, e) => {
                // [안정성 보완] 파일 쓰기가 완료될 때까지 최대 3초간 대기 (요구사항 8)
                if (WaitForFile(e.FullPath))
                {
                    OnFileCreated?.Invoke(e.FullPath);
                }
            };

            _watcher.EnableRaisingEvents = true;
        }

        // 파일 접근이 가능할 때까지 기다리는 함수
        private bool WaitForFile(string filePath)
        {
            int attempts = 0;
            while (attempts < 10) // 0.3초씩 10번, 총 3초 시도
            {
                try
                {
                    using (FileStream fs = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        return true; // 파일 열기 성공
                    }
                }
                catch (IOException)
                {
                    attempts++;
                    Thread.Sleep(300); // 0.3초 대기
                }
            }
            return false;
        }
    }
}