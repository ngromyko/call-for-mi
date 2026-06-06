const state = {
  calls: [],
  activeCall: null,
  mobileView: "call",
  authMode: "login",
  auth: { authenticated: false, user: null, balanceClientId: null },
  config: { twilioEnabled: false, aiEnabled: false, readyForRealCalls: false },
  balance: { clientId: "", balance: 0 },
  startedAt: Date.now(),
  toastTimer: null,
  summaryRequests: new Set(),
  hub: null,
  subscribedCallId: null
};

const languages = {
  "ru-RU": "Русский",
  "uk-UA": "Українська",
  "en-US": "English",
  "pl-PL": "Polski",
  "de-DE": "Deutsch",
  "es-ES": "Español",
  "fr-FR": "Français",
  "it-IT": "Italiano",
  "cs-CZ": "Čeština"
};

const setupCommandTemplate = `dotnet user-secrets init --project src/CallForMe.Api
dotnet user-secrets set "Twilio:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:AccountSid" "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:AuthToken" "your_twilio_auth_token" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:FromNumber" "+1234567890" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:PublicBaseUrl" "https://your-public-url.example" --project src/CallForMe.Api
dotnet user-secrets set "AI:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "AI:ApiKey" "sk-..." --project src/CallForMe.Api`;

const $ = selector => document.querySelector(selector);

class ApiError extends Error {
  constructor(message, problem = null, status = 0) {
    super(message);
    this.name = "ApiError";
    this.problem = problem;
    this.status = status;
    this.errors = problem?.errors || {};
  }
}

async function api(url, options = {}) {
  const response = await fetch(url, {
    headers: { "Content-Type": "application/json", ...(options.headers || {}) },
    ...options
  });
  if (!response.ok) {
    let detail = "";
    let problem = null;
    try {
      problem = await response.json();
      detail = problem.detail || problem.title || "";
    } catch {
      detail = await response.text();
    }
    throw new ApiError(detail || `Ошибка ${response.status}`, problem, response.status);
  }
  return response.status === 204 ? null : response.json();
}

function languageName(code) {
  return languages[code] || code || "Авто";
}

function formatPhone(phone) {
  return phone || "Выберите номер";
}

function formatTime(value) {
  return new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", hour12: false }).format(new Date(value));
}

function formatDuration(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) {
    return "";
  }

  const total = Math.max(0, Math.round(seconds));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = total % 60;
  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, "0")}:${rest.toString().padStart(2, "0")}`;
  }

  return `${minutes}:${rest.toString().padStart(2, "0")}`;
}

function formatBalance(value) {
  const amount = Number(value || 0);
  return Number.isInteger(amount) ? String(amount) : amount.toFixed(2);
}

function getClientId() {
  if (state.auth?.authenticated && state.auth.balanceClientId) {
    return state.auth.balanceClientId;
  }

  const key = "callforme_client_id";
  let clientId = localStorage.getItem(key);
  if (!clientId) {
    clientId = crypto.randomUUID ? crypto.randomUUID() : `client-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    localStorage.setItem(key, clientId);
  }
  return clientId;
}

function callDurationSeconds(call) {
  if (!call) {
    return null;
  }

  if (Number.isFinite(call.durationSeconds)) {
    return call.durationSeconds;
  }

  if (isLive(call)) {
    return (Date.now() - new Date(call.createdAt).getTime()) / 1000;
  }

  if (call.createdAt && call.updatedAt) {
    const fallback = (new Date(call.updatedAt).getTime() - new Date(call.createdAt).getTime()) / 1000;
    return fallback > 0 ? fallback : null;
  }

  return null;
}

function escapeHtml(text = "") {
  return text.replace(/[&<>"']/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#039;" })[char]);
}

function normalizeForCompare(text = "") {
  return text.trim().replace(/\s+/g, " ").toLocaleLowerCase();
}

function shouldShowTranslation(entry) {
  if (!entry?.translation) {
    return false;
  }

  if (normalizeForCompare(entry.text) === normalizeForCompare(entry.translation)) {
    return false;
  }

  const call = state.activeCall;
  if (!call) {
    return true;
  }

  if (entry.speaker === "Remote" && call.language === call.userLanguage) {
    return false;
  }

  if (entry.speaker === "Assistant" && call.language === call.userLanguage) {
    return false;
  }

  return true;
}

function isFullTwilioAccountSid(value) {
  return /^AC[0-9a-fA-F]{32}$/.test(value);
}

function isLive(call) {
  return ["Created", "Queued", "Calling", "Ringing", "InProgress"].includes(call?.status);
}

function callStatusMeta(call) {
  const status = call?.status || "Created";
  if (status === "Completed") return { className: "complete", icon: "check_circle", text: "Завершён" };
  const failed = status === "Failed" || (!!call?.error && !isLive(call));
  if (failed) return { className: "failed", icon: "error", text: "Ошибка" };
  if (status === "Busy") return { className: "warning", icon: "phone_missed", text: "Занято" };
  if (status === "NoAnswer") return { className: "warning", icon: "phone_missed", text: "Не ответили" };
  if (status === "Canceled") return { className: "warning", icon: "cancel", text: "Отменён" };
  if (status === "Queued" || status === "Calling" || status === "Ringing") {
    return { className: "live", icon: "radio_button_checked", text: "Дозваниваемся" };
  }
  if (isLive(call)) return { className: "live", icon: "radio_button_checked", text: "Идёт разговор" };
  return { className: "warning", icon: "info", text: status };
}

