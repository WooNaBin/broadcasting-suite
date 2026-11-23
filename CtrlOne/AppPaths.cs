namespace CtrlOne;

/// <summary>
/// 단일 파일(.exe) 배포 시 BaseDirectory는 임시 추출 폴더일 수 있으므로,
/// 실제 exe가 있는 폴더를 기준으로 설정 파일을 보관한다.
/// </summary>
public static class AppPaths
{
    public static string AppDirectory
    {
        get
        {
            var processPath = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(processPath))
            {
                var dir = Path.GetDirectoryName(processPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    return dir;
            }

            return AppContext.BaseDirectory;
        }
    }

    public static string DevicesJsonPath => Path.Combine(AppDirectory, "devices.json");
    public static string PresetsJsonPath => Path.Combine(AppDirectory, "presets.json");
}
