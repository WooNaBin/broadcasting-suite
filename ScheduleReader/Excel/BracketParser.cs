using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScheduleReader.Models;
using ScheduleReader.Parser;

namespace ScheduleReader.Excel;

/// <summary>
/// 엑셀 칸 문자열에서 제목·기간·강사·교구를 읽어
/// ScheduleDataManager가 불러오는 일정 JSON으로 만든다.
/// </summary>
public static class BracketParser
{
    public sealed class ParsedEntry
    {
        public string Title { get; init; } = "";
        public DateOnly? StartDate { get; init; }
        public DateOnly? EndDate { get; init; }
        public string Speaker { get; init; } = "";
        public string AccessoryInfo { get; init; } = "";
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly Regex LeadingDayRe = new(
        @"^\s*(\d{1,2})(?:\s*[일日])?(?:\s+|(?:\s*[\r\n]+))",
        RegexOptions.Compiled);
    private static readonly Regex LeadingDayOnlyRe = new(
        @"^\s*(\d{1,2})(?:\s*[일日])?\s*$",
        RegexOptions.Compiled);
    // 9.13 현충일 / 9/13 제목 처럼 월.일(또는 /)로 시작하는 칸
    private static readonly Regex LeadingMdRe = new(
        @"^\s*(\d{1,2})[./](\d{1,2})(?:\s*[일日])?(?:\s+|(?:\s*[\r\n]+))",
        RegexOptions.Compiled);
    private static readonly Regex TitleHintRe = new(
        @"(예배|모임|기도|말씀|찬양|특새|새벽|주일|수요|금요|청년|부흥|집회|세미나|회의|훈련|수양회|전도|학부모|교실|아카데미|임원|어머니회)",
        RegexOptions.Compiled);
    private static readonly Regex NameOnlyRe = new(
        @"^[가-힣]{2,4}[A-Za-z]?$",
        RegexOptions.Compiled);
    private static readonly Regex LocationPairRe = new(
        @"^(.+?)\(([^()]*)\)$",
        RegexOptions.Compiled);
    private static readonly Regex MdToMdRe = new(
        @"^(\d{1,2})\.(\d{1,2})\s*[-~～]\s*(\d{1,2})\.(\d{1,2})$",
        RegexOptions.Compiled);
    private static readonly Regex MdToDRe = new(
        @"^(\d{1,2})\.(\d{1,2})\s*[-~～]\s*(\d{1,2})$",
        RegexOptions.Compiled);
    private static readonly Regex DToDRe = new(
        @"^(\d{1,2})\s*[-~～]\s*(\d{1,2})$",
        RegexOptions.Compiled);
    private static readonly Regex MdRe = new(
        @"^(\d{1,2})\.(\d{1,2})$",
        RegexOptions.Compiled);
    private static readonly Regex DayRe = new(
        @"^(\d{1,2})$",
        RegexOptions.Compiled);

    private static int _contextYear;
    private static int _contextMonth;

