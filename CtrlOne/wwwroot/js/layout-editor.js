(() => {
  const DEFAULTS = {
    "--col-a-min": "140px",
    "--col-a-fr": "0.9fr",
    "--col-b-min": "108px",
    "--col-b-fr": "0.72fr",
    "--col-c-min": "200px",
    "--col-c-fr": "1.2fr",
    "--col-d-min": "220px",
    "--col-d-fr": "1.45fr",
    "--col-e-width": "2.4rem",
    "--card-gap-row": "0.75rem",
    "--card-gap-col": "0.85rem",
    "--card-padding-y": "0.85rem",
    "--card-padding-x": "0.9rem",
    "--mode-min-height": "4.4rem",
    "--mode-font-size": "1.15rem",
    "--mode-padding-y": "0.7rem",
    "--mode-padding-x": "0.65rem",
    "--mode-radius": "12px",
    "--transport-cols": "4",
  };

  const values = { ...DEFAULTS };
  const iframe = document.getElementById("preview");
  const toast = document.getElementById("toast");
  const fields = [...document.querySelectorAll(".layout-field")];

  function parseNum(raw) {
    const n = parseFloat(String(raw).replace(/[^\d.-]/g, ""));
    return Number.isFinite(n) ? n : 0;
  }

  function formatValue(num, unit, step) {
    if (!unit) return String(Math.round(num));
    const decimals = String(step).includes(".") ? String(step).split(".")[1].length : 0;
    return `${num.toFixed(decimals)}${unit}`;
  }

  function applyToDoc(doc) {
    if (!doc?.documentElement) return;
    Object.entries(values).forEach(([key, val]) => {
      doc.documentElement.style.setProperty(key, val);
    });
  }

  function applyAll() {
    applyToDoc(document);
    try {
      applyToDoc(iframe.contentDocument);
    } catch {
      // cross-origin unlikely on same host
    }
  }

  function syncField(field) {
    const key = field.getAttribute("data-var");
    const unit = field.getAttribute("data-unit") || "";
    const min = Number(field.getAttribute("data-min"));
    const max = Number(field.getAttribute("data-max"));
    const step = Number(field.getAttribute("data-step"));
    const input = field.querySelector("input");
    const output = field.querySelector("output");
    const current = parseNum(values[key] ?? DEFAULTS[key]);

    input.min = String(min);
    input.max = String(max);
    input.step = String(step);
    input.value = String(current);
    output.textContent = formatValue(current, unit, step);

    input.oninput = () => {
      const next = Number(input.value);
      values[key] = formatValue(next, unit, step);
      output.textContent = values[key];
      applyAll();
    };
  }

  function buildCss() {
    const lines = Object.entries(values).map(([k, v]) => `  ${k}: ${v};`);
    return `/*
 * CtrlOne 카드 레이아웃 조절용 변수
 * layout-editor.html 에서 복사됨 — wwwroot/css/layout.css 에 붙여넣기
 */
:root {
${lines.join("\n")}
}
`;
  }

  function showToast(text) {
    toast.textContent = text;
    toast.classList.add("show");
    setTimeout(() => toast.classList.remove("show"), 1600);
  }

  fields.forEach(syncField);

  iframe.addEventListener("load", applyAll);
  applyAll();

  document.getElementById("btn-copy-css").addEventListener("click", async () => {
    const css = buildCss();
    try {
      await navigator.clipboard.writeText(css);
      showToast("CSS 복사됨 → layout.css에 붙여넣기");
    } catch {
      prompt("복사해서 layout.css에 붙여넣으세요", css);
    }
  });

  document.getElementById("btn-reset").addEventListener("click", () => {
    Object.assign(values, DEFAULTS);
    fields.forEach(syncField);
    applyAll();
    showToast("기본값으로 되돌림");
  });
})();
