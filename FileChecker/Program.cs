using System.Text.Json;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Diagnostics;

// Finder/단축키 실행 시 cwd가 $HOME 이 되어 wwwroot를 못 찾는 문제 방지
static string ResolveAppRoot()
{
	static bool HasWwwRoot(string? dir) =>
		!string.IsNullOrWhiteSpace(dir) && Directory.Exists(Path.Combine(dir, "wwwroot"));

	var processDir = Path.GetDirectoryName(Environment.ProcessPath);
	if (HasWwwRoot(processDir)) return processDir!;

	var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
	if (HasWwwRoot(baseDir)) return baseDir;

	var cwd = Directory.GetCurrentDirectory();
	if (HasWwwRoot(cwd)) return cwd;

	return processDir ?? baseDir ?? cwd;
}

static bool TryOpenFileCheckerViaBroadcastNasBridge()
{
	try
	{
		using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1.5) };
		using var res = client.GetAsync("http://127.0.0.1:17820/api/status").GetAwaiter().GetResult();
		if (!res.IsSuccessStatusCode) return false;
		var json = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
		if (!json.Contains("BroadcastNasBridge", StringComparison.Ordinal) &&
		    !json.Contains("\"port\":17820", StringComparison.Ordinal))
			return false;

		const string url = "http://127.0.0.1:17820/files/";
		if (OperatingSystem.IsMacOS())
			Process.Start("open", url);
		else
			Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
		return true;
	}
	catch
	{
		return false;
	}
}

var appRoot = ResolveAppRoot();
Directory.SetCurrentDirectory(appRoot);

// 나스브릿지가 켜져 있으면 단독 5187 마운트 대신 /files 로 연다 (Mac 중복 마운트 방지)
if (TryOpenFileCheckerViaBroadcastNasBridge())
	return;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
	Args = args,
	ContentRootPath = appRoot,
	WebRootPath = Path.Combine(appRoot, "wwwroot"),
});
builder.Services.AddSingleton<FileCheckerStore>();
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/schedules", async (bool scan, FileCheckerStore store, CancellationToken cancellationToken) =>
	Results.Ok(await store.GetSchedulesAsync(scan, cancellationToken)));

app.MapPost("/api/schedules", async (ScheduleInput input, FileCheckerStore store, CancellationToken cancellationToken) =>
{
	if (input.Date == default || string.IsNullOrWhiteSpace(input.Description))
		return Results.BadRequest(new { message = "날짜와 설명은 필수입니다." });

	return Results.Ok(await store.AddScheduleAsync(input, cancellationToken));
});

app.MapDelete("/api/schedules/{id:guid}", async (Guid id, FileCheckerStore store, CancellationToken cancellationToken) =>
	await store.DeleteScheduleAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());

app.MapGet("/api/schedules/export", async (FileCheckerStore store, CancellationToken cancellationToken) =>
	Results.File(await store.ExportSchedulesAsync(cancellationToken), "application/json", $"filechecker-schedules-{DateTime.Now:yyyyMMdd-HHmmss}.json"));

app.MapPost("/api/schedules/import", async (IFormFile file, FileCheckerStore store, CancellationToken cancellationToken) =>
{
	if (file.Length == 0) return Results.BadRequest(new { message = "가져올 JSON 파일이 비어 있습니다." });
	try
	{
		await using var stream = file.OpenReadStream();
		var count = await store.ImportSchedulesAsync(stream, cancellationToken);
		return Results.Ok(new { count });
	}
	catch (JsonException) { return Results.BadRequest(new { message = "올바른 FileCheckerFinder JSON 파일이 아닙니다." }); }
});

app.MapGet("/api/settings", async (FileCheckerStore store, CancellationToken cancellationToken) =>
	Results.Ok(await store.GetSettingsAsync(cancellationToken)));

app.MapPut("/api/settings", async (Settings settings, FileCheckerStore store, CancellationToken cancellationToken) =>
	Results.Ok(await store.SaveSettingsAsync(settings, cancellationToken)));

app.MapPost("/api/settings/nas-test", async (Settings settings, FileCheckerStore store, CancellationToken cancellationToken) =>
	Results.Ok(await store.TestNasAsync(settings, cancellationToken)));

app.MapPost("/api/schedules/sync-from-schedule-data", async (FileCheckerStore store, CancellationToken cancellationToken) =>
{
	try
	{
		var count = await store.SyncFromScheduleDataAsync(cancellationToken);
		return Results.Ok(new { count, message = $"Recording 일정 {count}개를 가져왔습니다." });
	}
	catch (Exception ex)
	{
		return Results.BadRequest(new { message = ex.Message });
	}
});

app.MapPost("/api/settings/select-local", async () =>
{
	if (!OperatingSystem.IsWindows())
		return Results.BadRequest(new { message = "macOS에서는 로컬 경로를 직접 입력하세요." });

	var result = await Task.Run(() =>
	{
		using var process = Process.Start(new ProcessStartInfo
		{
			FileName = "powershell",
			ArgumentList =
			{
				"-NoProfile",
				"-STA",
				"-Command",
				"Add-Type -AssemblyName System.Windows.Forms; $d = New-Object System.Windows.Forms.FolderBrowserDialog; $d.Description = '영상 렌더링 완료 폴더를 선택하세요.'; if ($d.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) { [Console]::Out.Write($d.SelectedPath) }"
			},
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		});
		if (process is null) return null;
		var output = process.StandardOutput.ReadToEnd().Trim();
		process.WaitForExit(120_000);
		return string.IsNullOrWhiteSpace(output) ? null : output;
	});
	return result is not null ? Results.Ok(new { path = result }) : Results.NoContent();
});

