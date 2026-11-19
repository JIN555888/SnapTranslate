using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace SnapTranslate;

/// <summary>
/// Windows 의 글자 인식(OCR) 언어 기능을 설치한다.
/// 설치에는 관리자 권한이 필요하므로, 권한 상승 창(UAC)을 띄워 PowerShell 로 Windows 기능 추가 명령을 실행한다.
/// </summary>
internal static class OcrInstaller
{
    /// <summary>설치를 제공하는 언어. Check 는 이미 설치됐는지 확인할 때 쓰는 코드, Capability 는 Windows 기능 이름의 언어 부분</summary>
    public static readonly (string Check, string Capability, string Name)[] Languages =
    [
        ("zh-CN", "zh-CN", "중국어(간체)"),
        ("zh-TW", "zh-TW", "중국어(번체)"),
        ("ja", "ja-JP", "일본어"),
        ("ru", "ru-RU", "러시아어"),
        ("es", "es-ES", "스페인어"),
        ("fr", "fr-FR", "프랑스어"),
        ("de", "de-DE", "독일어"),
    ];

    /// <summary>
    /// 관리자 권한 PowerShell 창을 띄워 설치를 시작한다.
    /// 사용자가 권한 상승을 거절하면 null 을 돌려준다. 돌려준 프로세스가 끝나면 설치 창이 닫힌 것이다.
    /// </summary>
    public static Process? Start(string capability, string name)
    {
        // 작은따옴표 문자열 안에 넣으므로 이름에 작은따옴표가 없어야 한다(위 목록의 고정 값만 쓴다)
        string script =
            $"$n = 'Language.OCR~~~{capability}~0.0.1.0'\n" +
            $"Write-Host '{name} 글자 인식 기능을 설치합니다. 인터넷에서 내려받으므로 잠시 걸릴 수 있습니다...'\n" +
            "try {\n" +
            "  Add-WindowsCapability -Online -Name $n -ErrorAction Stop | Out-Null\n" +
            "  Write-Host '설치 완료. SnapTranslate 를 다시 시작하면 사용할 수 있습니다.' -ForegroundColor Green\n" +
            "} catch {\n" +
            "  Write-Host ('설치 실패: ' + $_.Exception.Message) -ForegroundColor Red\n" +
            "}\n" +
            "Read-Host '엔터 키를 누르면 이 창이 닫힙니다'\n";

        // 따옴표·한글이 깨지지 않게 명령을 UTF-16 base64 로 넘긴다
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var info = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}")
        {
            UseShellExecute = true,
            Verb = "runas",   // 관리자 권한으로 실행(UAC 확인 창이 뜬다)
        };
        try
        {
            return Process.Start(info);
        }
        catch (Win32Exception)
        {
            // 사용자가 권한 상승을 취소했다
            return null;
        }
    }
}
