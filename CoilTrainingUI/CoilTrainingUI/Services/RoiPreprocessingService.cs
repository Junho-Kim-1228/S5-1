using CoilTrainingUI.Models;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;
public class RoiPreprocessService
{
    private readonly string _roiDir;

    public RoiPreprocessService()
    {
        _roiDir = IOPath.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources",
            "ROI"
        );
    }

    public string GetProcessedPath(string originalPath)
    {
        string dir = IOPath.Combine(
            IOPath.GetDirectoryName(originalPath)!,
            "_roi_processed"
        );

        string fileName = IOPath.GetFileName(originalPath); // 확장자 유지 (원본이 bmp면 bmp)
        return IOPath.Combine(dir, fileName);              // ✅ 타입 무관 고정
    }

    public void DeleteProcessedImage(string originalPath)
    {
        var processedPath = GetProcessedPath(originalPath);
        if (File.Exists(processedPath))
            File.Delete(processedPath);
    }

    public BitmapSource GetOrCreateProcessedImage(string originalImagePath, RoiType roiType)
    {
        // None이면: 전처리 결과는 존재하면 삭제 + 원본 반환
        if (roiType == RoiType.None)
        {
            DeleteProcessedImage(originalImagePath);
            return LoadBitmap(originalImagePath);
        }

        string processedPath = GetProcessedPath(originalImagePath);

        // ✅ 타입이 바뀌었어도 우리는 "항상 새로 생성"을 강제하는 편이 안전합니다.
        // (가장 단순하고 버그가 안 남)
        DeleteProcessedImage(originalImagePath);

        var processed = ApplyRoi(originalImagePath, roiType);
        SaveBitmapBmp(processed, processedPath);

        return processed;
    }

    private BitmapSource ApplyRoi(string originalPath, RoiType roiType)
    {
        string roiPath = IOPath.Combine(_roiDir, $"roi_{roiType}.bmp");
        if (!File.Exists(roiPath))
            throw new FileNotFoundException($"ROI mask not found: {roiPath}");

        var original = LoadBitmap(originalPath);
        var mask = LoadBitmap(roiPath);

        if (original.PixelWidth != mask.PixelWidth || original.PixelHeight != mask.PixelHeight)
            throw new InvalidOperationException("ROI mask size mismatch");

        int width = original.PixelWidth;
        int height = original.PixelHeight;

        var originalPixels = new byte[width * height * 4];
        var maskPixels = new byte[width * height * 4];
        var outPixels = new byte[width * height * 4];

        original.CopyPixels(originalPixels, width * 4, 0);
        mask.CopyPixels(maskPixels, width * 4, 0);

        for (int i = 0; i < outPixels.Length; i += 4)
        {
            bool insideRoi = maskPixels[i] > 0;

            if (insideRoi)
            {
                outPixels[i + 0] = originalPixels[i + 0];
                outPixels[i + 1] = originalPixels[i + 1];
                outPixels[i + 2] = originalPixels[i + 2];
                outPixels[i + 3] = 255;
            }
            else
            {
                outPixels[i + 0] = 0;
                outPixels[i + 1] = 0;
                outPixels[i + 2] = 0;
                outPixels[i + 3] = 255;
            }
        }

        return BitmapSource.Create(
            width, height,
            original.DpiX, original.DpiY,
            PixelFormats.Bgra32,
            null, outPixels, width * 4
        );
    }

    private static BitmapSource LoadBitmap(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // ✅ 캐시 방지
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }

    private static void SaveBitmapBmp(BitmapSource bitmap, string path)
    {
        Directory.CreateDirectory(IOPath.GetDirectoryName(path)!);

        BitmapEncoder encoder = new BmpBitmapEncoder(); // ✅ bmp로 저장
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
    }
    public void EnsureProcessed(string originalImagePath, RoiType roiType)
    {
        if (roiType == RoiType.None)
        {
            DeleteProcessedImage(originalImagePath);
            return;
        }

        // 1이미지=1파일 고정이면: 항상 삭제 후 생성이 가장 안전
        DeleteProcessedImage(originalImagePath);

        var processed = ApplyRoi(originalImagePath, roiType);
        SaveBitmapBmp(processed, GetProcessedPath(originalImagePath));
    }

}