app.MapPost("/api/files/open-location", (OpenLocationRequest request) =>
{
	if (string.IsNullOrWhiteSpace(request.Path) || (!File.Exists(request.Path) && !Directory.Exists(request.Path)))
		return Results.NotFound(new { message = "파일 또는 폴더 경로를 찾을 수 없습니다." });

	if (OperatingSystem.IsMacOS())
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "open",
			UseShellExecute = false,
		};
		if (File.Exists(request.Path))
		{
			startInfo.ArgumentList.Add("-R");
			startInfo.ArgumentList.Add(request.Path);
		}
		else
		{
			startInfo.ArgumentList.Add(request.Path);
		}
		Process.Start(startInfo);
		return Results.Ok();
	}

	if (OperatingSystem.IsWindows())
	{
		var startInfo = new ProcessStartInfo
		{
			FileName = "explorer.exe",
			UseShellExecute = true
		};
		if (File.Exists(request.Path))
		{
			startInfo.ArgumentList.Add("/select,");
			startInfo.ArgumentList.Add(request.Path);
		}
		else
		{
			startInfo.ArgumentList.Add(request.Path);
		}
		Process.Start(startInfo);
		return Results.Ok();
	}

	return Results.BadRequest(new { message = "이 운영체제에서는 위치 열기를 지원하지 않습니다." });
});

var store = app.Services.GetRequiredService<FileCheckerStore>();
var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
lifetime.ApplicationStarted.Register(() =>
{
	_ = Task.Run(async () =>
	{
		await Task.Delay(600);
		try
		{
			if (OperatingSystem.IsMacOS())
				Process.Start("open", "http://127.0.0.1:5187/");
			else
				Process.Start(new ProcessStartInfo("http://127.0.0.1:5187/") { UseShellExecute = true });
		}
		catch { /* ignore */ }
	});
});
lifetime.ApplicationStopping.Register(() =>
{
	try { store.ReleaseMacMount(); }
	catch { /* ignore */ }
});

app.Run("http://localhost:5187");

public sealed record ScheduleInput(
	DateOnly Date,
	string Description,
	string? Memo,
	bool HasStartTime,
	TimeOnly? StartTime,
	bool HasInstructor,
	string? Instructor,
	bool SpecialSong,
	bool YouTube,
	string? Place = null);

public sealed record Schedule(
	Guid Id,
	DateOnly Date,
	string Description,
	string? Memo,
	bool HasStartTime,
	TimeOnly? StartTime,
	bool HasInstructor,
	string? Instructor,
	bool SpecialSong,
	bool YouTube,
	bool LocalAudioFound,
	bool LocalVideoFound,
	bool LocalHighQualityFound,
	bool NasAudioFound,
	bool NasVideoFound,
	bool NasHighQualityFound,
	string? LocalAudioPath,
	string? LocalVideoPath,
	string? LocalHighQualityPath,
	string? NasAudioPath,
	string? NasVideoPath,
	string? NasHighQualityPath,
	string BaseFileName,
	string? Place = null);

public sealed record StoredSchedule(Guid Id, ScheduleInput Input);

public sealed record Settings(
	string LocalPath,
	string NasIp,
	string NasUsername,
	string NasPassword,
	string NasSearchPath,
	string? ScheduleJsonPath = null);

public sealed record NasTestResult(bool Success, string Message, string Path);
public sealed record OpenLocationRequest(string Path);

public sealed class FileCheckerStore
{
	public const string DefaultNasSearchPath = "Permanent DATA\\H264_mp4 DATA";
	private readonly string _dataDirectory;
	private readonly string _schedulesFile;
	private readonly string _settingsFile;
	private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
	private readonly SemaphoreSlim _lock = new(1, 1);
	private readonly Dictionary<string, FileIndex> _fileIndexes = new(StringComparer.OrdinalIgnoreCase);
	private static readonly TimeSpan FileIndexLifetime = TimeSpan.FromMinutes(5);

	private string? _macMountPoint;
	private string? _macMountKey;
	/// <summary>앱이 mount_smbfs 로 만든 마운트면 true. /Volumes Finder 마운트면 false.</summary>
	private bool _macMountOwned;

	public FileCheckerStore(IWebHostEnvironment environment)
	{
		_dataDirectory = Path.Combine(environment.ContentRootPath, "data");
		_schedulesFile = Path.Combine(_dataDirectory, "schedules.json");
		_settingsFile = Path.Combine(_dataDirectory, "settings.json");
	}

	public void ReleaseMacMount()
	{
		if (string.IsNullOrWhiteSpace(_macMountPoint)) return;
		if (_macMountOwned)
		{
			TryUnmount(_macMountPoint, force: true);
			TryDeleteDirectory(_macMountPoint);
		}
		_macMountPoint = null;
		_macMountKey = null;
		_macMountOwned = false;
	}

