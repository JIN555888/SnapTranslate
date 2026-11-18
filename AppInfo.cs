using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SnapTranslate;

/// <summary>버전·저작권·저장소 같은 앱 정보</summary>
internal static class AppInfo
{
    public const string Name = "SnapTranslate";
    public const string Description = "단축키로 화면 영역을 캡처해 글자를 인식(OCR)하고 번역합니다.";
    public const string Copyright = "Copyright © 2026 참발자";
    public const string License = "MIT License";

    /// <summary>
    /// GitHub 저장소("소유자/저장소 이름"). 업데이트 확인과 정보 화면의 링크에 쓴다.
    /// 비어 있으면 업데이트 확인을 하지 않는다.
    /// </summary>
    public const string Repo = "JIN555888/SnapTranslate";

    /// <summary>제작자 블로그. 정보 화면에 링크로 보인다</summary>
    public const string BlogUrl = "https://barja.tistory.com";

    public static string RepoUrl => Repo.Length == 0 ? "" : $"https://github.com/{Repo}";
    public static string ReleasesUrl => Repo.Length == 0 ? "" : $"https://github.com/{Repo}/releases/latest";

    /// <summary>실행 파일 버전(예: 1.0.0). csproj 의 Version 값</summary>
    public static Version Version { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);

    public static string VersionText => Version.ToString(3);

    /// <summary>
    /// .NET 런타임을 함께 담은 배포본(self-contained)인지.
    /// 런타임이 PC 에 설치된 공용 폴더(dotnet\shared)에서 로드됐으면 작은 배포본이다.
    /// </summary>
    public static bool IsSelfContained =>
        !RuntimeEnvironment.GetRuntimeDirectory().Contains(@"\shared\Microsoft.NETCore.App\", StringComparison.OrdinalIgnoreCase);

    /// <summary>주소를 기본 브라우저로 연다</summary>
    public static void OpenUrl(string url)
    {
        if (url.Length == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Name);
        }
    }
}

/// <summary>업데이트 확인 결과. DownloadUrl 은 이 설치본에 맞는 zip 주소(없으면 빈 문자열)</summary>
internal readonly record struct UpdateResult(bool Available, string LatestVersion, string Url, string Message, string DownloadUrl = "");