    /// <summary>일자만 있는 문자열을 몇 년 몇 월 기준으로 읽을지 정한다. 파싱 전에 한 번 호출한다.</summary>
    public static void SetContext(int year, int month)
    {
        if (year is < 2000 or > 2099)
            throw new ArgumentOutOfRangeException(nameof(year), "기준 연도는 2000~2099입니다.");
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), "기준 월은 1~12입니다.");
        _contextYear = year;
        _contextMonth = month;
    }

    public static int ContextYear => _contextYear;
    public static int ContextMonth => _contextMonth;
    private static bool HasContext => _contextYear >= 2000 && _contextMonth is >= 1 and <= 12;


    /// <summary>칸 맨 앞의 날짜 숫자(달력 일, 또는 월.일)를 떼어 낸다.</summary>
    public static (int? Day, string Body) StripLeadingDay(string? text)
    {
        var raw = text ?? "";
        var only = LeadingDayOnlyRe.Match(raw);
        if (only.Success)
            return (int.Parse(only.Groups[1].Value, CultureInfo.InvariantCulture), "");

        var md = LeadingMdRe.Match(raw);
        if (md.Success)
        {
            var day = int.Parse(md.Groups[2].Value, CultureInfo.InvariantCulture);
            return (day, raw[md.Length..].Trim());
        }

        var m = LeadingDayRe.Match(raw);
        if (!m.Success)
            return (null, raw.Trim());

        var dayNum = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        return (dayNum, raw[m.Length..].Trim());
    }

    /// <summary>괄호 안 문자열만 꺼낸다. 겹친 괄호는 가장 바깥을 기준으로 한다.</summary>
    public static string[] ExtractBrackets(string? text)
    {
        var results = new List<string>();
        var raw = text ?? "";
        var depth = 0;
        var start = -1;
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (c is '(' or '（')
            {
                if (depth == 0) start = i + 1;
                depth++;
            }
            else if (c is ')' or '）')
            {
                if (depth == 0) continue;
                depth--;
                if (depth == 0 && start >= 0)
                {
                    results.Add(raw[start..i]);
                    start = -1;
                }
            }
        }

        return results.ToArray();
    }

    public static IReadOnlyList<ParsedEntry> ParseEntries(string? text, int? contextDay = null)
    {
        var normalized = Normalize(text);
        if (string.IsNullOrWhiteSpace(normalized))
            return [];

        var (leadDay, body) = StripLeadingDay(normalized);
        var day = contextDay ?? leadDay;
        if (string.IsNullOrWhiteSpace(body))
            return [];

        var entries = new List<ParsedEntry>();
        foreach (var chunk in SplitEvents(body))
        {
            var parsed = ParseEvent(chunk, day);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Title))
                entries.Add(parsed);
        }

        ApplySundaySermonSpeaker(entries);
        return entries;
    }

    public const string SundaySermonTitle = "주일말씀";

    public static bool IsSundaySermonTitle(string? title) =>
        string.Equals((title ?? "").Trim(), SundaySermonTitle, StringComparison.Ordinal);

    /// <summary>
    /// 일요일 일정은 강사가 1명뿐이고, 제목이 주일말씀인 항목에만 넣는다.
    /// 다른 일요일 일정은 강사를 비운다. 주일말씀이 없으면 그 날짜 강사는 모두 비운다.
    /// </summary>
    public static void ApplySundaySermonSpeaker(List<ParsedEntry> entries)
    {
        if (entries.Count == 0) return;

        foreach (var group in entries
            .Select((e, i) => (e, i))
            .Where(x => x.e.StartDate is DateOnly d && d.DayOfWeek == DayOfWeek.Sunday)
            .GroupBy(x => x.e.StartDate))
        {
            var idxs = group.Select(x => x.i).ToList();
            var sermonIdx = idxs.FirstOrDefault(i => IsSundaySermonTitle(entries[i].Title), -1);
            if (sermonIdx < 0) continue;

            var speaker = "";
            var extra = "";
            var donorIdx = -1;
            if (!string.IsNullOrWhiteSpace(entries[sermonIdx].Speaker))
            {
                speaker = entries[sermonIdx].Speaker.Trim();
            }
            else
            {
                foreach (var i in idxs)
                {
                    if (string.IsNullOrWhiteSpace(entries[i].Speaker)) continue;
                    donorIdx = i;
                    speaker = entries[i].Speaker.Trim();
                    extra = entries[i].AccessoryInfo;
                    break;
                }
            }

            foreach (var i in idxs)
            {
                var e = entries[i];
                var keep = i == sermonIdx;
                var accessory = e.AccessoryInfo;
                if (keep && extra.Length > 0 && string.IsNullOrWhiteSpace(accessory))
                    accessory = extra;
                else if (!keep && i == donorIdx && extra.Length > 0)
                    accessory = "";

                var nextSpeaker = keep ? speaker : "";
                if (e.Speaker == nextSpeaker && e.AccessoryInfo == accessory) continue;
                entries[i] = new ParsedEntry
                {
                    Title = e.Title,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate,
                    Speaker = nextSpeaker,
                    AccessoryInfo = accessory,
                };
            }
        }
    }

    public static void ApplySundaySermonSpeaker(IList<RawEvent> events)
    {
        foreach (var bucket in events
            .Where(e => e.EventDate is DateOnly d && d.DayOfWeek == DayOfWeek.Sunday)
            .GroupBy(e => e.EventDate))
        {
            var list = bucket.ToList();
            var sermon = list.FirstOrDefault(e => IsSundaySermonTitle(e.Title));
            if (sermon is null)
            {
                foreach (var e in list)
                    e.Speaker = "";
                continue;
            }

            RawEvent? donor = null;
            var speaker = "";
            var extra = "";
            if (!string.IsNullOrWhiteSpace(sermon.Speaker))
            {
                speaker = sermon.Speaker.Trim();
            }
            else
            {
                donor = list.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Speaker));
                speaker = donor?.Speaker.Trim() ?? "";
                extra = donor?.Notes ?? "";
            }

            foreach (var e in list)
            {
                if (IsSundaySermonTitle(e.Title))
                {
                    e.Speaker = speaker;
                    if (extra.Length > 0 && string.IsNullOrWhiteSpace(e.Notes))
                        e.Notes = extra;
                }
                else
                {
                    e.Speaker = "";
                    if (donor is not null && ReferenceEquals(e, donor) && extra.Length > 0)
                        e.Notes = "";
                }
            }
        }
    }

    public static List<RawEvent> ToRawEvents(
        string? text,
        int? contextDay = null,
        string sourceHint = "")
    {
        return ParseEntries(text, contextDay)
            .Select(e => new RawEvent
            {
                EventDate = e.StartDate,
                EndDate = e.EndDate is not null && e.EndDate != e.StartDate ? e.EndDate : null,
                Title = e.Title,
                Speaker = e.Speaker,
                Notes = e.AccessoryInfo,
                SourceHint = sourceHint,
            })
            .ToList();
    }

    /// <summary>기간은 날짜마다 한 줄, 모든 줄의 releaseDate는 마지막 날.</summary>
    public static List<ScheduleItem> ToScheduleItems(
        string? text,
        int? contextDay = null,
        string color = "auto")
    {
        return Normalizer.ExpandAndNormalize(
            ToRawEvents(text, contextDay),
            expandRanges: true,
            forceNoTime: true,
            forceNoPlace: true,
            defaultColor: color);
    }

    public static string ToJson(string? text, int? contextDay = null, string color = "auto")
    {
        return JsonSerializer.Serialize(ToScheduleItems(text, contextDay, color), JsonOpts) + "\n";
    }

    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        return text
            .Replace('（', '(')
            .Replace('）', ')')
            .Replace('：', ':')
            .Replace('\u00A0', ' ')
            .Trim();
    }

    private static List<string> SplitEvents(string text)
    {
        var result = new List<string>();
        foreach (var part in SplitTopLevelSpacedSlash(text))
            result.AddRange(SplitLinesIntoEvents(part));
        return result;
    }

    private static List<string> SplitLinesIntoEvents(string text)
    {
        var lines = Regex.Split(text, @"\r\n|\n|\r")
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count <= 1)
            return lines.Count == 0 ? [] : [lines[0]];

        var events = new List<string>();
        var buf = new List<string>();
        foreach (var line in lines)
        {
            if (buf.Count > 0 && IsEventHead(line))
            {
                events.Add(string.Join('\n', buf));
                buf = [line];
            }
            else
            {
                buf.Add(line);
            }
        }

        if (buf.Count > 0)
            events.Add(string.Join('\n', buf));
        return events;
    }

    private static bool IsEventHead(string line)
    {
        if (line.StartsWith('('))
            return false;
        if (IndexOfTopLevel(line, ':') >= 0) return true;
        if (IsLocationPairList(line)) return false;
        if (line.Contains('(')) return true;
        if (TitleHintRe.IsMatch(line)) return true;
        if (NameOnlyRe.IsMatch(line)) return false;
        return false;
    }

    private static bool IsLocationPairList(string line)
    {
        if (IndexOfTopLevel(line, ':') >= 0) return false;
        if (TitleHintRe.IsMatch(line)) return false;
        var parts = SplitTopLevel(line, ',', '，', '.', '·');
        if (parts.Count < 2) return false;
        return parts.All(p => LocationPairRe.IsMatch(p.Trim()));
    }

    private static ParsedEntry? ParseEvent(string chunk, int? contextDay)
    {
        var flat = Regex.Replace(chunk, @"\s+", " ").Trim();
        if (flat.Length == 0) return null;

        string left;
        string details;
        var colon = IndexOfTopLevel(flat, ':');
        if (colon >= 0)
        {
            left = flat[..colon].Trim();
            details = flat[(colon + 1)..].Trim();
        }
        else
        {
            left = flat;
            details = "";
        }

        var title = left;
        DateOnly? start = null;
        DateOnly? end = null;
        var speakers = new List<string>();
        var locations = new List<string>();

        var open = IndexOfTopLevel(left, '(');
        if (open >= 0 && left.EndsWith(')'))
        {
            var inner = left[(open + 1)..^1].Trim();
            var prefix = left[..open].Trim();
            if (prefix.Length > 0)
                title = prefix;

            ClassifyParenInner(inner, ref start, ref end, speakers, locations);
        }

        if (details.Length > 0)
            ParseDetails(details, locations, speakers);

        if (speakers.Count == 0 && locations.Count == 0)
            TryTakeTrailingName(ref title, speakers);

        if (start is null && HasContext && contextDay is > 0 and <= 31)
        {
            try
            {
                start = new DateOnly(_contextYear, _contextMonth, contextDay.Value);
                end = start;
            }
            catch
            {
                // 달력에 없는 일(9월 31일 등)은 날짜를 비운다.
            }
        }

        title = title.Trim();
        if (title.Length == 0) return null;

        return new ParsedEntry
        {
            Title = title,
            StartDate = start,
            EndDate = end ?? start,
            Speaker = Join(speakers.Select(NormalizeSpeakerName)),
            AccessoryInfo = Join(locations),
        };
    }

    /// <summary>
    /// 괄호 안: 날짜 / 끝이 P·E(또는 PA·Ev)인 강사 / 그 외는 부가정보.
    /// 강사명 뒤 <c>:</c> 이후는 지명 등 부가정보.
    /// </summary>
    private static void ClassifyParenInner(
        string inner,
        ref DateOnly? start,
        ref DateOnly? end,
        List<string> speakers,
        List<string> locations)
    {
        if (TryParseDateRange(inner, out var ds, out var de))
        {
            start = ds;
            end = de;
            return;
        }

        foreach (var token in SplitTopLevel(inner, ',', '，'))
            ClassifyToken(token, speakers, locations);
    }

    private static void ParseDetails(string details, List<string> locations, List<string> speakers)
    {
        foreach (var part in SplitTopLevel(details, ',', '，', '.', '·'))
            ClassifyToken(part, speakers, locations);
    }

    private static void ClassifyToken(string text, List<string> speakers, List<string> locations)
    {
        var token = text.Trim();
        if (token.Length == 0 || IsDateToken(token)) return;

        var pair = LocationPairRe.Match(token);
        if (pair.Success)
        {
            ClassifyToken(pair.Groups[1].Value, speakers, locations);
            DateOnly? unusedStart = null;
            DateOnly? unusedEnd = null;
            ClassifyParenInner(pair.Groups[2].Value.Trim(), ref unusedStart, ref unusedEnd, speakers, locations);
            return;
        }

        var colon = IndexOfTopLevel(token, ':');
        if (colon >= 0)
        {
            var name = token[..colon].Trim();
            var extra = token[(colon + 1)..].Trim();
            if (IsSpeakerName(name))
            {
                speakers.Add(name);
                if (extra.Length > 0) locations.Add(extra);
                return;
            }

            locations.Add(token);
            return;
        }

        if (IsSpeakerName(token))
            speakers.Add(token);
        else
            locations.Add(token);
    }

    /// <summary>한글 이름 뒤 직분 접미 P/E (이미 PA·Ev인 경우 포함).</summary>
    private static bool IsSpeakerName(string name)
    {
        var t = name.Trim();
        if (t.Length < 2) return false;
        if (t.EndsWith("PA", StringComparison.OrdinalIgnoreCase)) return true;
        if (t.EndsWith("Ev", StringComparison.OrdinalIgnoreCase)) return true;
        var last = t[^1];
        return last is 'P' or 'p' or 'E' or 'e';
    }

    /// <summary>추출 후 직분 표기 통일: P→PA, E→Ev.</summary>
    public static string NormalizeSpeakerName(string name)
    {
        var t = name.Trim();
        if (t.Length == 0) return t;
        if (t.EndsWith("PA", StringComparison.OrdinalIgnoreCase))
            return t[..^2] + "PA";
        if (t.EndsWith("Ev", StringComparison.OrdinalIgnoreCase))
            return t[..^2] + "Ev";
        if (t.EndsWith('P') || t.EndsWith('p'))
            return t[..^1] + "PA";
        if (t.EndsWith('E') || t.EndsWith('e'))
            return t[..^1] + "Ev";
        return t;
    }

    /// <summary>
    /// 강사 필드 후처리: P→PA, E→Ev.
    /// <c>이름P:지명</c>이면 강사와 부가정보로 나눈다. 접미 없는 이름은 강사로 둔다.
    /// </summary>
    public static (string Speaker, string Accessory) FinalizeSpeakerField(string? speaker, string? accessory = "")
    {
        var (speakers, extras) = CollectSpeakerAccessoryParts(speaker, accessory);
        return (Join(speakers), Join(extras));
    }

    /// <summary>
    /// 강사·부가정보가 쉼표로 2명 이상이고 개수가 같으면
    /// 제목(부가)·강사·부가 쌍으로 나눈다. 그렇지 않으면 한 건.
    /// </summary>
    public static IReadOnlyList<(string Title, string Speaker, string Accessory)> ExpandPairedSpeakerAccessory(
        string title,
        string? speaker,
        string? accessory = "")
    {
        var baseTitle = (title ?? "").Trim();
        var (speakers, extras) = CollectSpeakerAccessoryParts(speaker, accessory);
        if (speakers.Count >= 2 && speakers.Count == extras.Count)
        {
            var paired = new List<(string, string, string)>(speakers.Count);
            for (var i = 0; i < speakers.Count; i++)
            {
                var note = extras[i];
                paired.Add(($"{baseTitle}({note})", speakers[i], note));
            }

            return paired;
        }

        return [(baseTitle, Join(speakers), Join(extras))];
    }

    private static (List<string> Speakers, List<string> Extras) CollectSpeakerAccessoryParts(
        string? speaker,
        string? accessory)
    {
        var speakers = new List<string>();
        var extras = new List<string>();
        foreach (var part in SplitTopLevel(accessory ?? "", ',', '，'))
        {
            var t = part.Trim();
            if (t.Length > 0) extras.Add(t);
        }

        foreach (var part in SplitTopLevel(speaker ?? "", ',', '，'))
        {
            var t = part.Trim();
            if (t.Length == 0) continue;
            var colon = IndexOfTopLevel(t, ':');
            if (colon >= 0)
            {
                var name = t[..colon].Trim();
                var extra = t[(colon + 1)..].Trim();
                if (IsSpeakerName(name))
                {
                    speakers.Add(NormalizeSpeakerName(name));
                    if (extra.Length > 0) extras.Add(extra);
                    continue;
                }
            }

            speakers.Add(NormalizeSpeakerName(t));
        }

        return (speakers, extras);
    }

    private static void TryTakeTrailingName(ref string title, List<string> speakers)
    {
        var parts = title.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        var last = parts[^1];
        if (!NameOnlyRe.IsMatch(last)) return;
        if (TitleHintRe.IsMatch(last)) return;
        if (!TitleHintRe.IsMatch(string.Join(' ', parts.Take(parts.Length - 1)))
            && !TitleHintRe.IsMatch(title))
            return;

        speakers.Add(last);
        title = string.Join(' ', parts.Take(parts.Length - 1));
    }

    private static bool TryParseDateRange(string inner, out DateOnly start, out DateOnly end)
    {
        start = default;
        end = default;
        if (!HasContext) return false;
        var year = _contextYear;
        var month = _contextMonth;
        var token = inner.Replace(" ", "");
        if (!IsDateToken(token)) return false;

        int sm, sd, em, ed;
        var mdmd = MdToMdRe.Match(token);
        if (mdmd.Success)
        {
            sm = int.Parse(mdmd.Groups[1].Value, CultureInfo.InvariantCulture);
            sd = int.Parse(mdmd.Groups[2].Value, CultureInfo.InvariantCulture);
            em = int.Parse(mdmd.Groups[3].Value, CultureInfo.InvariantCulture);
            ed = int.Parse(mdmd.Groups[4].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            var mdd = MdToDRe.Match(token);
            if (mdd.Success)
            {
                sm = int.Parse(mdd.Groups[1].Value, CultureInfo.InvariantCulture);
                sd = int.Parse(mdd.Groups[2].Value, CultureInfo.InvariantCulture);
                em = sm;
                ed = int.Parse(mdd.Groups[3].Value, CultureInfo.InvariantCulture);
            }
            else
            {
                var dd = DToDRe.Match(token);
                if (dd.Success)
                {
                    sm = month;
                    em = month;
                    sd = int.Parse(dd.Groups[1].Value, CultureInfo.InvariantCulture);
                    ed = int.Parse(dd.Groups[2].Value, CultureInfo.InvariantCulture);
                }
                else
                {
                    var md = MdRe.Match(token);
                    if (md.Success)
                    {
                        sm = int.Parse(md.Groups[1].Value, CultureInfo.InvariantCulture);
                        sd = int.Parse(md.Groups[2].Value, CultureInfo.InvariantCulture);
                        em = sm;
                        ed = sd;
                    }
                    else
                    {
                        var d = DayRe.Match(token);
                        if (!d.Success) return false;
                        sm = month;
                        em = month;
                        sd = int.Parse(d.Groups[1].Value, CultureInfo.InvariantCulture);
                        ed = sd;
                    }
                }
            }
        }

        var startYear = ResolveYear(year, month, sm);
        var endYear = em < sm ? startYear + 1 : startYear;

        if (!TryMakeDate(startYear, sm, sd, out start)) return false;
        if (!TryMakeDate(endYear, em, ed, out end)) return false;
        if (end < start) (start, end) = (end, start);
        return true;
    }

    private static int ResolveYear(int contextYear, int contextMonth, int startMonth)
    {
        // 1월 칸의 12.28 처럼, 문맥 달보다 한참 뒤 월이면 직전 해로 본다.
        if (startMonth - contextMonth > 6)
            return contextYear - 1;
        if (contextMonth - startMonth > 6)
            return contextYear + 1;
        return contextYear;
    }

    private static bool TryMakeDate(int year, int month, int day, out DateOnly date)
    {
        date = default;
        if (month is < 1 or > 12 || day < 1) return false;
        try
        {
            if (day > DateTime.DaysInMonth(year, month)) return false;
            date = new DateOnly(year, month, day);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDateToken(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var t = text.Replace(" ", "");
        return t.Length > 0 && t.All(c => char.IsDigit(c) || c is '.' or '-' or '~' or '～');
    }

    private static List<string> SplitTopLevelSpacedSlash(string text)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && c == '/')
            {
                var leftWs = i > 0 && char.IsWhiteSpace(text[i - 1]);
                var rightWs = i + 1 < text.Length && char.IsWhiteSpace(text[i + 1]);
                if (leftWs && rightWs)
                {
                    var piece = text[start..i].Trim();
                    if (piece.Length > 0) result.Add(piece);
                    start = i + 1;
                }
            }
        }

        var last = text[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }

    private static List<string> SplitTopLevel(string text, params char[] separators)
    {
        var result = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
            else if (depth == 0 && separators.Contains(c))
            {
                var piece = text[start..i].Trim();
                if (piece.Length > 0) result.Add(piece);
                start = i + 1;
            }
        }

        var last = text[start..].Trim();
        if (last.Length > 0) result.Add(last);
        return result;
    }

    private static int IndexOfTopLevel(string text, char mark)
    {
        var depth = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (depth == 0 && c == mark) return i;
            if (c == '(') depth++;
            else if (c == ')') depth = Math.Max(0, depth - 1);
        }

        return -1;
    }

    private static string Join(IEnumerable<string> parts) =>
        string.Join(", ", parts.Select(p => p.Trim()).Where(p => p.Length > 0));
}
