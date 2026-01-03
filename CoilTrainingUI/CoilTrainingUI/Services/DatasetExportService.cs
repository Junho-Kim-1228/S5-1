using System;
using System.Collections.Generic;
using System.IO;
using CoilTrainingUI.Models;
using IOPath = System.IO.Path;

namespace CoilTrainingUI.Services
{
    public class DatasetExportService
    {
        public string ExportAnomalyDataset(
            IEnumerable<ImageItem> images,
            string projectRoot
        )
        {
            string baseDir = IOPath.Combine(projectRoot, "datasets", "anomaly");

            string trainDir = IOPath.Combine(baseDir, "train");
            string valDir = IOPath.Combine(baseDir, "val");
            string testDir = IOPath.Combine(baseDir, "test");

            Directory.CreateDirectory(trainDir);
            Directory.CreateDirectory(valDir);
            Directory.CreateDirectory(testDir);

            int normalIndex = 0;

            foreach (var item in images)
            {
                if (!File.Exists(item.FullPath))
                    continue;

                string fileName = IOPath.GetFileName(item.FullPath);
                string destPath;

                if (item.IsNormal)
                {
                    destPath = (normalIndex++ % 5 == 0)
                        ? IOPath.Combine(valDir, fileName)
                        : IOPath.Combine(trainDir, fileName);
                }
                else
                {
                    destPath = IOPath.Combine(testDir, fileName);
                }

                File.Copy(item.FullPath, destPath, overwrite: true);
            }

            return baseDir;
        }
    }
}