function contactName(call) {
  if (!call) return "Нет активного звонка";
  if (call.displayName) return call.displayName;
  const prompt = call.prompt.toLowerCase();
  if (prompt.includes("врач") || prompt.includes("клиник")) return "Клиника";
  if (prompt.includes("банк")) return "Банк";
  if (prompt.includes("достав")) return "Служба доставки";
  if (prompt.includes("документ") || prompt.includes("карта")) return "Государственная служба";
  if (prompt.includes("статус") || prompt.includes("обращен")) return "Служба поддержки";
  return "Звонок";
}

function isFailedCall(call) {
  return call?.status === "Failed" || (!!call?.error && call?.status !== "Completed" && !isLive(call));
}

function upsertCall(call) {
  if (!call?.id) {
    return;
  }

  const index = state.calls.findIndex(candidate => candidate.id === call.id);
  if (index >= 0) {
    state.calls[index] = call;
  } else {
    state.calls.unshift(call);
  }

  state.calls.sort((left, right) => new Date(right.createdAt) - new Date(left.createdAt));
}

async function subscribeActiveCall(callId) {
  if (!state.hub || !callId || state.subscribedCallId === callId) {
    return;
  }

  try {
    if (state.subscribedCallId) {
      await state.hub.invoke("UnsubscribeCall", state.subscribedCallId);
    }
    await state.hub.invoke("SubscribeCall", callId);
    state.subscribedCallId = callId;
  } catch {
    state.subscribedCallId = null;
  }
}

async function startLiveUpdates() {
  const signalRClient = globalThis.signalR ||
    (typeof window !== "undefined" ? window.signalR : null) ||
    (typeof self !== "undefined" ? self.signalR : null);
  if (!signalRClient || state.hub) {
    return;
  }

  state.hub = new signalRClient.HubConnectionBuilder()
    .withUrl("/hubs/calls")
    .withAutomaticReconnect()
    .build();

  state.hub.on("CallUpdated", call => {
    upsertCall(call);
    if (state.activeCall?.id === call.id) {
      state.activeCall = call;
      if (isLive(call)) {
        state.startedAt = new Date(call.createdAt).getTime();
      }
      render();
      return;
    }
    renderHistory();
  });

  state.hub.on("TranscriptAdded", entry => {
    if (!state.activeCall?.id || state.activeCall.transcript?.some(item => item.id === entry.id)) {
      return;
    }

    state.activeCall.transcript = [...(state.activeCall.transcript || []), entry];
    upsertCall(state.activeCall);
    renderHeader();
    renderConversation();
  });

  state.hub.onreconnected(() => {
    if (state.activeCall?.id) {
      subscribeActiveCall(state.activeCall.id);
    }
  });

  try {
    await state.hub.start();
    if (state.activeCall?.id) {
      await subscribeActiveCall(state.activeCall.id);
    }
  } catch {
    state.hub = null;
  }
}

function renderSetupState() {
  const ready = state.config.readyForRealCalls;
  $("#setupWarning").hidden = ready;
  $("#newCallButton").disabled = !ready;
  $("#newCallButton").title = ready ? "" : (state.config.setupReason || "Сначала завершите настройки");
  $("#mobileNewCallButton").disabled = !ready;
  $("#mobileNewCallButton").title = ready ? "" : (state.config.setupReason || "Сначала завершите настройки");
  const setupText = $("#setupWarning p");
  if (setupText) setupText.textContent = state.config.setupReason || "Проверьте настройки перед звонком.";
}

function setMobileView(view) {
  state.mobileView = view === "history" ? "history" : "call";
  document.body.classList.toggle("mobile-view-history", state.mobileView === "history");
  document.body.classList.toggle("mobile-view-call", state.mobileView !== "history");
  document.querySelectorAll("[data-mobile-view]").forEach(button => {
    button.classList.toggle("active", button.dataset.mobileView === state.mobileView);
  });
}

function renderSettings() {
  const config = state.config;
  const twilioCredentialsInvalid = config.twilioCredentialsValid === false;
  const twilioReady = config.twilioEnabled && !twilioCredentialsInvalid;
  $("#twilioStatusCard").classList.toggle("ready", twilioReady);
  $("#twilioStatusCard").classList.toggle("missing", !twilioReady);
  $("#aiStatusCard").classList.toggle("ready", config.aiEnabled);
  $("#aiStatusCard").classList.toggle("missing", !config.aiEnabled);
  $("#twilioStatusText").textContent = twilioCredentialsInvalid
    ? "Ключи не прошли проверку"
    : (config.twilioEnabled ? "Поля заполнены" : "Не хватает параметров");
  $("#aiStatusText").textContent = config.aiEnabled ? `Готово, модель ${config.aiModel || "по умолчанию"}` : "Не хватает ключа";

  const missing = [...(config.twilioMissing || []), ...(config.aiMissing || [])];
  $("#missingBox").innerHTML = missing.length
    ? `<strong>Не хватает:</strong> ${missing.map(item => `<code>${escapeHtml(item)}</code>`).join(" ")}`
    : twilioCredentialsInvalid
      ? `<strong>Twilio не готов:</strong> Account SID/Auth Token не прошли проверку. Вставьте правильный Auth Token и нажмите «Проверить Twilio».`
      : `<strong>Готово:</strong> можно запускать реальные звонки.`;
  $("#setupCommands").textContent = setupCommandTemplate;
  $("#openAiModelInput").value = config.aiModel || "gpt-5.4-mini";
  $("#twilioAccountSidHint").textContent = config.accountSid ? `Сейчас сохранён: ${config.accountSid}. Для изменения вставьте полный SID.` : "";
  $("#twilioFromNumberInput").value = config.fromNumber || "";
  $("#twilioPublicBaseUrlInput").value = config.publicBaseUrl || "";
  $("#adminLogoutButton").hidden = !config.adminAuthenticated;
}

