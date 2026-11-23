namespace ScheduleReader;

public static class AppPaths
{
    public static string AppDirectory
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            return string.IsNullOrWhiteSpace(dir)
                ? Directory.GetCurrentDirectory()
                : Path.GetFullPath(dir);
        }
    }

    public static string TempSessionsDirectory =>
        Path.Combine(Path.GetTempPath(), "schedule_reader_xlsx");
}
