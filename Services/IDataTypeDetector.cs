using ClipFlyout.Models;

namespace ClipFlyout.Services;

public interface IDataTypeDetector
{
    DetectionResult Detect(object clipboardData);
}