function renderBalance() {
  $("#balanceAmount").textContent = formatBalance(state.balance.balance);
}

function renderAuth() {
  const authenticated = !!state.auth?.authenticated;
  $("#accountName").textContent = authenticated ? state.auth.user.username : "Гость";
  $("#openAuthButton").hidden = authenticated;
  $("#logoutButton").hidden = !authenticated;
}

function renderPromoCodes(promoCodes = []) {
  const list = $("#promoAdminList");
  if (!list) return;
  list.innerHTML = promoCodes.length ? promoCodes.map(code => {
    const status = code.active ? "Активен" : "Отключён";
    const limit = code.maxRedemptions ? `${code.redemptionCount}/${code.maxRedemptions}` : `${code.redemptionCount}`;
    return `<article class="promo-admin-item ${code.active ? "" : "disabled"}">
      <div>
        <strong>${escapeHtml(code.code)}</strong>
        <span>+${escapeHtml(formatBalance(code.amount))} баланса · активации ${escapeHtml(limit)}</span>
      </div>
      <button type="button" class="secondary-button" data-toggle-promo-id="${code.id}" data-active="${code.active ? "false" : "true"}">
        <span class="material-symbols-rounded">${code.active ? "block" : "check_circle"}</span>
        ${status}
      </button>
    </article>`;
  }).join("") : `<div class="empty-history">Промокодов пока нет.</div>`;
}

function renderHistory() {
  if (!state.calls.length) {
    $("#callHistory").innerHTML = `
      <div class="history-group-label">История пуста</div>
      <div class="empty-history">Здесь появятся только реальные звонки.</div>`;
    return;
  }

  $("#callHistory").innerHTML = `<div class="history-group-label">Сегодня</div>` + state.calls.slice(0, 8).map(call => {
    const live = isLive(call);
    const active = call.id === state.activeCall?.id;
    const status = callStatusMeta(call);
    const duration = formatDuration(callDurationSeconds(call));
    return `<article class="history-item ${active ? "active" : ""}" data-call-id="${call.id}">
      <button class="history-main" type="button" data-select-call-id="${call.id}">
        <span class="history-icon material-symbols-rounded">${live ? "phone_in_talk" : "call"}</span>
        <span class="history-details">
          <strong>${escapeHtml(contactName(call))}</strong>
          <span>${escapeHtml(formatPhone(call.phoneNumber))}</span>
          <span>${escapeHtml(call.prompt)}</span>
          <span class="history-status ${status.className}"><span class="material-symbols-rounded">${status.icon}</span>${escapeHtml(status.text)}</span>
        </span>
        <span class="history-meta">
          <span class="history-time">${formatTime(call.createdAt)}</span>
          ${duration ? `<span class="history-duration">${escapeHtml(duration)}</span>` : ""}
        </span>
      </button>
      <button class="history-menu-button material-symbols-rounded" type="button" data-menu-call-id="${call.id}" aria-label="Меню звонка" aria-expanded="false">more_vert</button>
      <div class="history-menu" data-menu-for="${call.id}" hidden>
        <button type="button" data-hide-call-id="${call.id}">
          <span class="material-symbols-rounded">visibility_off</span>
          Скрыть звонок
        </button>
      </div>
    </article>`;
  }).join("");
}

function renderActiveHistoryDuration() {
  if (!state.activeCall?.id) {
    return;
  }

  const activeItem = document.querySelector(`[data-call-id="${CSS.escape(state.activeCall.id)}"]`);
  const duration = activeItem?.querySelector(".history-duration");
  const nextText = formatDuration(callDurationSeconds(state.activeCall));
  if (duration && nextText && duration.textContent !== nextText) {
    duration.textContent = nextText;
  }
}

function renderHeader() {
  const call = state.activeCall;
  const status = callStatusMeta(call);
  const live = isLive(call);
  const latest = call?.transcript?.at(-1);
  const remoteSpeaking = live && latest?.speaker === "Remote";
  document.body.classList.toggle("has-active-call", !!call);
  document.body.classList.toggle("is-live-call", live);
  document.body.classList.toggle("remote-speaking", remoteSpeaking);
  document.body.classList.remove("call-status-dialing", "call-status-live", "call-status-complete", "call-status-failed", "call-status-idle");
  const statusClass = !call
    ? "call-status-idle"
    : isFailedCall(call) || ["Busy", "NoAnswer", "Canceled"].includes(call.status)
      ? "call-status-failed"
      : call.status === "Completed"
        ? "call-status-complete"
        : call.status === "InProgress"
          ? "call-status-live"
          : "call-status-dialing";
  document.body.classList.add(statusClass);
  $("#contactEyebrow").textContent = call
    ? (live ? "Активный звонок" : "Прошлый звонок")
    : "Панель ожидания";
  $("#contactAvatarIcon").textContent = call ? "phone_in_talk" : "add_box";
  $("#contactName").textContent = contactName(call);
  $("#contactNumber").textContent = call?.phoneNumber ? formatPhone(call.phoneNumber) : "Нажмите «Новый звонок», когда будете готовы";
  $("#callGoal").textContent = isFailedCall(call)
    ? call.error
    : call?.prompt || (state.config.readyForRealCalls ? "Сначала выберите номер и цель звонка" : (state.config.setupReason || "Нужно завершить настройки"));
  $("#statusLabel").textContent = call ? status.text : "Ожидание";
  $("#autopilotToggle").checked = !!call?.autoPilot;
  $("#autopilotToggle").disabled = !call || !live;
  $("#messageInput").disabled = !call || !live;
  $("#endCallButton").disabled = !call || !live;
  $("#languageBadgeText").innerHTML = call
    ? `Язык звонка<br>${escapeHtml(languageName(call.language))}`
    : "Создайте звонок<br>когда будете готовы";
  $("#translationStatusText").textContent = call
    ? (remoteSpeaking
      ? "Собеседник говорит. ИИ готовит варианты ответа."
      : call.userLanguage === call.language
        ? `Без перевода: ${languageName(call.userLanguage)}`
        : `Перевод: ${languageName(call.userLanguage)} → ${languageName(call.language)}`)
    : "Ответы и перевод появятся только после старта звонка";
  $("#messageInput").placeholder = call
    ? `Напишите ответ: ${languageName(call.userLanguage)}`
    : "Сначала начните реальный звонок";
}

