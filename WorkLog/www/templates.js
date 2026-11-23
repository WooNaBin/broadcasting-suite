/** 기본 업무 템플릿 카탈로그 + 헬퍼 */

export const WORK_TEMPLATES = [
  { id: "Sermon_Sunday_Primary", label: "주일 말씀" },
  { id: "Sermon_Sunday_Secondary", label: "주일 2부 말씀" },
  { id: "Sermon_Wednesday", label: "수요 말씀" },
  { id: "Sermon_Thursday", label: "오전 수요말씀" },
  { id: "AM_Sunday_Senior", label: "은장회 주일집회" },
  { id: "AM_Sunday_Galilee", label: "갈릴리 학교" },
  { id: "BibleAcademy", label: "바이블 아카데미" },
  { id: "Asc_All", label: "모두회" },
  { id: "Asc_Leader_All", label: "전체 임원모임" },
  { id: "Asc_Leader_Sister", label: "어머니회 임원모임" },
  { id: "Asc_Youth", label: "청년회 모임" },
  { id: "Asc_Brother", label: "봉사회 모임" },
  { id: "Asc_Sister", label: "어머니회 모임" },
  { id: "Asc_Senior", label: "은빛장년회 모임" },
  { id: "Wedding", label: "결혼식" },
  { id: "BibleSeminar_Main", label: "대전도집회" },
  { id: "BibleSeminar_Part_L", label: "교구집회" },
  { id: "BibleSeminar_Part_M", label: "구역집회" },
  { id: "BibleSeminar_Part_S", label: "기타집회" },
  { id: "BibleSeminar_Youth", label: "청년전도집회" },
  { id: "Retreat_Summer", label: "하계수양회" },
  { id: "Retreat_Autumn", label: "추계수양회" },
  { id: "Retreat_Winter", label: "동계수양회" },
  { id: "Retreat_Teacher", label: "전국교사수련회" },
  { id: "Retreat_Winter_Youth", label: "전국청년수련회" },
  { id: "Retreat_Media", label: "방문미수련회" },
  { id: "Retreat_University", label: "전국대학생수련회" },
  { id: "SamuelSchool", label: "사무엘학교" },
];

export const PLACE_LABELS = {
  None: "미정",
  Main_Large: "본당 대강당",
  Main_Medium: "본당 중강당",
  Main_Small: "본당 소강당",
  Edu_Large: "교육원 대강당",
  Edu_Medium: "교육원 중강당",
  Edu_Small: "교육원 소강당",
  Edu_Nongseon: "교육원 농선부실",
  Edu_Siseon: "교육원 시선부실",
  Edu_Go2: "교육원 고2실",
  Etc: "기타",
};

export const PREPARATION_LABELS = {
  Recording: "녹화",
  Praise: "찬양",
  Streaming: "스트리밍",
  OtherDisplay: "기타화면",
};

export const DEFAULT_PROFILES = [
  { id: "ko_yunseok", displayName: "고윤석" },
  { id: "kim_jonghee", displayName: "김종희" },
  { id: "lim_wonbin", displayName: "임원빈" },
];

export const DEFAULT_DEFAULTS = {
  version: 2,
  specialNotesMax: 10,
  customTemplates: [],
  titleRules: [
    {
      id: "rule-daejundo",
      conditionType: "schedule_title",
      value: "대전도",
      record: "{schedule_title} {place_label} 방송 및 유튜브 송출 준비.",
      appendTarget: "A",
      templates: ["BibleSeminar_Main"],
    },
  ],
  templateButtons: {
    Sermon_Sunday_Primary: { appendLog: "주일 말씀 방송 준비", appendTarget: "A" },
    Sermon_Wednesday: { appendLog: "수요 말씀 방송 준비", appendTarget: "A" },
    Sermon_Thursday: { appendLog: "오전 수요말씀 방송 준비", appendTarget: "A" },
  },
};

/** 기본 + 사용자 추가 템플릿 */
export function allTemplates(defaultsDoc) {
  const custom = Array.isArray(defaultsDoc?.customTemplates) ? defaultsDoc.customTemplates : [];
  const seen = new Set(WORK_TEMPLATES.map((t) => t.id));
  const extras = custom.filter((t) => t?.id && t?.label && !seen.has(t.id));
  return [...WORK_TEMPLATES, ...extras.map((t) => ({ id: t.id, label: t.label, custom: true }))];
}

export function templateLabel(id, defaultsDoc) {
  const hit = allTemplates(defaultsDoc).find((t) => t.id === id);
  return hit?.label || id;
}

export function placeLabel(place) {
  if (!place) return "-";
  return PLACE_LABELS[place] || place;
}

function migrateConditionType(type) {
  if (type === "title_contains" || !type) return "schedule_title";
  return type;
}

function normalizeTemplateButtons(map) {
  const out = {};
  for (const [id, cfg] of Object.entries(map || {})) {
    if (!cfg || typeof cfg !== "object") continue;
    const appendLog = String(cfg.appendLog || "");
    if (!appendLog.trim()) continue;
    out[id] = {
      appendLog,
      appendTarget: normalizeAppendTarget(cfg.appendTarget),
    };
  }
  return out;
}

export function normalizeAppendTarget(value) {
  return String(value || "A").toUpperCase() === "B" ? "B" : "A";
}

export function normalizeDefaults(doc) {
  if (!doc || typeof doc !== "object") return structuredClone(DEFAULT_DEFAULTS);
  if (Array.isArray(doc.titleRules) && doc.templateButtons) {
    const max = Number(doc.specialNotesMax);
    return {
      version: 2,
      specialNotesMax: Number.isFinite(max) ? Math.min(50, Math.max(1, Math.round(max))) : 10,
      customTemplates: Array.isArray(doc.customTemplates) ? doc.customTemplates : [],
      titleRules: (doc.titleRules || []).map((rule) => ({
        ...rule,
        conditionType: migrateConditionType(rule.conditionType),
        appendTarget: normalizeAppendTarget(rule.appendTarget),
      })),
      templateButtons: normalizeTemplateButtons(doc.templateButtons),
    };
  }
  const titleRules = [];
  for (const rule of doc.byScheduleTitle || []) {
    titleRules.push({
      id: crypto.randomUUID(),
      conditionType: "schedule_title",
      value: rule.match || "",
      record: rule.log || "",
      appendTarget: "A",
      templates: [...(rule.templates || [])],
    });
  }
  const templateButtons = {};
  for (const [, cfg] of Object.entries(doc.byWeekday || {})) {
    for (const tid of cfg.templates || []) {
      if (!templateButtons[tid] && cfg.log) {
        templateButtons[tid] = { appendLog: cfg.log, appendTarget: "A" };
      }
    }
  }
  const max = Number(doc.specialNotesMax);
  return {
    version: 2,
    specialNotesMax: Number.isFinite(max) ? Math.min(50, Math.max(1, Math.round(max))) : 10,
    customTemplates: Array.isArray(doc.customTemplates) ? doc.customTemplates : [],
    titleRules,
    templateButtons: normalizeTemplateButtons(templateButtons),
  };
}
