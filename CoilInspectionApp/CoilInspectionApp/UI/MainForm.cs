using CoilInspectionApp.Watcher;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CoilInspectionApp.UI
{
    public partial class MainForm : Form
    {
        private ImageWatcher _imageWatcher;
        public MainForm()
        {
            InitializeComponent();

            string watchDir = @"C:\Users\wnsgh\Desktop\input";

            _imageWatcher = new ImageWatcher(watchDir);
            _imageWatcher.ImageCreated += OnImageCreated;
            _imageWatcher.Start();
        }

        private void OnImageCreated(string imagePath)
        {
            Invoke(new Action(() =>
            {
                Text = $"New image detected: {Path.GetFileName(imagePath)}";
            }));
        }
    }
}