function messageMarkup(entry) {
  if (entry.speaker === "System") {
    return `<div class="system-message">${escapeHtml(entry.text)}</div>`;
  }
  const assistant = entry.speaker === "Assistant";
  const showTranslation = shouldShowTranslation(entry);
  return `<div class="message-row ${assistant ? "assistant" : "remote"}">
    ${assistant ? "" : `<span class="speaker-avatar material-symbols-rounded">person</span>`}
    <div class="message-wrap">
      <div class="message-meta"><span>${assistant ? "Вы через ИИ" : "Собеседник"}</span><time>${formatTime(entry.timestamp)}</time></div>
      <div class="message-bubble">
        <div class="message-original">${escapeHtml(entry.text)}</div>
        ${showTranslation ? `<div class="message-translation">${escapeHtml(entry.translation)}</div>` : ""}
      </div>
    </div>
  </div>`;
}

function callSummaryMarkup(call) {
  if (!call || isLive(call) || !(call.transcript || []).length) {
    return "";
  }

  const summary = call.summary;
  if (!summary) {
    return `<section class="call-summary loading">
      <div class="summary-icon material-symbols-rounded">auto_awesome</div>
      <div>
        <span class="eyebrow">Короткий итог</span>
        <strong>ИИ готовит summary...</strong>
        <p>Сейчас появится аккуратный итог разговора.</p>
      </div>
    </section>`;
  }

  return `<section class="call-summary">
    <div class="summary-icon material-symbols-rounded">summarize</div>
    <div>
      <span class="eyebrow">Короткий итог</span>
      <strong>${escapeHtml(summary.outcome || "Звонок завершён")}</strong>
      <p>${escapeHtml(summary.keyPoint || "Summary сохранён в истории.")}</p>
      <div class="summary-stats">
        ${summary.tone ? `<span>${escapeHtml(summary.tone)}</span>` : ""}
        ${call.durationSeconds ? `<span>${Math.max(1, Math.round(call.durationSeconds / 60))} мин.</span>` : ""}
      </div>
      ${summary.nextStep ? `<small>${escapeHtml(summary.nextStep)}</small>` : ""}
    </div>
  </section>`;
}

function renderPinnedSummary(call, transcript) {
  const summary = $("#pinnedSummary");
  const markup = callSummaryMarkup(call);
  summary.hidden = !markup;
  summary.innerHTML = markup;
  document.body.classList.toggle("has-pinned-summary", !!markup);
  if (markup && !call?.summary) {
    ensureCallSummary(call);
  }
}

function renderConversation() {
  const call = state.activeCall;
  const transcript = call?.transcript || [];
  const emptyTitle = state.config.readyForRealCalls ? "Готов к реальному звонку" : "Реальный режим требует настройки";
  const emptyText = state.config.readyForRealCalls
    ? "Здесь появится разговор после старта. Пока звонок не начат, никакой линии нет."
    : `${state.config.setupReason || "Проверьте настройки."} Откройте «Настройки» слева внизу.`;

  renderPinnedSummary(call, transcript);

  $("#conversation").innerHTML = transcript.length ? transcript.map(messageMarkup).join("") : `
    <div class="empty-state">
      <div><span class="material-symbols-rounded">phone_in_talk</span><strong>${emptyTitle}</strong><span>${emptyText}</span></div>
    </div>`;
  $("#conversation").scrollTop = $("#conversation").scrollHeight;

  const suggestions = Array.isArray(call?.suggestions) ? call.suggestions : [];
  $("#suggestions").innerHTML = suggestions.map((item, index) => {
    const suggestion = typeof item === "string" ? { text: item, spokenText: item } : item;
    const icon = ["calendar_month", "schedule", "event_available"][index] || "chat";
    return `<button class="suggestion-button" data-text="${escapeHtml(suggestion.text)}" data-spoken="${escapeHtml(suggestion.spokenText)}">
      <span class="material-symbols-rounded">${icon}</span><span>${escapeHtml(suggestion.text)}</span>
    </button>`;
  }).join("");
}

function render() {
  renderSetupState();
  renderSettings();
  renderAuth();
  renderBalance();
  renderHistory();
  renderHeader();
  renderConversation();
  setMobileView(state.mobileView);
}

async function loadConfig() {
  state.config = await api("/api/config");
}

async function loadAuth() {
  state.auth = await api("/api/auth/me");
}

async function loadBalance() {
  state.balance.clientId = getClientId();
  state.balance = await api(`/api/balance/${encodeURIComponent(state.balance.clientId)}`);
}

async function redeemPromoCode(code) {
  state.balance.clientId ||= getClientId();
  state.balance = await api("/api/promocodes/redeem", {
    method: "POST",
    body: JSON.stringify({ clientId: state.balance.clientId, code })
  });
  renderBalance();
}

async function loadPromoCodes() {
  const promoCodes = await api("/api/admin/promocodes");
  renderPromoCodes(promoCodes);
}