/// <summary>GitHub Releases 의 최신 버전을 확인하고, 내려받아 설치한 뒤 다시 시작한다</summary>
internal static class UpdateService
{
    private const string ExeName = "SnapTranslate.exe";
    private const string OldSuffix = ".old";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub API 는 User-Agent 가 없으면 거절한다. 개인 정보는 넣지 않는다
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SnapTranslate-update-check");
        return client;
    }

    public static async Task<UpdateResult> CheckAsync()
    {
        if (AppInfo.Repo.Length == 0)
            return new UpdateResult(false, "", "", "업데이트 주소가 아직 설정되지 않았습니다.");
        try
        {
            using var response = await Http.GetAsync($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest");
            if ((int)response.StatusCode == 404)
                return new UpdateResult(false, "", "", "아직 배포된 버전이 없습니다.");
            if (!response.IsSuccessStatusCode)
                return new UpdateResult(false, "", "", $"확인 실패: 서버 응답 {(int)response.StatusCode}");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            JsonElement root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            // 태그는 "v1.2.0" 또는 "1.2.0" 형식
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
                return new UpdateResult(false, tag, url, $"버전 형식을 읽을 수 없습니다: {tag}");

            var normalized = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            var current = new Version(AppInfo.Version.Major, AppInfo.Version.Minor, Math.Max(0, AppInfo.Version.Build));
            if (normalized <= current)
                return new UpdateResult(false, normalized.ToString(3), url, "최신 버전입니다.");

            return new UpdateResult(true, normalized.ToString(3), url.Length > 0 ? url : AppInfo.ReleasesUrl,
                $"새 버전 {normalized.ToString(3)} 이 있습니다.", FindDownloadUrl(root));
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, "", "", "확인 실패: " + ex.Message);
        }
    }

    /// <summary>릴리스의 첨부 파일 중 이 설치본과 같은 종류(런타임 포함 여부)의 zip 주소를 찾는다</summary>
    private static string FindDownloadUrl(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return "";
        string suffix = AppInfo.IsSelfContained ? "-win-x64-self-contained.zip" : "-win-x64.zip";
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            if (!name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            return asset.TryGetProperty("browser_download_url", out var d) ? d.GetString() ?? "" : "";
        }
        return "";
    }

    /// <summary>
    /// 새 버전 zip 을 내려받아 프로그램 폴더의 파일을 바꾼다. 다시 시작은 <see cref="Restart"/> 로 따로 한다.
    /// 실행 중인 파일은 덮어쓸 수 없지만 이름은 바꿀 수 있으므로, 기존 파일을 ".old" 로 바꾼 뒤 새 파일을 넣는다.
    /// config.json(개인 설정)은 건드리지 않는다.
    /// </summary>
    public static async Task InstallAsync(UpdateResult update, IProgress<string>? progress)
    {
        // 설정한 저장소의 릴리스 파일만 받는다
        if (!update.DownloadUrl.StartsWith($"https://github.com/{AppInfo.Repo}/releases/download/", StringComparison.Ordinal))
            throw new InvalidOperationException("이 설치본에 맞는 내려받기 파일을 찾지 못했습니다.");

        string work = Path.Combine(Path.GetTempPath(), "SnapTranslate-update", update.LatestVersion);
        if (Directory.Exists(work)) Directory.Delete(work, true);
        Directory.CreateDirectory(work);

        string zip = Path.Combine(work, "update.zip");
        await DownloadAsync(update.DownloadUrl, zip, progress);

        progress?.Report("설치하는 중…");
        string extracted = Path.Combine(work, "files");
        ZipFile.ExtractToDirectory(zip, extracted);
        if (!File.Exists(Path.Combine(extracted, ExeName)))
            throw new InvalidOperationException("내려받은 파일에 실행 파일이 없습니다.");

        string appDir = AppContext.BaseDirectory;
        var renamed = new List<(string Target, string Old)>();   // 실패하면 되돌리려고 기록한다
        var added = new List<string>();
        try
        {
            foreach (string source in Directory.GetFiles(extracted, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(extracted, source);
                if (relative.Equals("config.json", StringComparison.OrdinalIgnoreCase)) continue;

                string target = Path.Combine(appDir, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                {
                    string old = target + OldSuffix;
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(target, old);
                    renamed.Add((target, old));
                }
                File.Copy(source, target);
                added.Add(target);
            }
        }
        catch
        {
            // 중간에 실패하면(쓰기 권한 없음 등) 원래 파일로 되돌린다
            foreach (string file in added)
                try { File.Delete(file); } catch (Exception) { /* 되돌리기는 할 수 있는 만큼만 */ }
            foreach (var (target, old) in renamed)
                try { if (!File.Exists(target)) File.Move(old, target); } catch (Exception) { /* 되돌리기는 할 수 있는 만큼만 */ }
            throw;
        }
    }

    private static async Task DownloadAsync(string url, string path, IProgress<string>? progress)
    {
        progress?.Report("내려받는 중…");
        // 머리말만 받은 뒤 본문은 조금씩 읽는다(큰 파일이 시간 제한에 걸리지 않게)
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;

        await using Stream input = await response.Content.ReadAsStreamAsync();
        await using FileStream output = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int lastPercent = -1;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read));
            done += read;
            if (total is > 0)
            {
                int percent = (int)(done * 100 / total.Value);
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    progress?.Report($"내려받는 중… {percent}%");
                }
            }
        }
    }

    /// <summary>새 실행 파일을 띄우고 이 프로그램을 끝낸다. 새 프로그램은 이 프로그램이 끝날 때까지 기다렸다가 시작한다</summary>
    public static void Restart()
    {
        string exe = Path.Combine(AppContext.BaseDirectory, ExeName);
        Process.Start(new ProcessStartInfo(exe, $"--after-update {Environment.ProcessId}") { UseShellExecute = false });
        Application.Exit();
    }

    /// <summary>지난 업데이트 때 이름을 바꿔 둔 옛 파일(*.old)을 지운다</summary>
    public static void CleanupOldFiles()
    {
        try
        {
            foreach (string old in Directory.GetFiles(AppContext.BaseDirectory, "*" + OldSuffix, SearchOption.AllDirectories))
                try { File.Delete(old); } catch (Exception) { /* 아직 쓰이는 파일은 다음 실행 때 지운다 */ }
        }
        catch (Exception)
        {
            // 폴더를 읽지 못해도 실행에는 지장이 없다
        }
    }

    /// <summary>
    /// 새 버전이 있을 때 사용자에게 물어보고 설치·재시작까지 진행한다.
    /// 자동 설치에 실패하면 내려받기 화면을 열지 묻는다.
    /// </summary>
    public static async Task RunInteractiveAsync(IWin32Window? owner, UpdateResult update, Action<string> status)
    {
        if (!update.Available) return;
        if (update.DownloadUrl.Length == 0)
        {
            if (MessageBox.Show(owner, update.Message + "\n자동 설치용 파일을 찾지 못했습니다. 내려받기 화면을 열까요?", AppInfo.Name, MessageBoxButtons.YesNo) == DialogResult.Yes)
                AppInfo.OpenUrl(update.Url);
            return;
        }

        var answer = MessageBox.Show(owner,
            $"{update.Message}\n(현재 {AppInfo.VersionText})\n\n지금 업데이트할까요?\n내려받아 설치한 뒤 프로그램을 다시 시작합니다. 설정은 그대로 유지됩니다.",
            AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        try
        {
            await InstallAsync(update, new Progress<string>(status));
            status("다시 시작하는 중…");
            Restart();
        }
        catch (Exception ex)
        {
            status("업데이트 실패: " + ex.Message);
            if (MessageBox.Show(owner, "업데이트에 실패했습니다.\n" + ex.Message + "\n\n내려받기 화면을 열까요?", AppInfo.Name, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                AppInfo.OpenUrl(update.Url);
        }
    }
}
