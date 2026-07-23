using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CoilInspectionApp.Interface;
using CoilInspectionApp.Preprocess;
using Newtonsoft.Json;

namespace CoilInspectionApp
{
    internal static class Program
    {
        /// <summary>
        /// 해당 애플리케이션의 주 진입점입니다.
        /// </summary>
        [STAThread]
        static int Main(string[] args)
        {
            if (args != null && args.Length > 0
                && string.Equals(args[0], "--mask-smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                return RunMaskSmokeTest(args);
            }
            if (args != null && args.Length > 0
                && string.Equals(args[0], "--package-smoke-test", StringComparison.OrdinalIgnoreCase))
            {
                return RunPackageSmokeTest(args);
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
            return 0;
        }

        private static int RunMaskSmokeTest(string[] args)
        {
            if (args.Length != 4)
                return 2;

            string packagePath = Path.GetFullPath(args[1]);
            string inputPath = Path.GetFullPath(args[2]);
            string outputPath = Path.GetFullPath(args[3]);
            try
            {
                string pipelinePath = Path.Combine(packagePath, "config", "pipeline.json");
                var config = JsonConvert.DeserializeObject<PipelinePackageConfig>(File.ReadAllText(pipelinePath));
                if (config?.mask == null || string.IsNullOrWhiteSpace(config.mask.model))
                    throw new InvalidOperationException("pipeline.json missing mask.model");

                string modelPath = Path.GetFullPath(Path.Combine(
                    packagePath,
                    config.mask.model.Replace('/', Path.DirectorySeparatorChar)));
                using (var runner = new MaskOnnxRunner(modelPath, config.mask))
                    runner.ProcessImage(inputPath, outputPath);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(outputPath + ".error.txt", ex.ToString());
                }
                catch
                {
                }
                return 1;
            }
        }

        private static int RunPackageSmokeTest(string[] args)
        {
            if (args.Length != 2)
                return 2;

            string packagePath = Path.GetFullPath(args[1]);
            string errorPath = Path.Combine(packagePath, "package_smoke_test.error.txt");
            try
            {
                string pipelinePath = Path.Combine(packagePath, "config", "pipeline.json");
                var config = JsonConvert.DeserializeObject<PipelinePackageConfig>(File.ReadAllText(pipelinePath));
                if (config == null
                    || !string.Equals(config.pipeline?.mode, "anoma_then_yolo", StringComparison.OrdinalIgnoreCase)
                    || config.pipeline.skip_yolo_when_stage1_normal != true
                    || config.mask == null
                    || config.anoma == null
                    || config.yolo == null)
                {
                    throw new InvalidOperationException("Inference package contract is invalid.");
                }

                string maskPath = ResolvePackageModelPath(packagePath, config.mask.model);
                string anomaPath = ResolvePackageModelPath(packagePath, config.anoma.model);
                string yoloPath = ResolvePackageModelPath(packagePath, config.yolo.model);
                using (var maskRunner = new MaskOnnxRunner(maskPath, config.mask))
                using (var tester = new OnnxModelTester())
                {
                    tester.LoadAnomaModel(anomaPath);
                    tester.LoadYoloModel(yoloPath);
                }

                if (File.Exists(errorPath))
                    File.Delete(errorPath);
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    File.WriteAllText(errorPath, ex.ToString());
                }
                catch
                {
                }
                return 1;
            }
        }

        private static string ResolvePackageModelPath(string packagePath, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException("Package model path is empty.");
            return Path.GetFullPath(Path.Combine(
                packagePath,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));
        }
    }
}
