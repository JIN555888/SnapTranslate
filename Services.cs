using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SnapTranslate;

internal readonly record struct OcrOutput(string Text, string Language);

internal readonly record struct TranslateOutput(string Text, string SourceLang, string TargetLang);

/// <summary>Windows 내장 OCR(Windows.Media.Ocr). 인터넷 없이 로컬에서 동작한다</summary>
internal static class OcrService
{
    public static async Task<OcrOutput> RecognizeAsync(Bitmap source, string[] preferredLanguages)
    {
        using var scaled = Upscale(source);
        using var software = await ToSoftwareBitmapAsync(scaled);

        var engines = CreateEngines(preferredLanguages);
        if (engines.Count == 0)
            throw new InvalidOperationException("사용할 수 있는 Windows OCR 언어가 없습니다. Windows 설정 > 시간 및 언어 > 언어에서 언어 팩(광학 문자 인식)을 설치하세요.");

        // 엔진이 하나면 그 결과를 그대로 쓴다
        if (engines.Count == 1)
        {
            var only = await engines[0].Engine.RecognizeAsync(software);
            return new OcrOutput(BuildText(only, engines[0].Tag), engines[0].Tag);
        }

        // 여러 엔진으로 모두 읽어 보고, "그 언어의 문자가 가장 많이 나온" 결과를 고른다.
        // (예: 일본어 글을 영어 엔진으로 읽으면 엉뚱한 알파벳이 조금 나오고, 일본어 엔진으로 읽으면 가나·한자가 많이 나온다)
        OcrOutput? best = null;
        double bestScore = 0;
        foreach (var (tag, engine) in engines)
        {
            var result = await engine.RecognizeAsync(software);
            string text = BuildText(result, tag);
            if (text.Length == 0) continue;
            double score = Score(text, tag);
            if (best == null || score > bestScore)
            {
                best = new OcrOutput(text, tag);
                bestScore = score;
            }
        }
        return best ?? new OcrOutput("", engines[0].Tag);
    }

    /// <summary>Windows 에 설치돼 있어 글자를 읽을 수 있는 언어 이름들</summary>
    public static string[] InstalledLanguageNames() =>
        OcrEngine.AvailableRecognizerLanguages.Select(l => l.DisplayName).ToArray();

    /// <summary>해당 언어의 OCR 이 Windows 에 설치돼 있는지</summary>
    public static bool IsAvailable(string language) => FindLanguage(language) != null;

    /// <summary>그 언어의 글을 읽을 수 있는지(전용 엔진이 없어도 알파벳을 쓰는 언어는 영어 엔진으로 읽는다)</summary>
    public static bool CanRead(string language) =>
        IsAvailable(language) || (ScriptOf(language) == Script.Latin && IsAvailable("en"));

    private static Language? FindLanguage(string want)
    {
        // 번역 언어 코드와 OCR 언어 태그가 다른 것(중국어·필리핀어)을 맞춘다
        if (want.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)) want = "zh-Hans";
        else if (want.Equals("zh-TW", StringComparison.OrdinalIgnoreCase)) want = "zh-Hant";
        else if (want.Equals("tl", StringComparison.OrdinalIgnoreCase)) want = "fil";
        // "en" 으로 적어도 설치된 "en-US" 를 찾도록 앞부분으로 맞춘다
        return OcrEngine.AvailableRecognizerLanguages.FirstOrDefault(l =>
            l.LanguageTag.Equals(want, StringComparison.OrdinalIgnoreCase) ||
            l.LanguageTag.StartsWith(want + "-", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 쓸 OCR 엔진을 만든다. preferred 가 비어 있으면(자동 감지) 설치된 모든 언어를 쓴다.
    /// 언어를 지정했는데 그 언어의 엔진이 없으면, 알파벳을 쓰는 언어(필리핀어·베트남어 등)는 영어 엔진으로 대신 읽는다.
    /// </summary>
    private static List<(string Tag, OcrEngine Engine)> CreateEngines(string[] preferred)
    {
        var list = new List<(string, OcrEngine)>();

        void Add(Language? language)
        {
            if (language == null || list.Any(e => e.Item1 == language.LanguageTag)) return;
            var engine = OcrEngine.TryCreateFromLanguage(language);
            if (engine != null) list.Add((language.LanguageTag, engine));
        }

        if (preferred.Length == 0)
        {
            // 점수가 같을 때 앞쪽이 이기므로 영어를 맨 앞에 둔다(알파벳 글은 영어 엔진이 기본)
            foreach (var language in OcrEngine.AvailableRecognizerLanguages.OrderBy(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 0 : 1))
                Add(language);
        }
        else
        {
            foreach (string want in preferred)
            {
                Language? match = FindLanguage(want);
                if (match == null && ScriptOf(want) == Script.Latin) match = FindLanguage("en");
                Add(match);
            }
        }

        if (list.Count == 0)
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine != null) list.Add((engine.RecognizerLanguage.LanguageTag, engine));
        }
        return list;
    }