async function createPromoCode() {
  const code = $("#adminPromoCodeInput").value.trim();
  const amount = Number($("#adminPromoAmountInput").value);
  const maxRedemptionsValue = $("#adminPromoLimitInput").value.trim();
  const payload = {
    code,
    amount,
    maxRedemptions: maxRedemptionsValue ? Number(maxRedemptionsValue) : null
  };
  await api("/api/admin/promocodes", {
    method: "POST",
    body: JSON.stringify(payload)
  });
  $("#adminPromoCodeInput").value = "";
  $("#adminPromoLimitInput").value = "";
  await loadPromoCodes();
}

async function togglePromoCode(id, active) {
  await api(`/api/admin/promocodes/${id}`, {
    method: "PATCH",
    body: JSON.stringify({ active })
  });
  await loadPromoCodes();
}

function setAuthMode(mode) {
  state.authMode = mode === "register" ? "register" : "login";
  $("#authTitle").textContent = state.authMode === "register" ? "Регистрация" : "Вход";
  $("#authSubmitButton span:last-child").textContent = state.authMode === "register" ? "Зарегистрироваться" : "Войти";
  $("#authSubmitButton .material-symbols-rounded").textContent = state.authMode === "register" ? "person_add" : "login";
  $("#authModeLogin").classList.toggle("active", state.authMode === "login");
  $("#authModeRegister").classList.toggle("active", state.authMode === "register");
  $("#authPasswordInput").autocomplete = state.authMode === "register" ? "new-password" : "current-password";
}

function clearAuthErrors() {
  $("#authErrorSummary").hidden = true;
  $("#authErrorSummary").textContent = "";
}

function openAuthDialog(mode = "login") {
  setAuthMode(mode);
  clearAuthErrors();
  $("#authUsernameInput").value = "";
  $("#authPasswordInput").value = "";
  $("#authDialog").showModal();
  setTimeout(() => $("#authUsernameInput").focus(), 0);
}

async function submitAuth() {
  clearAuthErrors();
  const username = $("#authUsernameInput").value.trim();
  const password = $("#authPasswordInput").value;
  if (!username || !password) {
    $("#authErrorSummary").textContent = "Введите username и пароль.";
    $("#authErrorSummary").hidden = false;
    return;
  }

  const endpoint = state.authMode === "register" ? "/api/auth/register" : "/api/auth/login";
  state.auth = await api(endpoint, {
    method: "POST",
    body: JSON.stringify({ username, password })
  });
  await loadBalance();
  await loadCalls(state.activeCall?.id);
  render();
}

async function logoutUser() {
  await api("/api/auth/logout", { method: "POST" });
  state.auth = { authenticated: false, user: null, balanceClientId: null };
  await loadBalance();
  await loadCalls();
  render();
}

async function refreshAdminState() {
  const status = await api("/api/admin/status");
  state.config.adminConfigured = status.configured;
  state.config.adminAuthenticated = status.authenticated;
}

async function saveOpenAiSettings() {
  const apiKey = $("#openAiKeyInput").value.trim();
  const model = $("#openAiModelInput").value.trim();
  if (!apiKey) {
    showToast("Вставьте OpenAI API key");
    return;
  }

  try {
    await api("/api/config/openai", {
      method: "POST",
      body: JSON.stringify({ apiKey, model })
    });
    $("#openAiKeyInput").value = "";
    await loadConfig();
    render();
    showToast("OpenAI key сохранён локально");
  } catch (error) {
    if (error.status === 401) {
      openAdminDialog();
      return;
    }
    showToast(error.message || "Не удалось сохранить OpenAI key");
  }
}

async function saveTwilioSettings() {
  const accountSid = $("#twilioAccountSidInput").value;
  const authToken = $("#twilioAuthTokenInput").value;
  if (accountSid.includes("…") || accountSid.includes("...")) {
    $("#twilioAccountSidInput").value = "";
    showToast("Маску SID отправлять нельзя. Поле очищено, сохранённый полный SID останется.");
    return;
  }
  if (accountSid && !isFullTwilioAccountSid(accountSid)) {
    showToast("Если меняете Account SID, вставьте полный ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx");
    return;
  }
  if (!accountSid && !state.config.accountSid) {
    showToast("Вставьте полный Account SID");
    return;
  }
  if (!authToken && !state.config.hasAuthToken) {
    showToast("Вставьте Twilio Auth Token");
    return;
  }

  const payload = {
    accountSid,
    authToken,
    fromNumber: $("#twilioFromNumberInput").value.trim(),
    publicBaseUrl: $("#twilioPublicBaseUrlInput").value.trim()
  };
  if (!payload.fromNumber || !payload.publicBaseUrl) {
    showToast("Заполните Twilio номер и Public URL");
    return;
  }

  try {
    await api("/api/config/twilio", {
      method: "POST",
      body: JSON.stringify(payload)
    });
    $("#twilioAuthTokenInput").value = "";
    await loadConfig();
    render();
    showToast("Twilio сохранён. Нажмите «Проверить Twilio»");
  } catch (error) {
    if (error.status === 401) {
      openAdminDialog();
      return;
    }
    showToast(error.message || "Не удалось сохранить Twilio");
  }
}

async function checkTwilioSettings() {
  try {
    await api("/api/config/twilio/check", { method: "POST" });
    await new Promise(resolve => setTimeout(resolve, 1200));
    await loadConfig();
    render();
    showToast("Twilio SID/Auth Token валидны");
  } catch (error) {
    if (error.status === 401) {
      openAdminDialog();
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 1200));
    await loadConfig().catch(() => {});
    render();
    showToast(error.message || "Twilio не принял SID/Auth Token");
  }
}

