(() => {
  const $ = (id) => document.getElementById(id);

  const state = {
    sessionId: null,
    extractReady: false,
  };

  const fileLabel = $("fileLabel");
  const sheetSelect = $("sheetSelect");
  const statusText = $("statusText");
  const btnLoad = $("btnLoad");
  const btnExtract = $("btnExtract");
  const btnExport = $("btnExport");
  const excelInput = $("excelInput");
  const previewWrap = $("previewWrap");
  const previewMeta = $("previewMeta");
  const resultWrap = $("resultWrap");
  const resultMeta = $("resultMeta");
  const warningList = $("warningList");

  function setStatus(text) {
    statusText.textContent = text;
  }

  function syncButtons() {
    btnExtract.disabled = !state.sessionId;
    btnExport.disabled = !state.extractReady;
  }

  function renderPreview(preview) {
    if (!preview?.rows?.length) {
      previewWrap.innerHTML = `<p class="empty">미리볼 셀이 없습니다.</p>`;
      previewMeta.textContent = "";
      return;
    }
    const colCount = Math.max(...preview.rows.map((r) => r.length), 1);
    const head = Array.from({ length: colCount }, (_, i) => `<th>${i + 1}</th>`).join("");
    const body = preview.rows
      .map(
        (row) =>
          `<tr>${Array.from({ length: colCount }, (_, i) => `<td>${escapeHtml(row[i] || "")}</td>`).join("")}</tr>`,
      )
      .join("");
    previewWrap.innerHTML = `<table><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table>`;
    previewMeta.textContent = `${preview.sheet} · 표시 ${preview.rows.length}행`;
  }

  function renderResult(payload) {
    const warnings = payload.warnings || [];
    if (warnings.length) {
      warningList.classList.remove("hidden");
      warningList.innerHTML = warnings.map((w) => `<li>${escapeHtml(w)}</li>`).join("");
    } else {
      warningList.classList.add("hidden");
      warningList.innerHTML = "";
    }

    const items = payload.items || [];
    resultMeta.textContent = payload.ok
      ? `${items.length}건 · ${payload.sheet || ""}`
      : "추출 실패";

    if (!items.length) {
      resultWrap.innerHTML = `<p class="empty">추출된 일정이 없습니다.</p>`;
      return;
    }
    const rows = items
      .map(
        (item) => `<tr>
          <td>${escapeHtml(item.scheduleDate || "")}</td>
          <td>${escapeHtml(formatTime(item.startTime))}</td>
          <td>${escapeHtml(item.scheduleTitle || "")}</td>
          <td>${escapeHtml(item.sermonSpeaker || "")}</td>
          <td>${escapeHtml(item.place || "")}</td>
        </tr>`,
      )
      .join("");
    resultWrap.innerHTML = `<table>
      <thead><tr><th>날짜</th><th>시간</th><th>제목</th><th>강사</th><th>장소</th></tr></thead>
      <tbody>${rows}</tbody>
    </table>`;
  }

  function formatTime(value) {
    const n = Number(value || 0);
    if (!n) return "";
    const h = Math.floor(n / 100);
    const m = n % 100;
    return `${String(h).padStart(2, "0")}:${String(m).padStart(2, "0")}`;
  }

  function escapeHtml(value) {
    return String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll('"', "&quot;");
  }

  function fillSheets(sheets, active) {
    sheetSelect.innerHTML = (sheets || [])
      .map(
        (name) =>
          `<option value="${escapeHtml(name)}" ${name === active ? "selected" : ""}>${escapeHtml(name)}</option>`,
      )
      .join("");
    sheetSelect.disabled = !(sheets && sheets.length);
  }

  btnLoad.addEventListener("click", () => excelInput.click());

  excelInput.addEventListener("change", async () => {
    const file = excelInput.files?.[0];
    excelInput.value = "";
    if (!file) return;
    const body = new FormData();
    body.append("file", file);
    setStatus("엑셀 불러오는 중…");
    state.extractReady = false;
    syncButtons();
    try {
      const res = await fetch("/api/workbook", { method: "POST", body });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.detail || data.message || "불러오기 실패");
      state.sessionId = data.sessionId;
      fileLabel.textContent = data.filename || file.name;
      fillSheets(data.sheets, data.activeSheet);
      renderPreview(data.preview);
      resultWrap.innerHTML = `<p class="empty">추출이 성공하면 일정 목록이 표시됩니다.</p>`;
      resultMeta.textContent = "";
      warningList.classList.add("hidden");
      setStatus("엑셀을 불러왔습니다. 시트를 확인한 뒤 「일정 추출」을 누르세요.");
      syncButtons();
    } catch (err) {
      state.sessionId = null;
      syncButtons();
      setStatus(err.message || String(err));
    }
  });

  sheetSelect.addEventListener("change", async () => {
    if (!state.sessionId) return;
    const body = new FormData();
    body.append("sessionId", state.sessionId);
    body.append("sheet", sheetSelect.value);
    setStatus("시트 전환 중…");
    state.extractReady = false;
    syncButtons();
    try {
      const res = await fetch("/api/workbook/sheet", { method: "POST", body });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.detail || "시트 전환 실패");
      renderPreview(data.preview);
      setStatus(`시트「${sheetSelect.value}」미리보기입니다. 「일정 추출」을 누르세요.`);
    } catch (err) {
      setStatus(err.message || String(err));
    }
  });

  btnExtract.addEventListener("click", async () => {
    if (!state.sessionId) return;
    const body = new FormData();
    body.append("sessionId", state.sessionId);
    body.append("sheet", sheetSelect.value || "");
    setStatus("일정 추출 중…");
    btnExtract.disabled = true;
    try {
      const res = await fetch("/api/extract", { method: "POST", body });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) throw new Error(data.detail || "추출 실패");
      state.extractReady = Boolean(data.extractReady);
      renderResult(data);
      setStatus(
        data.ok
          ? `추출 완료: ${data.itemCount}건. 「일정 데이터 내보내기」로 JSON을 받을 수 있습니다.`
          : "추출에 실패했습니다. 경고와 엑셀 표 형식을 확인하세요.",
      );
    } catch (err) {
      state.extractReady = false;
      setStatus(err.message || String(err));
    } finally {
      syncButtons();
    }
  });

  btnExport.addEventListener("click", () => {
    if (!state.sessionId || !state.extractReady) return;
    window.location.href = `/api/export?sessionId=${encodeURIComponent(state.sessionId)}`;
  });

  syncButtons();
})();
