using System;
using System.IO;
using System.Linq;

namespace CoilInspectionApp.Watcher
{
    public class DirectoryWatcher
    {
        private FileSystemWatcher _watcher;
        private static readonly string[] SupportedExtensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        // 파일이 생성되었을 때 외부(Form1)로 알려주기 위한 이벤트
        public event Action<string> OnFileCreated;
        public event Action<string> OnFileDeleted;

        public void StartWatch(string path)
        {
            if (!Directory.Exists(path))
            {
                throw new DirectoryNotFoundException($"감시할 폴더가 없습니다: {path}");
            }

            _watcher = new FileSystemWatcher();
            _watcher.Path = path;

            // 어떤 파일을 감시할지 설정 (이미지 파일만)
            _watcher.Filter = "*.*";

            // 파일 생성 이벤트 연결
            _watcher.Created += (s, e) => {
                string extension = Path.GetExtension(e.FullPath) ?? "";
                if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    return;

                // 이벤트 발생 시 등록된 함수(OnFileCreated)를 실행
                OnFileCreated?.Invoke(e.FullPath);
            };

            _watcher.Deleted += (s, e) => {
                string extension = Path.GetExtension(e.FullPath) ?? "";
                if (!SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    return;

                OnFileDeleted?.Invoke(e.FullPath);
            };

            // 감시 시작
            _watcher.EnableRaisingEvents = true;
        }
    }
}