async function loadCalls(selectId = null) {
  try {
    state.calls = await api("/api/calls");
    if (selectId) {
      state.activeCall = state.calls.find(call => call.id === selectId) || state.activeCall;
    } else if (state.activeCall?.id) {
      state.activeCall = state.calls.find(call => call.id === state.activeCall.id) || state.activeCall;
    } else {
      state.activeCall = state.calls.find(isLive) || null;
    }
    if (state.activeCall?.id) {
      subscribeActiveCall(state.activeCall.id);
    }
    render();
  } catch {
    showToast("Не удалось обновить звонки");
  }
}

async function sendMessage(text, spokenText = null) {
  if (!state.activeCall?.id) {
    showToast("Сначала начните реальный звонок");
    return;
  }
  try {
    state.activeCall = await api(`/api/calls/${state.activeCall.id}/messages`, {
      method: "POST",
      body: JSON.stringify({ text, spokenText })
    });
    render();
  } catch (error) {
    showToast(error.message || "Не удалось отправить ответ");
  }
}

async function ensureCallSummary(call) {
  if (!call?.id || isLive(call) || call.summary || !(call.transcript || []).length || state.summaryRequests.has(call.id)) {
    return;
  }

  state.summaryRequests.add(call.id);
  try {
    state.activeCall = await api(`/api/calls/${call.id}/summary`, { method: "POST" });
    upsertCall(state.activeCall);
    render();
  } catch (error) {
    showToast(error.message || "Не удалось подготовить summary");
  } finally {
    state.summaryRequests.delete(call.id);
  }
}

async function selectCall(id) {
  try {
    state.activeCall = await api(`/api/calls/${id}`);
    state.startedAt = new Date(state.activeCall.createdAt).getTime();
    await subscribeActiveCall(id);
    setMobileView("call");
    render();
  } catch {
    showToast("Не удалось открыть звонок");
  }
}

function closeHistoryMenus() {
  document.querySelectorAll(".history-menu:not([hidden])").forEach(menu => {
    menu.hidden = true;
  });
  document.querySelectorAll(".history-menu-button[aria-expanded='true']").forEach(button => {
    button.setAttribute("aria-expanded", "false");
  });
}

function toggleHistoryMenu(id) {
  const menu = document.querySelector(`[data-menu-for="${CSS.escape(id)}"]`);
  const button = document.querySelector(`[data-menu-call-id="${CSS.escape(id)}"]`);
  if (!menu || !button) return;
  const willOpen = menu.hidden;
  closeHistoryMenus();
  menu.hidden = !willOpen;
  button.setAttribute("aria-expanded", String(willOpen));
}

async function hideCall(id) {
  try {
    await api(`/api/calls/${id}/hide`, { method: "POST" });
    const wasActive = state.activeCall?.id === id;
    if (state.activeCall?.id === id) {
      state.activeCall = null;
    }
    state.calls = state.calls.filter(call => call.id !== id);
    closeHistoryMenus();
    if (wasActive) {
      render();
    } else {
      renderHistory();
    }
    showToast("Звонок скрыт из истории");
  } catch (error) {
    showToast(error.message || "Не удалось скрыть звонок");
  }
}

function showToast(message) {
  const toast = $("#toast");
  toast.textContent = message;
  toast.classList.add("visible");
  clearTimeout(state.toastTimer);
  state.toastTimer = setTimeout(() => toast.classList.remove("visible"), 4200);
}

function clearNewCallErrors() {
  $("#newCallErrorSummary").hidden = true;
  $("#newCallErrorSummary").textContent = "";
  document.querySelectorAll("#newCallForm .field-error").forEach(error => {
    error.textContent = "";
  });
  document.querySelectorAll("#newCallForm .field-invalid").forEach(field => {
    field.classList.remove("field-invalid");
    field.removeAttribute("aria-invalid");
  });
}

function setFieldError(fieldName, messages) {
  const input = $(`#newCallForm [name="${fieldName}"]`);
  const error = $(`#newCallForm [data-error-for="${fieldName}"]`);
  const text = Array.isArray(messages) ? messages.join(" ") : String(messages || "");
  if (input) {
    input.classList.add("field-invalid");
    input.setAttribute("aria-invalid", "true");
  }
  if (error) {
    error.textContent = text;
  }
  return text;
}

function showNewCallErrors(error) {
  clearNewCallErrors();
  const summary = $("#newCallErrorSummary");
  const errors = error?.errors || {};
  const shown = [];

  for (const [key, messages] of Object.entries(errors)) {
    const normalized = key.charAt(0).toLowerCase() + key.slice(1);
    shown.push(setFieldError(normalized, messages));
  }

  const message = shown.filter(Boolean)[0] || error?.message || "Проверьте номер, цель и настройки";
  summary.textContent = message;
  summary.hidden = false;
}

function clearAdminErrors() {
  $("#adminErrorSummary").hidden = true;
  $("#adminErrorSummary").textContent = "";
  $("#adminPasswordError").textContent = "";
  $("#adminPasswordInput").classList.remove("field-invalid");
  $("#adminPasswordInput").removeAttribute("aria-invalid");
}

function showAdminError(message) {
  $("#adminErrorSummary").textContent = message;
  $("#adminErrorSummary").hidden = false;
  $("#adminPasswordError").textContent = message;
  $("#adminPasswordInput").classList.add("field-invalid");
  $("#adminPasswordInput").setAttribute("aria-invalid", "true");
}

function openAdminDialog() {
  clearAdminErrors();
  $("#settingsDialog").close();
  $("#adminPasswordInput").value = "";
  $("#adminDialog").showModal();
  setTimeout(() => $("#adminPasswordInput").focus(), 0);
}

