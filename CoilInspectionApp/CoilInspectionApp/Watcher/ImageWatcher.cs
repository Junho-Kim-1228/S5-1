using System;
using System.IO;

namespace CoilInspectionApp.Watcher
{
    public class ImageWatcher
    {
        private readonly FileSystemWatcher _watcher;

        public event Action<string> ImageCreated;

        public ImageWatcher(string watchPath)
        {
            _watcher = new FileSystemWatcher(watchPath)
            {
                Filter = "*.*",
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = false
            };

            _watcher.Created += OnCreated;
        }

        public void Start()
        {
            _watcher.EnableRaisingEvents = true;
        }

        public void Stop()
        {
            _watcher.EnableRaisingEvents = false;
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            if (!IsImageFile(e.FullPath))
                return;

            ImageCreated?.Invoke(e.FullPath);
        }

        private bool IsImageFile(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".png" || ext == ".bmp";
        }
    }
}
