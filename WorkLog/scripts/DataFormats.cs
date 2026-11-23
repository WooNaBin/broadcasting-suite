using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WorkLog
{
    /// <summary>
    /// UI catalog of known work templates. Stored values are string ids in JSON arrays.
    /// </summary>
    public static class WorkTemplateCatalog
    {
        public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
        {
            ["Sermon_Sunday_Primary"] = "주일 말씀",
            ["Sermon_Sunday_Secondary"] = "주일 2부 말씀",
            ["Sermon_Wednesday"] = "수요 말씀",
            ["Sermon_Thursday"] = "오전 수요말씀",
            ["AM_Sunday_Senior"] = "은장회 주일집회",
            ["AM_Sunday_Galilee"] = "갈릴리 학교",
            ["BibleAcademy"] = "바이블 아카데미",
            ["Asc_All"] = "모두회",
            ["Asc_Leader_All"] = "전체 임원모임",
            ["Asc_Leader_Sister"] = "어머니회 임원모임",
            ["Asc_Youth"] = "청년회 모임",
            ["Asc_Brother"] = "봉사회 모임",
            ["Asc_Sister"] = "어머니회 모임",
            ["Asc_Senior"] = "은빛장년회 모임",
            ["Wedding"] = "결혼식",
            ["BibleSeminar_Main"] = "대전도집회",
            ["BibleSeminar_Part_L"] = "교구집회",
            ["BibleSeminar_Part_M"] = "구역집회",
            ["BibleSeminar_Part_S"] = "기타집회",
            ["BibleSeminar_Youth"] = "청년전도집회",
            ["Retreat_Summer"] = "하계수양회",
            ["Retreat_Autumn"] = "추계수양회",
            ["Retreat_Winter"] = "동계수양회",
            ["Retreat_Teacher"] = "전국교사수련회",
            ["Retreat_Winter_Youth"] = "전국청년수련회",
            ["Retreat_Media"] = "방문미수련회",
            ["Retreat_University"] = "전국대학생수련회",
            ["SamuelSchool"] = "사무엘학교",
        };

        public static string ToStringKor(string id) =>
            Labels.TryGetValue(id, out var label) ? label : id;
    }

    /// <summary>
    /// One day entry in work-logs/YYYY-MM.json
    /// </summary>
    public class LogData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = ""; // YYYY-MM-DD

        [JsonPropertyName("templates")]
        public List<string> Templates { get; set; } = new();

        [JsonPropertyName("workTemplateNote")]
        public string WorkTemplateNote { get; set; } = "";

        [JsonPropertyName("log")]
        public string Log { get; set; } = "";

        /// <summary>A: 교회 스케줄에 따른 업무내용 (구버전 log 와 동기화)</summary>
        [JsonPropertyName("logA")]
        public string LogA { get; set; } = "";

        /// <summary>B: 교회 일정 외 작업/업무내용</summary>
        [JsonPropertyName("logB")]
        public string LogB { get; set; } = "";

        [JsonPropertyName("isPrinted")]
        public bool IsPrinted { get; set; }

        [JsonPropertyName("sourceScheduleIds")]
        public List<string> SourceScheduleIds { get; set; } = new();

        [JsonPropertyName("createdBy")]
        public string CreatedBy { get; set; } = "";

        [JsonPropertyName("updatedBy")]
        public string UpdatedBy { get; set; } = "";

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = "";

        [JsonPropertyName("updatedAt")]
        public string UpdatedAt { get; set; } = "";
    }

    public class WorkLogMonthFile
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("entries")]
        public List<LogData> Entries { get; set; } = new();
    }

    public class AuditEvent
    {
        [JsonPropertyName("ts")]
        public string Ts { get; set; } = "";

        [JsonPropertyName("actor")]
        public string Actor { get; set; } = "";

        [JsonPropertyName("action")]
        public string Action { get; set; } = "";

        [JsonPropertyName("entryId")]
        public string EntryId { get; set; } = "";

        [JsonPropertyName("date")]
        public string Date { get; set; } = "";

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";
    }
}