    private enum Script { Latin, Hangul, Japanese, Chinese, Cyrillic, Thai }

    /// <summary>언어 태그(코드)가 쓰는 문자 종류</summary>
    private static Script ScriptOf(string tag)
    {
        string lang = tag.Split('-')[0].ToLowerInvariant();
        return lang switch
        {
            "ko" => Script.Hangul,
            "ja" => Script.Japanese,
            "zh" => Script.Chinese,
            "ru" or "uk" or "bg" or "be" or "mk" or "kk" => Script.Cyrillic,
            "th" => Script.Thai,
            _ => Script.Latin,
        };
    }

    /// <summary>
    /// 인식 결과가 그 엔진의 언어다운 정도. 그 언어의 문자 수를 세되,
    /// 알파벳이 아닌 문자에는 가중치를 준다(다른 문자를 영어 엔진으로 읽어 나온 엉뚱한 알파벳에 지지 않게).
    /// </summary>
    private static double Score(string text, string tag)
    {
        int hangul = 0, kana = 0, han = 0, cyrillic = 0, thai = 0, latin = 0, vietnamese = 0;
        foreach (char c in text)
        {
            if (c is >= '가' and <= '힣' or >= 'ㄱ' and <= 'ㆎ') hangul++;
            else if (c is >= '぀' and <= 'ヿ') kana++;
            else if (c is >= '一' and <= '鿿') han++;
            else if (c is >= 'Ѐ' and <= 'ӿ') cyrillic++;
            else if (c is >= '฀' and <= '๿') thai++;
            else if (char.IsLetter(c) && (c < 'ɐ' || c is >= 'Ḁ' and <= 'ỿ'))
            {
                latin++;
                // 베트남어에만 주로 쓰이는 글자(성조 붙은 모음, đ ơ ư ă)
                if (c is >= 'Ạ' and <= 'ỹ' or 'đ' or 'Đ' or 'ơ' or 'Ơ' or 'ư' or 'Ư' or 'ă' or 'Ă') vietnamese++;
            }
        }

        const double weight = 1.5;
        switch (ScriptOf(tag))
        {
            case Script.Hangul: return hangul * weight;
            // 가나가 충분히 섞여 있으면 일본어, 한자 위주면 중국어 쪽이 이기게 한다.
            // 중국어 글을 일본어 엔진으로 읽으면 한자 몇 개가 가나로 잘못 읽히므로(실측: 12자 중 1자), 가나가 조금 있는 것만으로는 일본어로 보지 않는다
            case Script.Japanese:
                bool enoughKana = kana + han > 0 && (double)kana / (kana + han) >= 0.15;
                return enoughKana ? (kana + han) * weight + 0.5 : han * weight - 0.5;
            case Script.Chinese: return han * weight;
            case Script.Cyrillic: return cyrillic * weight;
            case Script.Thai: return thai * weight;
            default:
                // 베트남어 엔진은 베트남어 글자가 실제로 있을 때만 영어 엔진보다 앞선다
                bool isVietnamese = tag.StartsWith("vi", StringComparison.OrdinalIgnoreCase);
                return latin + (isVietnamese ? vietnamese * 0.5 : 0);
        }
    }

