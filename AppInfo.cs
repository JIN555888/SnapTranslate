using System.Diagnostics;
using System.Reflection;
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

/// <summary>업데이트 확인 결과</summary>
internal readonly record struct UpdateResult(bool Available, string LatestVersion, string Url, string Message);

/// <summary>GitHub Releases 의 최신 버전을 읽어 현재 버전과 비교한다(받아서 설치하지는 않고 내려받기 화면을 연다)</summary>
internal static class UpdateService
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // GitHub API 는 User-Agent 가 없으면 거절한다. 개인 정보는 넣지 않는다
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SnapTranslate-update-check");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
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
            string tag = doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() ?? "" : "";
            // 태그는 "v1.2.0" 또는 "1.2.0" 형식
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? latest))
                return new UpdateResult(false, tag, url, $"버전 형식을 읽을 수 없습니다: {tag}");

            var normalized = new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build));
            var current = new Version(AppInfo.Version.Major, AppInfo.Version.Minor, Math.Max(0, AppInfo.Version.Build));
            return normalized > current
                ? new UpdateResult(true, normalized.ToString(3), url.Length > 0 ? url : AppInfo.ReleasesUrl, $"새 버전 {normalized.ToString(3)} 이 있습니다.")
                : new UpdateResult(false, normalized.ToString(3), url, "최신 버전입니다.");
        }
        catch (Exception ex)
        {
            return new UpdateResult(false, "", "", "확인 실패: " + ex.Message);
        }
    }
}