async function openSettingsProtected() {
  try {
    await refreshAdminState();
    if (state.config.adminAuthenticated) {
      await loadConfig();
      render();
      await loadPromoCodes().catch(() => {});
      $("#settingsDialog").showModal();
      return;
    }

    openAdminDialog();
  } catch {
    showToast("Не удалось проверить admin доступ");
  }
}

async function loginAdmin(password) {
  await api("/api/admin/login", {
    method: "POST",
    body: JSON.stringify({ password })
  });
  await loadConfig();
  render();
}

async function logoutAdmin() {
  await api("/api/admin/logout", { method: "POST" });
  await loadConfig();
  render();
}

function openNewCallDialog() {
  if (!state.config.readyForRealCalls) {
    showToast(state.config.setupReason || "Завершите настройки перед реальным звонком");
    return;
  }
  clearNewCallErrors();
  $("#newCallDialog").showModal();
}

$("#newCallButton").addEventListener("click", openNewCallDialog);
$("#mobileNewCallButton").addEventListener("click", openNewCallDialog);
document.querySelectorAll("[data-mobile-view]").forEach(button => {
  button.addEventListener("click", () => setMobileView(button.dataset.mobileView));
});
$("#mobileSettingsNav").addEventListener("click", openSettingsProtected);
$("#closeNewCallDialog").addEventListener("click", () => {
  clearNewCallErrors();
  $("#newCallDialog").close();
});
$("#cancelNewCallDialog").addEventListener("click", () => {
  clearNewCallErrors();
  $("#newCallDialog").close();
});
$("#openSettingsButton").addEventListener("click", openSettingsProtected);
$("#settingsSidebarButton").addEventListener("click", openSettingsProtected);
$("#refreshConfigButton").addEventListener("click", async () => {
  try {
    await loadConfig();
    render();
    showToast("Статус настроек обновлён");
  } catch {
    showToast("Не удалось проверить настройки");
  }
});
$("#saveOpenAiButton").addEventListener("click", saveOpenAiSettings);
$("#saveTwilioButton").addEventListener("click", saveTwilioSettings);
$("#checkTwilioButton").addEventListener("click", checkTwilioSettings);
$("#checkTwilioTopButton").addEventListener("click", checkTwilioSettings);
$("#adminLogoutButton").addEventListener("click", async () => {
  try {
    await logoutAdmin();
    $("#settingsDialog").close();
    showToast("Admin сессия закрыта");
  } catch {
    showToast("Не удалось выйти из admin режима");
  }
});
$("#adminLoginForm").addEventListener("submit", async event => {
  event.preventDefault();
  clearAdminErrors();
  const password = $("#adminPasswordInput").value;
  if (!password) {
    showAdminError("Введите admin пароль.");
    return;
  }

  const button = $("#adminLoginButton");
  const label = button.querySelector("span:last-child");
  button.disabled = true;
  label.textContent = "Проверяем...";
  try {
    await loginAdmin(password);
    $("#adminDialog").close();
    await loadPromoCodes().catch(() => {});
    $("#settingsDialog").showModal();
    showToast("Admin доступ открыт");
  } catch (error) {
    showAdminError(error.message || "Неверный admin пароль.");
  } finally {
    button.disabled = false;
    label.textContent = "Войти";
  }
});
$("#closeAdminDialog").addEventListener("click", () => {
  clearAdminErrors();
  $("#adminDialog").close();
});
$("#cancelAdminDialog").addEventListener("click", () => {
  clearAdminErrors();
  $("#adminDialog").close();
});
$("#helpButton").addEventListener("click", () => $("#helpDialog").showModal());
$("#openAuthButton").addEventListener("click", () => openAuthDialog("login"));
$("#logoutButton").addEventListener("click", async () => {
  try {
    await logoutUser();
    showToast("Вы вышли из аккаунта");
  } catch {
    showToast("Не удалось выйти");
  }
});
$("#authModeLogin").addEventListener("click", () => setAuthMode("login"));
$("#authModeRegister").addEventListener("click", () => setAuthMode("register"));
$("#closeAuthDialog").addEventListener("click", () => $("#authDialog").close());
$("#cancelAuthDialog").addEventListener("click", () => $("#authDialog").close());
$("#authForm").addEventListener("submit", async event => {
  event.preventDefault();
  const button = $("#authSubmitButton");
  button.disabled = true;
  try {
    await submitAuth();
    $("#authDialog").close();
    showToast(state.authMode === "register" ? "Аккаунт создан" : "Вы вошли");
  } catch (error) {
    $("#authErrorSummary").textContent = error.message || "Не удалось войти.";
    $("#authErrorSummary").hidden = false;
  } finally {
    button.disabled = false;
  }
});
$("#redeemPromoForm").addEventListener("submit", async event => {
  event.preventDefault();
  const input = $("#promoCodeInput");
  const code = input.value.trim();
  if (!code) {
    showToast("Введите промокод");
    return;
  }

  try {
    await redeemPromoCode(code);
    input.value = "";
    showToast("Промокод применён");
  } catch (error) {
    showToast(error.message || "Промокод не применён");
  }
});
$("#createPromoButton")?.addEventListener("click", async () => {
  try {
    await createPromoCode();
    showToast("Промокод сохранён");
  } catch (error) {
    showToast(error.message || "Не удалось сохранить промокод");
  }
});
$("#promoAdminList")?.addEventListener("click", async event => {
  const button = event.target.closest("[data-toggle-promo-id]");
  if (!button) return;
  try {
    await togglePromoCode(button.dataset.togglePromoId, button.dataset.active === "true");
    showToast("Промокод обновлён");
  } catch (error) {
    showToast(error.message || "Не удалось обновить промокод");
  }
});
$("#dismissTip")?.addEventListener("click", () => $("#precallTip").classList.add("hidden"));
$("#refreshButton").addEventListener("click", () => loadCalls(state.activeCall?.id));
$("#callHistory").addEventListener("click", event => {
  const menuButton = event.target.closest("[data-menu-call-id]");
  if (menuButton) {
    event.stopPropagation();
    toggleHistoryMenu(menuButton.dataset.menuCallId);
    return;
  }

  const hideButton = event.target.closest("[data-hide-call-id]");
  if (hideButton) {
    event.stopPropagation();
    hideCall(hideButton.dataset.hideCallId);
    return;
  }

  const selectButton = event.target.closest("[data-select-call-id]");
  if (selectButton) {
    closeHistoryMenus();
    selectCall(selectButton.dataset.selectCallId);
  }
});
document.addEventListener("click", event => {
  if (!event.target.closest("#callHistory")) {
    closeHistoryMenus();
  }
});
$("#suggestions").addEventListener("click", event => {
  const button = event.target.closest("[data-text]");
  if (button) sendMessage(button.dataset.text, button.dataset.spoken);
});
$(".tip-chips")?.addEventListener("click", event => {
  const button = event.target.closest("[data-fill]");
  if (!button || $("#messageInput").disabled) return;
  $("#messageInput").value = button.dataset.fill;
  $("#messageInput").focus();
});
$("#messageForm").addEventListener("submit", event => {
  event.preventDefault();
  const text = $("#messageInput").value.trim();
  if (!text) return;
  $("#messageInput").value = "";
  sendMessage(text);
});
$("#autopilotToggle").addEventListener("change", async event => {
  const enabled = event.target.checked;
  if (!state.activeCall?.id) {
    event.target.checked = false;
    showToast("Сначала начните реальный звонок");
    return;
  }
  try {
    state.activeCall = await api(`/api/calls/${state.activeCall.id}/autopilot`, {
      method: "POST",
      body: JSON.stringify({ enabled })
    });
    showToast(enabled ? "ИИ будет отвечать самостоятельно" : "ИИ будет ждать вашего решения");
  } catch {
    event.target.checked = !enabled;
    showToast("Не удалось изменить режим");
  }
});
$("#endCallButton").addEventListener("click", async () => {
  if (!state.activeCall?.id) {
    showToast("Нет активного звонка");
    return;
  }
  try {
    state.activeCall = await api(`/api/calls/${state.activeCall.id}/end`, { method: "POST" });
    render();
    showToast("Звонок завершён");
  } catch {
    showToast("Не удалось завершить звонок");
  }
});
$("#newCallForm").addEventListener("submit", async event => {
  event.preventDefault();
  clearNewCallErrors();
  const data = new FormData(event.currentTarget);
  const startButton = $("#startCallButton");
  const startButtonLabel = startButton?.querySelector("span:last-child");
  if (startButton) startButton.disabled = true;
  if (startButtonLabel) startButtonLabel.textContent = "Дозваниваемся...";
  const payload = {
    phoneNumber: String(data.get("phoneNumber") || "").replace(/\s/g, ""),
    displayName: String(data.get("displayName") || ""),
    prompt: String(data.get("prompt") || ""),
    userLanguage: String(data.get("userLanguage") || ""),
    language: String(data.get("language") || ""),
    autoPilot: data.get("autoPilot") === "on"
  };
  const localErrors = {};
  if (!/^\+[1-9]\d{7,14}$/.test(payload.phoneNumber)) {
    localErrors.phoneNumber = ["Введите номер в формате +48123456789."];
  }
  if (payload.prompt.trim().length < 10) {
    localErrors.prompt = ["Опишите цель хотя бы в 10 символов."];
  }
  if (Object.keys(localErrors).length) {
    showNewCallErrors(new ApiError("Проверьте поля", { errors: localErrors }, 400));
    if (startButton) startButton.disabled = false;
    if (startButtonLabel) startButtonLabel.textContent = "Позвонить за меня";
    return;
  }
  try {
    const call = await api("/api/calls", { method: "POST", body: JSON.stringify(payload) });
    state.activeCall = call;
    upsertCall(call);
    state.startedAt = Date.now();
    await subscribeActiveCall(call.id);
    $("#newCallDialog").close();
    setMobileView("call");
    render();
    showToast("Реальный звонок запущен");
  } catch (error) {
    showNewCallErrors(error);
    showToast(error.message || "Проверьте номер, цель и настройки");
  } finally {
    if (startButton) startButton.disabled = false;
    if (startButtonLabel) startButtonLabel.textContent = "Позвонить за меня";
  }
});

setInterval(() => {
  if (!isLive(state.activeCall)) {
    $("#callTimer").textContent = "00:00";
    return;
  }
  const elapsed = Math.max(0, Date.now() - state.startedAt);
  const minutes = Math.floor(elapsed / 60000).toString().padStart(2, "0");
  const seconds = Math.floor((elapsed % 60000) / 1000).toString().padStart(2, "0");
  $("#callTimer").textContent = `${minutes}:${seconds}`;
  renderActiveHistoryDuration();
}, 1000);

(async function boot() {
  try {
    await loadConfig();
    await loadAuth().catch(() => {});
    await refreshAdminState().catch(() => {});
    await loadBalance().catch(() => {});
    await loadCalls();
    await startLiveUpdates();
    setMobileView("call");
  } catch {
    showToast("Не удалось загрузить конфигурацию");
    render();
  }
})();