    private static bool IsCjkNoSpace(string tag) =>
        tag.StartsWith("ja", StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    /// <summary>줄 간격을 보고 같은 문단의 줄은 이어 붙이고, 간격이 넓으면 문단을 나눈다</summary>
    private static string BuildText(OcrResult result, string tag)
    {
        var sb = new StringBuilder();
        double prevBottom = double.NaN;
        bool noSpace = IsCjkNoSpace(tag);

        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0) continue;
            double top = line.Words.Min(w => w.BoundingRect.Top);
            double bottom = line.Words.Max(w => w.BoundingRect.Bottom);
            double height = bottom - top;

            string text = line.Text.Trim();
            // 일본어·중국어 엔진은 글자마다 공백을 넣으므로 한자·가나 사이 공백을 없앤다
            if (noSpace) text = Regex.Replace(text, @"(?<=[぀-ヿ一-鿿　-〿]) (?=[぀-ヿ一-鿿　-〿])", "");
            if (text.Length == 0) continue;

            if (sb.Length > 0)
            {
                bool newParagraph = !double.IsNaN(prevBottom) && top - prevBottom > height * 0.7;
                sb.Append(newParagraph ? "\r\n" : noSpace ? "" : " ");
            }
            sb.Append(text);
            prevBottom = bottom;
        }
        return sb.ToString();
    }

    /// <summary>작은 글자는 인식률이 떨어지므로 키워서 넘긴다</summary>
    private static Bitmap Upscale(Bitmap source)
    {
        int max = (int)OcrEngine.MaxImageDimension;
        double factor = source.Height < 60 ? 3 : Math.Max(source.Width, source.Height) < 2000 ? 2 : 1;
        factor = Math.Min(factor, (double)max / Math.Max(source.Width, source.Height));
        if (factor <= 0) factor = 1;

        int width = Math.Max(1, (int)(source.Width * factor));
        int height = Math.Max(1, (int)(source.Height * factor));
        var scaled = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.DrawImage(source, new Rectangle(0, 0, width, height),
            new Rectangle(0, 0, source.Width, source.Height), GraphicsUnit.Pixel);
        return scaled;
    }

    private static async Task<SoftwareBitmap> ToSoftwareBitmapAsync(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        stream.Position = 0;
        var decoder = await BitmapDecoder.CreateAsync(stream.AsRandomAccessStream());
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}

/// <summary>Google 번역 웹 엔드포인트(키 없음). 인식한 글자가 Google 서버로 전송된다</summary>
internal static class Translator
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");
        return client;
    }

    /// <summary>
    /// source(auto 면 자동 감지) → target 으로 번역한다.
    /// altTarget 을 주면, 원문이 이미 target 언어일 때 altTarget 으로 번역한다. null 이면 target 그대로 쓴다.
    /// </summary>
    public static async Task<TranslateOutput> TranslateAsync(string text, string source, string target, string? altTarget)
    {
        var first = await RequestAsync(text, source, target);
        if (altTarget == null) return first;
        bool sameLanguage = first.SourceLang.Length > 0 &&
            first.SourceLang.Split('-')[0].Equals(target.Split('-')[0], StringComparison.OrdinalIgnoreCase);
        if (sameLanguage && !altTarget.Equals(target, StringComparison.OrdinalIgnoreCase))
            return await RequestAsync(text, source, altTarget);
        return first;
    }

    // 같은 엔드포인트의 클라이언트 이름. 하나가 사용량 제한(429)에 걸리면 다음 것을 쓴다(응답 형식은 같다)
    private static readonly string[] Clients = ["gtx", "dict-chrome-ex"];
    private static int _clientIndex;

    private static async Task<string> PostAsync(string text, string source, string target)
    {
        HttpRequestException? last = null;
        for (int attempt = 0; attempt < Clients.Length; attempt++)
        {
            int index = (_clientIndex + attempt) % Clients.Length;
            string url = "https://translate.googleapis.com/translate_a/single?dt=t&client=" + Clients[index] +
                         "&sl=" + Uri.EscapeDataString(source) + "&tl=" + Uri.EscapeDataString(target);
            // 긴 글도 보낼 수 있게 본문(POST)으로 보낸다
            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = text });
            using var response = await Http.PostAsync(url, content);
            if (response.IsSuccessStatusCode)
            {
                _clientIndex = index;   // 통한 클라이언트를 다음에도 먼저 쓴다
                return await response.Content.ReadAsStringAsync();
            }
            last = new HttpRequestException($"번역 서버 응답 {(int)response.StatusCode} ({response.StatusCode})");
            // 제한·차단이 아닌 오류는 클라이언트를 바꿔도 같으므로 바로 끝낸다
            if ((int)response.StatusCode != 429 && (int)response.StatusCode != 403) break;
        }
        throw last ?? new HttpRequestException("번역 요청 실패");
    }

    private static async Task<TranslateOutput> RequestAsync(string text, string source, string target)
    {
        if (string.IsNullOrWhiteSpace(source)) source = "auto";
        string body = await PostAsync(text, source, target);

        // 응답 형식: [[["번역","원문",...],...], null, "감지 언어", ...]
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var sb = new StringBuilder();
        if (root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array)
        {
            foreach (var segment in root[0].EnumerateArray())
            {
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 &&
                    segment[0].ValueKind == JsonValueKind.String)
                    sb.Append(segment[0].GetString());
            }
        }
        string detected = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 2 &&
                          root[2].ValueKind == JsonValueKind.String ? root[2].GetString() ?? "" : "";
        return new TranslateOutput(sb.ToString().Replace("\r\n", "\n").Replace("\n", "\r\n"), detected, target);
    }
}