	public async Task<IReadOnlyList<Schedule>> GetSchedulesAsync(bool forceScan, CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var schedules = await ReadAsync<List<StoredSchedule>>(_schedulesFile, cancellationToken) ?? [];
			var settings = await ReadAsync<Settings>(_settingsFile, cancellationToken) ?? DefaultSettings();
			var targetDates = schedules.Select(item => item.Input.Date).ToHashSet();
			var localFiles = GetFileIndex(settings.LocalPath, targetDates, forceScan);
			var nasFiles = GetFileIndex(ResolveNasScanRoot(settings), targetDates, forceScan);
			return schedules
				.Select(item => ToSchedule(item.Id, item.Input, localFiles, nasFiles))
				.OrderByDescending(schedule => schedule.Date)
				.ThenBy(schedule => schedule.StartTime)
				.ToList();
		}
		finally { _lock.Release(); }
	}

	public async Task<Schedule> AddScheduleAsync(ScheduleInput input, CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var schedules = await ReadAsync<List<StoredSchedule>>(_schedulesFile, cancellationToken) ?? [];
			var id = Guid.NewGuid();
			schedules.Add(new StoredSchedule(id, input));
			await WriteAsync(_schedulesFile, schedules, cancellationToken);
			var settings = await ReadAsync<Settings>(_settingsFile, cancellationToken) ?? DefaultSettings();
			var targetDates = new[] { input.Date }.ToHashSet();
			return ToSchedule(id, input, GetFileIndex(settings.LocalPath, targetDates, false), GetFileIndex(ResolveNasScanRoot(settings), targetDates, false));
		}
		finally { _lock.Release(); }
	}

	public async Task<bool> DeleteScheduleAsync(Guid id, CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var schedules = await ReadAsync<List<StoredSchedule>>(_schedulesFile, cancellationToken) ?? [];
			var removed = schedules.RemoveAll(item => item.Id == id);
			if (removed == 0) return false;
			await WriteAsync(_schedulesFile, schedules, cancellationToken);
			return true;
		}
		finally { _lock.Release(); }
	}

	public async Task<byte[]> ExportSchedulesAsync(CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var schedules = await ReadAsync<List<StoredSchedule>>(_schedulesFile, cancellationToken) ?? [];
			return JsonSerializer.SerializeToUtf8Bytes(schedules, _jsonOptions);
		}
		finally { _lock.Release(); }
	}

	public async Task<int> ImportSchedulesAsync(Stream stream, CancellationToken cancellationToken)
	{
		var schedules = await JsonSerializer.DeserializeAsync<List<StoredSchedule>>(stream, _jsonOptions, cancellationToken)
			?? throw new JsonException("스케줄 데이터가 없습니다.");
		if (schedules.Any(item => item.Id == Guid.Empty || item.Input is null || item.Input.Date == default || string.IsNullOrWhiteSpace(item.Input.Description)))
			throw new JsonException("필수 스케줄 정보가 없습니다.");

		await _lock.WaitAsync(cancellationToken);
		try
		{
			await WriteAsync(_schedulesFile, schedules, cancellationToken);
			_fileIndexes.Clear();
			return schedules.Count;
		}
		finally { _lock.Release(); }
	}

	public async Task<Settings> GetSettingsAsync(CancellationToken cancellationToken) =>
		WithDefaults(await ReadAsync<Settings>(_settingsFile, cancellationToken) ?? DefaultSettings());

	public async Task<Settings> SaveSettingsAsync(Settings settings, CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var normalized = WithDefaults(settings);
			await WriteAsync(_settingsFile, normalized, cancellationToken);
			return normalized;
		}
		finally { _lock.Release(); }
	}

	private static Settings WithDefaults(Settings settings) =>
		string.IsNullOrWhiteSpace(settings.NasSearchPath)
			? settings with { NasSearchPath = DefaultNasSearchPath }
			: settings;

	public Task<NasTestResult> TestNasAsync(Settings settings, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (!IPAddress.TryParse(settings.NasIp, out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
			return Task.FromResult(new NasTestResult(false, "NAS IP 주소를 올바른 IPv4 형식으로 입력하세요.", string.Empty));

		if (OperatingSystem.IsMacOS())
			return Task.FromResult(TestNasMac(settings));

		var path = BuildNasPath(settings);
		if (string.IsNullOrWhiteSpace(path))
			return Task.FromResult(new NasTestResult(false, "NAS IP 주소와 검색 경로를 입력하세요.", path));
		try
		{
			var connectResult = ConnectToShare(BuildNasShareRoot(path), settings.NasUsername, settings.NasPassword);
			// 1219/85/2404: 다른 앱이 이미 같은 NAS에 연결됨 → 기존 세션으로 경로만 확인 (세션 끊지 않음)
			if (connectResult != 0 && connectResult is not (1219 or 85 or 2404))
				return Task.FromResult(new NasTestResult(false, DescribeNasConnectError(connectResult), path));

			var check = Task.Run(() =>
			{
				try
				{
					if (!Directory.Exists(path)) return false;
					_ = Directory.GetFileSystemEntries(path);
					return true;
				}
				catch
				{
					return false;
				}
			}, cancellationToken);
			if (!check.Wait(TimeSpan.FromSeconds(5)))
				return Task.FromResult(new NasTestResult(false, "NAS 응답 시간이 초과되었습니다. IP와 네트워크 연결을 확인하세요.", path));
			var reachable = check.Result;
			if (reachable && connectResult is 1219 or 85 or 2404)
			{
				return Task.FromResult(new NasTestResult(true,
					"NAS 경로에 접근할 수 있습니다. (이미 연결된 Windows SMB 세션 재사용 — 형제 앱과 동일 계정 권장)", path));
			}
			return Task.FromResult(new NasTestResult(reachable,
				reachable ? "NAS 경로에 접근할 수 있습니다. (Windows 공유 권한 사용)" : DescribeNasUnreachable(connectResult), path));
		}
		catch (UnauthorizedAccessException)
		{
			return Task.FromResult(new NasTestResult(false, "NAS 경로는 있지만 접근 권한이 없습니다.", path));
		}
		catch (IOException exception)
		{
			return Task.FromResult(new NasTestResult(false, $"NAS 접속 중 오류가 발생했습니다: {exception.Message}", path));
		}
	}

	private NasTestResult TestNasMac(Settings settings)
	{
		var path = string.Empty;
		try
		{
			path = ResolveNasScanRoot(settings, forceRemount: true);
			if (string.IsNullOrWhiteSpace(path))
				return new NasTestResult(false,
					"NAS IP 주소와 검색 경로를 입력하세요. 권장: BroadcastNasBridge(17820) 연결 후 /files 사용.",
					path);

			if (!Directory.Exists(path))
				return new NasTestResult(false,
					"NAS 경로를 찾을 수 없습니다. 검색 경로는 공유폴더부터 입력하세요. Finder smb 연결·로컬 네트워크 권한도 확인하세요.",
					path);

			_ = Directory.GetFileSystemEntries(path);
			return new NasTestResult(true, $"NAS 경로에 접근할 수 있습니다. (macOS · {path})", path);
		}
		catch (UnauthorizedAccessException)
		{
			return new NasTestResult(false, "NAS 경로는 있지만 접근 권한이 없습니다.", path);
		}
		catch (Exception exception)
		{
			return new NasTestResult(false, $"NAS 접속 중 오류가 발생했습니다: {exception.Message}", path);
		}
	}

	private static string DescribeNasConnectError(int code) => code switch
	{
		1326 or 86 => "NAS 사용자 이름 또는 비밀번호가 올바르지 않습니다.",
		53 => "네트워크 경로를 찾을 수 없습니다. NAS IP와 네트워크 연결을 확인하세요.",
		67 => "공유 폴더 이름을 찾을 수 없습니다. 검색 경로는 공유폴더부터 입력하세요.",
		_ => $"NAS 인증에 실패했습니다. Windows 오류 코드: {code}",
	};

	private static string DescribeNasUnreachable(int connectResult) => connectResult switch
	{
		1219 or 85 or 2404 =>
			"같은 NAS에 다른 계정으로 이미 연결되어 있어 이 경로에 접근할 수 없습니다. ScheduleDataManager·WorkLog·탐색기와 같은 NAS 계정을 사용하세요.",
		_ => "NAS 경로를 찾을 수 없습니다. 검색 경로는 공유폴더부터 입력하세요.",
	};

	private static int ConnectToShare(string path, string? username, string? password)
	{
		if (!OperatingSystem.IsWindows()) return 0;
		if (string.IsNullOrWhiteSpace(username)) return 0;
		var resource = new NetResource { ResourceType = 1, RemoteName = path };
		return WNetAddConnection2(ref resource, password ?? string.Empty, username, 0);
	}

	private static string BuildNasShareRoot(string path)
	{
		var parts = path.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
		return parts.Length >= 2 ? $"\\\\{parts[0]}\\{parts[1]}" : path;
	}

	[DllImport("mpr.dll", CharSet = CharSet.Unicode)]
	private static extern int WNetAddConnection2(ref NetResource netResource, string password, string username, int flags);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NetResource
	{
		public int Scope;
		public int ResourceType;
		public int DisplayType;
		public int Usage;
		public string? LocalName;
		public string? RemoteName;
		public string? Comment;
		public string? Provider;
	}

	private Schedule ToSchedule(Guid id, ScheduleInput input, FileIndex localFiles, FileIndex nasFiles)
	{
		var identity = string.Join(' ', input.Date.ToString("yyyyMMdd"), input.Description, input.Instructor);
		var localAudioPath = FindFile(localFiles.Files, identity, FileKind.Audio);
		var localVideoPath = FindFile(localFiles.Files, identity, FileKind.Video);
		var localHighQualityPath = FindFile(localFiles.Files, identity, FileKind.HighQuality);
		var nasAudioPath = FindFile(nasFiles.Files, identity, FileKind.Audio);
		var nasVideoPath = FindFile(nasFiles.Files, identity, FileKind.Video);
		var nasHighQualityPath = FindFile(nasFiles.Files, identity, FileKind.HighQuality);
		return new Schedule(id, input.Date, input.Description, input.Memo, input.HasStartTime,
			input.StartTime, input.HasInstructor, input.Instructor, input.SpecialSong, input.YouTube,
			localAudioPath is not null, localVideoPath is not null, localHighQualityPath is not null,
			nasAudioPath is not null, nasVideoPath is not null, nasHighQualityPath is not null,
			localAudioPath, localVideoPath, localHighQualityPath, nasAudioPath, nasVideoPath, nasHighQualityPath,
			BuildBaseFileName(input),
			input.Place);
	}

	private static string BuildBaseFileName(ScheduleInput input)
	{
		var parts = new List<string> { input.Description };
		if (!string.IsNullOrWhiteSpace(input.Memo)) parts.Add(input.Memo.Trim());
		parts.Add(input.Date.ToString("yyyyMMdd"));
		if (input.HasInstructor && !string.IsNullOrWhiteSpace(input.Instructor)) parts.Add(input.Instructor.Trim());
		return string.Join('_', parts).Replace('/', '_').Replace('\\', '_');
	}

	private FileIndex GetFileIndex(string? root, IReadOnlySet<DateOnly> targetDates, bool forceScan)
	{
		if (string.IsNullOrWhiteSpace(root)) return FileIndex.Empty;
		var cacheKey = $"{root}|{string.Join(',', targetDates.OrderBy(date => date).Select(date => date.ToString("yyyyMMdd")))}";
		if (!forceScan && _fileIndexes.TryGetValue(cacheKey, out var cached) && DateTime.UtcNow - cached.CreatedAt < FileIndexLifetime)
			return cached;
		try
		{
			var files = Directory.Exists(root)
				? EnumerateCandidateFiles(root, targetDates).ToArray()
				: [];
			var index = new FileIndex(DateTime.UtcNow, files);
			_fileIndexes[cacheKey] = index;
			return index;
		}
		catch (IOException) { return FileIndex.Empty; }
		catch (UnauthorizedAccessException) { return FileIndex.Empty; }
	}

	private static IEnumerable<string> EnumerateCandidateFiles(string root, IReadOnlySet<DateOnly> targetDates)
	{
		var targetYears = targetDates.Select(date => date.Year).ToHashSet();
		foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.TopDirectoryOnly))
			yield return file;

		foreach (var directory in Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly))
		{
			var folderName = Path.GetFileName(directory);
			var folderDate = ParseFolderDate(folderName);
			var folderYear = ParseFolderYear(folderName);
			if (folderDate is not null && !targetDates.Contains(folderDate.Value))
				continue;
			if (folderDate is null && folderYear is not null && !targetYears.Contains(folderYear.Value))
				continue;

			foreach (var file in EnumerateCandidateFiles(directory, targetDates))
				yield return file;
		}
	}

	private static DateOnly? ParseFolderDate(string folderName)
	{
		var match = Regex.Match(folderName, @"(?<!\d)(20\d{2}(0[1-9]|1[0-2])(0[1-9]|[12]\d|3[01]))(?!\d)");
		return match.Success && DateOnly.TryParseExact(match.Groups[1].Value, "yyyyMMdd", out var date) ? date : null;
	}

	private static int? ParseFolderYear(string folderName)
	{
		var match = Regex.Match(folderName, @"(?<!\d)(20\d{2})(?!\d)");
		return match.Success && int.TryParse(match.Groups[1].Value, out var year) ? year : null;
	}

	private static string? FindFile(IReadOnlyList<string> files, string query, FileKind kind)
	{
		var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		var hit = files.FirstOrDefault(file => IsExpectedFile(file, terms, kind));
		if (hit is not null || kind != FileKind.HighQuality)
			return hit;

		// 일반 화질과 같은 이름 + _숫자M 접미 고화질 보완 검색
		var normalVideo = files.FirstOrDefault(file => IsExpectedFile(file, terms, FileKind.Video));
		if (normalVideo is null) return null;
		var baseName = Path.GetFileNameWithoutExtension(normalVideo);
		return files.FirstOrDefault(file =>
		{
			if (!Path.GetExtension(file).Equals(".mp4", StringComparison.OrdinalIgnoreCase))
				return false;
			var name = Path.GetFileNameWithoutExtension(file);
			if (!Regex.IsMatch(name, @"_\d{1,4}M$", RegexOptions.IgnoreCase))
				return false;
			var stripped = Regex.Replace(name, @"_\d{1,4}M$", string.Empty, RegexOptions.IgnoreCase);
			return string.Equals(stripped, baseName, StringComparison.OrdinalIgnoreCase);
		});
	}

	private static bool IsExpectedFile(string file, string[] terms, FileKind kind)
	{
		var fileName = Path.GetFileName(file);
		var normalizedFileName = NormalizeSearchText(fileName);
		var extension = Path.GetExtension(fileName);
		var highQuality = Regex.IsMatch(Path.GetFileNameWithoutExtension(fileName), @"_\d{1,4}M$", RegexOptions.IgnoreCase);
		var extensionMatches = kind == FileKind.Audio ? extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) : extension.Equals(".mp4", StringComparison.OrdinalIgnoreCase);
		var qualityMatches = kind == FileKind.HighQuality ? highQuality : kind == FileKind.Video && !highQuality;
		return extensionMatches && qualityMatches && terms.All(term => normalizedFileName.Contains(NormalizeSearchText(term), StringComparison.OrdinalIgnoreCase));
	}

	private static string NormalizeSearchText(string value) =>
		string.Concat(value.Where(character => !char.IsWhiteSpace(character) && character != '_')).ToUpperInvariant();

	private enum FileKind { Audio, Video, HighQuality }

	private sealed record FileIndex(DateTime CreatedAt, IReadOnlyList<string> Files)
	{
		public static FileIndex Empty { get; } = new(DateTime.MinValue, []);
	}

	private string ResolveNasScanRoot(Settings settings, bool forceRemount = false)
	{
		if (OperatingSystem.IsMacOS())
			return ResolveMacNasScanRoot(settings, forceRemount);

		var path = BuildNasPath(settings);
		if (string.IsNullOrWhiteSpace(path)) return string.Empty;
		if (!string.IsNullOrWhiteSpace(settings.NasUsername))
			ConnectToShare(BuildNasShareRoot(path), settings.NasUsername, settings.NasPassword);
		return path;
	}

	private string ResolveMacNasScanRoot(Settings settings, bool forceRemount)
	{
		if (string.IsNullOrWhiteSpace(settings.NasIp)) return string.Empty;

		var ip = settings.NasIp.Trim();
		var search = NormalizeSearchSeparators(settings.NasSearchPath).Trim('/');
		if (search.StartsWith(ip + "/", StringComparison.OrdinalIgnoreCase) ||
		    search.Equals(ip, StringComparison.OrdinalIgnoreCase))
		{
			search = search.Length > ip.Length ? search[ip.Length..].Trim('/') : string.Empty;
		}

		var parts = search.Split('/', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 0) return string.Empty;

		var shareName = parts[0];
		var subPath = parts.Length > 1
			? string.Join(Path.DirectorySeparatorChar, parts.Skip(1))
			: null;

		EnsureMacMount(ip, shareName, settings.NasUsername, settings.NasPassword, forceRemount);
		if (string.IsNullOrWhiteSpace(_macMountPoint)) return string.Empty;
		return string.IsNullOrEmpty(subPath) ? _macMountPoint : Path.Combine(_macMountPoint, subPath);
	}

	private void EnsureMacMount(string host, string shareName, string? username, string? password, bool forceRemount = false)
	{
		var key = $"{host}|{shareName}";
		var safeShare = string.Concat(shareName.Select(ch =>
			char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_'));

		if (!forceRemount)
		{
			var existing = FindExistingMacShareMount(host, shareName, safeShare);
			if (existing is not null)
			{
				_macMountPoint = existing;
				_macMountKey = key;
				_macMountOwned = false;
				return;
			}
		}

		if (!forceRemount &&
		    _macMountPoint is not null &&
		    string.Equals(_macMountKey, key, StringComparison.OrdinalIgnoreCase) &&
		    Directory.Exists(_macMountPoint))
		{
			return;
		}

		ReleaseMacMount();

		var volumesPath = Path.Combine("/Volumes", shareName);
		var mountPoint = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library",
			"Application Support",
			"FileChecker",
			"mnt",
			$"{host}_{safeShare}");

		PrepareEmptyMountPoint(mountPoint);

		var user = string.IsNullOrWhiteSpace(username) ? "guest" : username.Trim();
		var pass = password ?? string.Empty;

		Exception? lastError = null;
		foreach (var candidate in BuildMacUsernameCandidates(user, host))
		{
			try
			{
				MountSmbfs(host, shareName, candidate, pass, mountPoint);
				lastError = null;
				break;
			}
			catch (Exception ex)
			{
				lastError = ex;
				TryUnmount(mountPoint, force: true);
				PrepareEmptyMountPoint(mountPoint);
			}
		}

		if (lastError is not null)
		{
			var fallback = FindExistingMacShareMount(host, shareName, safeShare);
			if (fallback is not null)
			{
				_macMountPoint = fallback;
				_macMountKey = key;
				_macMountOwned = false;
				return;
			}

			TryDeleteDirectory(mountPoint);
			throw new InvalidOperationException(
				$"SMB 연결에 실패했습니다.\n대상: //{host}/{shareName}\n{lastError.Message}\n" +
				"· 공유명·계정·비밀번호를 확인하세요.\n" +
				"· 권장: BroadcastNasBridge(17820)를 먼저 연결한 뒤 파일체크를 여세요.\n" +
				"· macOS 시스템 설정 → 개인정보 보호 및 보안 → 로컬 네트워크에서 이 앱을 허용했는지 확인하세요.\n" +
				"· Finder에서 smb:// 로 해당 공유를 먼저 연결해 /Volumes 경로를 만들 수도 있습니다.");
		}

		_macMountPoint = mountPoint;
		_macMountKey = key;
		_macMountOwned = true;
	}

	private static string? FindExistingMacShareMount(string host, string shareName, string safeShare)
	{
		foreach (var candidate in new[]
		{
			Path.Combine("/Volumes", shareName),
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"Library", "Application Support", "BroadcastNasBridge", "mnt", $"{host}_{safeShare}"),
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"Library", "Application Support", "BroadcastingSchedule", "mnt", $"{host}_{safeShare}"),
			Path.Combine(
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				"Library", "Application Support", "FileChecker", "mnt", $"{host}_{safeShare}"),
		})
		{
			if (CanAccessMacDirectory(candidate)) return candidate;
		}

		foreach (var mounted in ListMacSmbfsMounts())
		{
			if (!MacMountedShareMatches(mounted.Source, host, shareName)) continue;
			if (CanAccessMacDirectory(mounted.Path)) return mounted.Path;
		}

		return null;
	}

	private static bool CanAccessMacDirectory(string path)
	{
		try
		{
			if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return false;
			_ = Directory.GetFileSystemEntries(path);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool MacMountedShareMatches(string source, string host, string shareName)
	{
		var s = source.Trim();
		if (s.StartsWith("smb:", StringComparison.OrdinalIgnoreCase)) s = s[4..];
		s = s.TrimStart('/');
		var at = s.LastIndexOf('@');
		var pathPart = at >= 0 ? s[(at + 1)..] : s;
		var slash = pathPart.IndexOf('/');
		if (slash < 0) return false;
		var mountedHost = pathPart[..slash];
		var mountedShareEnc = pathPart[(slash + 1)..].Split('/', 2)[0];
		string mountedShare;
		try { mountedShare = Uri.UnescapeDataString(mountedShareEnc); }
		catch { mountedShare = mountedShareEnc; }
		return mountedHost.Equals(host, StringComparison.OrdinalIgnoreCase) &&
		       mountedShare.Equals(shareName, StringComparison.OrdinalIgnoreCase);
	}

	private static List<(string Source, string Path)> ListMacSmbfsMounts()
	{
		var list = new List<(string, string)>();
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = "/sbin/mount",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			using var process = Process.Start(psi);
			if (process is null) return list;
			var output = process.StandardOutput.ReadToEnd();
			process.WaitForExit(5000);
			foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				if (!line.Contains("smbfs", StringComparison.OrdinalIgnoreCase)) continue;
				var onIdx = line.IndexOf(" on ", StringComparison.Ordinal);
				if (onIdx < 0) continue;
				var source = line[..onIdx].Trim();
				var rest = line[(onIdx + 4)..];
				var paren = rest.IndexOf(" (", StringComparison.Ordinal);
				var path = (paren >= 0 ? rest[..paren] : rest).Trim();
				if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(path))
					list.Add((source, path));
			}
		}
		catch { /* ignore */ }
		return list;
	}

	private static void PrepareEmptyMountPoint(string mountPoint)
	{
		TryUnmount(mountPoint, force: true);
		try
		{
			if (Directory.Exists(mountPoint))
			{
				foreach (var entry in Directory.EnumerateFileSystemEntries(mountPoint))
				{
					try
					{
						if (Directory.Exists(entry)) Directory.Delete(entry, true);
						else File.Delete(entry);
					}
					catch { /* ignore */ }
				}
				// 비어 있지 않거나 마운트 잔여면 폴더 자체를 지우고 다시 생성
				try { Directory.Delete(mountPoint, false); }
				catch
				{
					TryUnmount(mountPoint, force: true);
					try { Directory.Delete(mountPoint, true); } catch { /* ignore */ }
				}
			}
		}
		catch { /* ignore */ }

		Directory.CreateDirectory(Path.GetDirectoryName(mountPoint)!);
		Directory.CreateDirectory(mountPoint);
	}

	private static void MountSmbfs(string host, string shareName, string username, string password, string mountPoint)
	{
		var sources = new[]
		{
			BuildMacMountSource(host, shareName, username, password, smbScheme: false),
			BuildMacMountSource(host, shareName, username, password, smbScheme: true),
		};

		Exception? lastError = null;
		foreach (var source in sources.Distinct(StringComparer.Ordinal))
		{
			foreach (var nobrowse in new[] { true, false })
			{
				try
				{
					PrepareEmptyMountPoint(mountPoint);
					RunMountSmbfs(source, mountPoint, nobrowse);
					return;
				}
				catch (Exception ex)
				{
					lastError = ex;
					TryUnmount(mountPoint, force: true);
				}
			}
		}

		throw lastError ?? new InvalidOperationException("mount_smbfs 실패");
	}

	private static void RunMountSmbfs(string source, string mountPoint, bool nobrowse = true)
	{
		var psi = new ProcessStartInfo
		{
			FileName = "/sbin/mount_smbfs",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			CreateNoWindow = true,
		};
		if (nobrowse)
		{
			psi.ArgumentList.Add("-o");
			psi.ArgumentList.Add("nobrowse");
		}
		psi.ArgumentList.Add(source);
		psi.ArgumentList.Add(mountPoint);

		using var process = Process.Start(psi)
			?? throw new InvalidOperationException("mount_smbfs를 실행할 수 없습니다.");

		var stdout = process.StandardOutput.ReadToEnd();
		var stderr = process.StandardError.ReadToEnd();
		if (!process.WaitForExit(30000))
		{
			try { process.Kill(entireProcessTree: true); } catch { /* ignore */ }
			throw new TimeoutException("SMB 마운트 시간이 초과되었습니다.");
		}

		if (process.ExitCode != 0)
		{
			var detail = string.Join('\n', new[] { stderr, stdout }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
			throw new InvalidOperationException(string.IsNullOrWhiteSpace(detail)
				? $"mount_smbfs 실패 (코드 {process.ExitCode})"
				: detail);
		}
	}

	private static string BuildMacMountSource(
		string host,
		string shareName,
		string username,
		string password,
		bool smbScheme = false)
	{
		// mount_smbfs URL: //[[[domain;]user[:password]@]server[/share]
		var user = username.Trim();
		string auth;
		if (user.Contains('\\'))
		{
			var parts = user.Split('\\', 2);
			auth = $"{EscapeMountComponent(parts[0])};{EscapeMountComponent(parts[1])}:{EscapeMountComponent(password)}";
		}
		else if (user.Contains(';'))
		{
			var sep = user.IndexOf(';');
			auth = $"{EscapeMountComponent(user[..sep])};{EscapeMountComponent(user[(sep + 1)..])}:{EscapeMountComponent(password)}";
		}
		else
		{
			auth = $"{EscapeMountComponent(user)}:{EscapeMountComponent(password)}";
		}

		var encodedShare = string.Join('/',
			shareName.Replace('\\', '/')
				.Split('/', StringSplitOptions.RemoveEmptyEntries)
				.Select(EscapeMountComponent));

		var path = $"//{auth}@{host}/{encodedShare}";
		return smbScheme ? $"smb:{path}" : path;
	}

	private static string EscapeMountComponent(string value) =>
		Uri.EscapeDataString(value ?? string.Empty);

	private static IEnumerable<string> BuildMacUsernameCandidates(string username, string host)
	{
		var user = username.Trim();
		var set = new List<string>();
		void Add(string value)
		{
			if (!string.IsNullOrWhiteSpace(value) && !set.Contains(value, StringComparer.OrdinalIgnoreCase))
				set.Add(value);
		}

		Add(user);
		if (!user.Contains('\\') && !user.Contains(';') && !user.Contains('@'))
		{
			Add($"{host};{user}");
			Add($".;{user}");
		}
		return set;
	}

	private static void TryUnmount(string mountPoint, bool force = false)
	{
		if (string.IsNullOrWhiteSpace(mountPoint)) return;
		try
		{
			var psi = new ProcessStartInfo
			{
				FileName = "/sbin/umount",
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				CreateNoWindow = true,
			};
			if (force) psi.ArgumentList.Add("-f");
			psi.ArgumentList.Add(mountPoint);
			using var process = Process.Start(psi);
			process?.WaitForExit(10000);
		}
		catch
		{
			// ignore
		}
	}

	private static void TryDeleteDirectory(string path)
	{
		try
		{
			if (Directory.Exists(path))
				Directory.Delete(path, recursive: false);
		}
		catch
		{
			// ignore
		}
	}

	private static string NormalizeSearchSeparators(string? value) =>
		(value ?? string.Empty).Trim().Replace('\\', '/');

	private static string BuildNasPath(Settings settings)
	{
		if (string.IsNullOrWhiteSpace(settings.NasIp)) return string.Empty;
		var path = NormalizeSearchSeparators(settings.NasSearchPath).Replace('/', '\\').Trim('\\');
		if (path.StartsWith(settings.NasIp.Trim('\\'), StringComparison.OrdinalIgnoreCase))
			return $"\\\\{path}";
		return $"\\\\{settings.NasIp.Trim('\\')}\\{path}";
	}

	private static Settings DefaultSettings() => new("", "", "", "", DefaultNasSearchPath, "");

	public async Task<int> SyncFromScheduleDataAsync(CancellationToken cancellationToken)
	{
		await _lock.WaitAsync(cancellationToken);
		try
		{
			var settings = WithDefaults(await ReadAsync<Settings>(_settingsFile, cancellationToken) ?? DefaultSettings());
			var configured = settings.ScheduleJsonPath?.Trim() ?? "";
			if (string.IsNullOrWhiteSpace(configured))
				throw new InvalidOperationException("설정에서 스케줄 JSON 경로(ScheduleJsonPath)를 지정하세요. 예: \\\\NAS\\Temp DATA\\_data\\schedule-data.json");

			if (OperatingSystem.IsWindows())
			{
				if (!string.IsNullOrWhiteSpace(settings.NasUsername))
					ConnectToShare(BuildNasShareRoot(configured.StartsWith(@"\\") ? configured : BuildNasPath(settings)), settings.NasUsername, settings.NasPassword);
			}

			var pathLookup = configured;
			if (OperatingSystem.IsMacOS())
				pathLookup = ResolveScheduleJsonPathOnMac(configured, settings);

			var path = ResolveScheduleJsonPathLikeSdm(pathLookup)
				?? throw new FileNotFoundException($"스케줄 JSON을 찾을 수 없습니다: {configured}");

			// 실제 사용한 경로를 설정에 반영 (SDM과 동일 파일 추적)
			if (!string.Equals(settings.ScheduleJsonPath, path, StringComparison.OrdinalIgnoreCase))
			{
				settings = settings with { ScheduleJsonPath = path };
				await WriteAsync(_settingsFile, settings, cancellationToken);
			}

			await using var stream = File.OpenRead(path);
			using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
				throw new InvalidOperationException("스케줄 JSON은 배열이어야 합니다.");

			var imported = new List<StoredSchedule>();
			foreach (var item in document.RootElement.EnumerateArray())
			{
				if (!HasRecordingPreparation(item)) continue;
				var input = MapScheduleDataItem(item);
				if (input is null) continue;
				imported.Add(new StoredSchedule(Guid.NewGuid(), input));
			}

			await WriteAsync(_schedulesFile, imported, cancellationToken);
			_fileIndexes.Clear();
			return imported.Count;
		}
		finally { _lock.Release(); }
	}

	private string ResolveScheduleJsonPathOnMac(string path, Settings settings)
	{
		if (path.StartsWith('/'))
			return path;

		var unc = path.Replace('/', '\\');
		if (!unc.StartsWith(@"\\", StringComparison.Ordinal))
			return path;

		var parts = unc.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length < 2)
			throw new InvalidOperationException($"UNC 경로 형식이 올바르지 않습니다: {path}");

		var host = parts[0];
		var share = parts[1];
		var rest = parts.Length > 2
			? string.Join(Path.DirectorySeparatorChar, parts.Skip(2))
			: string.Empty;

		EnsureMacMount(host, share, settings.NasUsername, settings.NasPassword);
		if (string.IsNullOrWhiteSpace(_macMountPoint))
			throw new InvalidOperationException($"SMB 마운트에 실패했습니다: //{host}/{share}");

		return string.IsNullOrEmpty(rest) ? _macMountPoint : Path.Combine(_macMountPoint, rest);
	}

	/// <summary>ScheduleDataManager pickPreferredJsonFile 과 같은 우선순위로 JSON 선택.</summary>
	private static string? ResolveScheduleJsonPathLikeSdm(string configured)
	{
		if (File.Exists(configured) &&
		    configured.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
		    !Directory.Exists(configured))
			return configured;

		var folder = Directory.Exists(configured)
			? configured
			: Path.GetDirectoryName(configured);
		if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
			return null;

		var remembered = Path.GetFileName(configured);
		var files = Directory.EnumerateFiles(folder, "*.json")
			.Select(Path.GetFileName)
			.Where(name => name is not null && IsSdmScheduleJsonCandidate(name))
			.Cast<string>()
			.ToList();
		if (files.Count == 0) return null;

		if (!string.IsNullOrWhiteSpace(remembered) &&
		    remembered.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
		{
			var rememberedHit = files.FirstOrDefault(f =>
				f.Equals(remembered, StringComparison.OrdinalIgnoreCase));
			if (rememberedHit is not null)
				return Path.Combine(folder, rememberedHit);
		}

		string[] preferred =
		[
			"schedule-data.json",
			"scheduledata.json",
			"schedule.json",
		];
		foreach (var name in preferred)
		{
			var hit = files.FirstOrDefault(f => f.Equals(name, StringComparison.OrdinalIgnoreCase));
			if (hit is not null) return Path.Combine(folder, hit);
		}

		var soft = files.FirstOrDefault(f =>
			f.Contains("schedule", StringComparison.OrdinalIgnoreCase));
		if (soft is not null) return Path.Combine(folder, soft);

		var sorted = files
			.OrderBy(f => f, StringComparer.Create(new System.Globalization.CultureInfo("ko-KR"), ignoreCase: false))
			.First();
		return Path.Combine(folder, sorted);
	}

	private static bool IsSdmScheduleJsonCandidate(string name)
	{
		if (name.StartsWith('_')) return false;
		if (name.Contains("work_log", StringComparison.OrdinalIgnoreCase)) return false;
		if (name.Contains("backup", StringComparison.OrdinalIgnoreCase)) return false;
		if (name.Equals("official-documents.json", StringComparison.OrdinalIgnoreCase)) return false;
		if (name.Equals("links.json", StringComparison.OrdinalIgnoreCase)) return false;
		if (name.Equals("_shared_notice.json", StringComparison.OrdinalIgnoreCase)) return false;
		return true;
	}

	private static bool HasRecordingPreparation(JsonElement item)
	{
		if (!item.TryGetProperty("preparation", out var prep) || prep.ValueKind != JsonValueKind.Array)
			return false;
		foreach (var value in prep.EnumerateArray())
		{
			if (value.ValueKind == JsonValueKind.String &&
			    value.GetString()?.Equals("Recording", StringComparison.OrdinalIgnoreCase) == true)
				return true;
		}
		return false;
	}

	private static ScheduleInput? MapScheduleDataItem(JsonElement item)
	{
		var dateText = GetStringProp(item, "scheduleDate") ?? GetStringProp(item, "releaseDate");
		if (string.IsNullOrWhiteSpace(dateText) || !DateOnly.TryParse(dateText, out var date))
			return null;
		var title = GetStringProp(item, "scheduleTitle") ?? "";
		if (string.IsNullOrWhiteSpace(title)) return null;
		var speaker = GetStringProp(item, "sermonSpeaker");
		var memo = GetStringProp(item, "preaparationNote")
			?? GetStringProp(item, "preparationNote")
			?? GetStringProp(item, "accessoryInfo");
		TimeOnly? startTime = null;
		var hasStart = false;
		if (item.TryGetProperty("startTime", out var st))
		{
			if (st.ValueKind == JsonValueKind.Number && st.TryGetInt32(out var num) && num is >= 0 and <= 2359)
			{
				var hour = num / 100;
				var minute = num % 100;
				if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59)
				{
					startTime = new TimeOnly(hour, minute);
					hasStart = true;
				}
			}
		}
		return new ScheduleInput(
			date,
			title.Trim(),
			string.IsNullOrWhiteSpace(memo) ? null : memo.Trim(),
			hasStart,
			startTime,
			!string.IsNullOrWhiteSpace(speaker),
			string.IsNullOrWhiteSpace(speaker) ? null : speaker.Trim(),
			SpecialSong: HasPreparationFlag(item, "Praise"),
			YouTube: HasPreparationFlag(item, "Streaming"),
			Place: GetStringProp(item, "place"));
	}

	private static bool HasPreparationFlag(JsonElement item, string flag)
	{
		if (!item.TryGetProperty("preparation", out var prep) || prep.ValueKind != JsonValueKind.Array)
			return false;
		foreach (var value in prep.EnumerateArray())
		{
			if (value.ValueKind == JsonValueKind.String &&
			    value.GetString()?.Equals(flag, StringComparison.OrdinalIgnoreCase) == true)
				return true;
		}
		return false;
	}

	private static string? GetStringProp(JsonElement item, string name) =>
		item.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

	private async Task<T?> ReadAsync<T>(string path, CancellationToken cancellationToken)
	{
		if (!File.Exists(path)) return default;
		await using var stream = File.OpenRead(path);
		return await JsonSerializer.DeserializeAsync<T>(stream, _jsonOptions, cancellationToken);
	}

	private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(_dataDirectory);
		await using var stream = File.Create(path);
		await JsonSerializer.SerializeAsync(stream, value, _jsonOptions, cancellationToken);
	}
}
