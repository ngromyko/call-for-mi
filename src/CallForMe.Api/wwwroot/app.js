const state = {
  calls: [],
  activeCall: null,
  mobileView: (window.location?.pathname || "/").replace(/\/+$/, "").toLowerCase() === "/admin" ? "admin" : "call",
  authMode: "login",
  auth: { authenticated: false, user: null, balanceClientId: null },
  config: { twilioEnabled: false, aiEnabled: false, readyForRealCalls: false },
  balance: { clientId: "", balance: 0 },
  adminUsers: [],
  tonPayments: [],
  adminTonPayments: [],
  startedAt: Date.now(),
  toastTimer: null,
  summaryRequests: new Set(),
  lastActivePollAt: 0,
  hub: null,
  subscribedCallId: null
};

const demoStartedAt = Date.now() - 9 * 60 * 1000;

const demoCall = {
  id: "demo-call",
  displayName: "Пример звонка",
  phoneNumber: "+48123456789",
  prompt: "Уточнить свободное время для записи и попросить подтверждение по SMS.",
  language: "pl-PL",
  userLanguage: "ru-RU",
  autoPilot: false,
  status: "Completed",
  createdAt: new Date(demoStartedAt).toISOString(),
  updatedAt: new Date(demoStartedAt + 2 * 60 * 1000).toISOString(),
  durationSeconds: 74,
  transcript: [
    {
      id: "demo-1",
      speaker: "Assistant",
      text: "Здравствуйте. Я звоню от имени клиента, чтобы узнать доступное время для записи.",
      translation: "Dzien dobry. Dzwonie w imieniu klienta, aby zapytac o wolny termin.",
      timestamp: new Date(demoStartedAt).toISOString()
    },
    {
      id: "demo-2",
      speaker: "Remote",
      text: "Mamy wolne miejsce jutro o 10:30 albo w piatek po poludniu.",
      translation: "Есть свободное место завтра в 10:30 или в пятницу после обеда.",
      timestamp: new Date(demoStartedAt + 60 * 1000).toISOString()
    },
    {
      id: "demo-3",
      speaker: "Assistant",
      text: "Подтвердите, пожалуйста, пятницу после обеда и отправьте SMS с адресом.",
      translation: "Prosze potwierdzic piatek po poludniu i wyslac SMS z adresem.",
      timestamp: new Date(demoStartedAt + 2 * 60 * 1000).toISOString()
    }
  ],
  suggestions: [],
  summary: {
    title: "Запись согласована",
    outcome: "Служба предложила два времени. Выбран вариант в пятницу после обеда.",
    nextSteps: ["Дождаться SMS с адресом", "Взять документ, если он нужен для визита"]
  },
  isDemo: true
};

const languages = {
  "auto": "Авто",
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

if ("serviceWorker" in navigator) {
  window.addEventListener("load", () => {
    navigator.serviceWorker.register("/service-worker.js").catch(() => {});
  });
}

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

function isAutoLanguage(code) {
  return !code || code === "auto";
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

function formatShortDate(value) {
  if (!value) {
    return "—";
  }

  return new Intl.DateTimeFormat(undefined, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit", hour12: false }).format(new Date(value));
}

function callPrice() {
  return Number.isFinite(Number(state.config?.callPricePerMinute))
    ? Number(state.config.callPricePerMinute)
    : 0.5;
}

function formatCreditAmount(value) {
  const amount = Number(value || 0);
  return amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function formatBalance(value) {
  const amount = Number(value || 0);
  return Number.isFinite(amount)
    ? amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    : "0.00";
}

function formatTon(value) {
  const amount = Number(value || 0);
  return amount.toLocaleString(undefined, { maximumFractionDigits: 9 });
}

function formatUsdt(value) {
  const amount = Number(value || 0);
  return amount.toLocaleString(undefined, { maximumFractionDigits: 6 });
}

function tonTransferLink(walletAddress, tonAmount, comment) {
  const nanotons = Math.max(0, Math.round(Number(tonAmount || 0) * 1_000_000_000));
  return `ton://transfer/${encodeURIComponent(walletAddress || "")}?amount=${nanotons}&text=${encodeURIComponent(comment || "")}`;
}

function tonQrUrl(tonAmount) {
  const amount = Number(tonAmount || 0);
  return `/api/ton/qr?amount=${encodeURIComponent(Number.isFinite(amount) ? String(amount) : "0")}`;
}

function usdtQrUrl(usdtAmount) {
  const amount = Number(usdtAmount || 0);
  return `/api/usdt/qr?amount=${encodeURIComponent(Number.isFinite(amount) ? String(amount) : "0")}`;
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

function speakerName(entry) {
  const speaker = entry?.speaker;
  if (speaker === 0 || speaker === "0") return "Remote";
  if (speaker === 1 || speaker === "1") return "Assistant";
  if (speaker === 2 || speaker === "2") return "System";
  return speaker || "Remote";
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

  const speaker = speakerName(entry);
  if (speaker === "Remote" && call.language === call.userLanguage) {
    return false;
  }

  if (speaker === "Assistant" && call.language === call.userLanguage) {
    return false;
  }

  return true;
}

function isFullTwilioAccountSid(value) {
  return /^AC[0-9a-fA-F]{32}$/.test(value);
}

function statusName(status) {
  const statuses = ["Created", "Queued", "Calling", "Ringing", "InProgress", "Completed", "Failed", "Busy", "NoAnswer", "Canceled"];
  return statuses[Number(status)] || status;
}

function isLive(call) {
  return ["Created", "Queued", "Calling", "Ringing", "InProgress"].includes(statusName(call?.status));
}

function callStatusMeta(call) {
  const status = statusName(call?.status) || "Created";
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
  const status = statusName(call?.status);
  return status === "Failed" || (!!call?.error && status !== "Completed" && !isLive(call));
}

function isDemoCall(callOrId) {
  return callOrId === demoCall.id || callOrId?.id === demoCall.id || callOrId?.isDemo === true;
}

function isAdminUser() {
  return state.auth?.authenticated && state.auth.user?.username?.toLowerCase() === "admin";
}

function visibleCalls() {
  return state.auth?.authenticated && state.calls.length ? state.calls : [demoCall];
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

function hasCallChanged(left, right) {
  if (!left || !right) {
    return !!left !== !!right;
  }

  return left.updatedAt !== right.updatedAt ||
    left.status !== right.status ||
    (left.transcript?.length || 0) !== (right.transcript?.length || 0) ||
    (left.suggestions?.length || 0) !== (right.suggestions?.length || 0);
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
    if (!state.auth?.authenticated) {
      return;
    }

    upsertCall(call);
    if (state.activeCall?.id === call.id) {
      state.activeCall = call;
      if (isLive(call)) {
        state.startedAt = new Date(call.createdAt).getTime();
      }
      if (!isLive(call)) {
        loadBalance().catch(() => {});
      }
      render();
      return;
    }
    renderHistory();
  });

  state.hub.on("TranscriptAdded", entry => {
    if (!state.auth?.authenticated) {
      return;
    }

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
  const adminAuthenticated = isAdminUser();
  $("#setupWarning").hidden = ready || !adminAuthenticated;
  $("#newCallButton").disabled = !ready;
  $("#newCallButton").title = ready ? "" : (state.config.setupReason || "Сначала завершите настройки");
  $("#mobileNewCallButton").disabled = !ready;
  $("#mobileNewCallButton").title = ready ? "" : (state.config.setupReason || "Сначала завершите настройки");
  const settingsSidebarButton = $("#settingsSidebarButton");
  const mobileSettingsNav = $("#mobileSettingsNav");
  if (settingsSidebarButton) {
    settingsSidebarButton.hidden = !adminAuthenticated;
  }
  if (mobileSettingsNav) {
    mobileSettingsNav.hidden = !adminAuthenticated;
  }
  const setupText = $("#setupWarning p");
  if (setupText) setupText.textContent = state.config.setupReason || "Проверьте настройки перед звонком.";
}

function setMobileView(view) {
  state.mobileView = view === "history" || view === "admin" ? view : "call";
  document.body.classList.toggle("mobile-view-history", state.mobileView === "history");
  document.body.classList.toggle("mobile-view-admin", state.mobileView === "admin");
  document.body.classList.toggle("mobile-view-call", state.mobileView === "call");
  const adminPage = $("#adminPage");
  const mainSection = $(".main");
  if (adminPage) {
    adminPage.hidden = state.mobileView !== "admin";
  }
  if (mainSection) {
    mainSection.hidden = state.mobileView === "admin";
  }
  document.querySelectorAll("[data-mobile-view]").forEach(button => {
    button.classList.toggle("active", button.dataset.mobileView === state.mobileView);
  });
}

function normalizeAppPath(pathname = window.location.pathname) {
  const normalized = (pathname || "/").replace(/\/+$/, "").toLowerCase();
  return normalized || "/";
}

function isAdminPath(pathname) {
  return normalizeAppPath(pathname) === "/admin";
}

function setRouteState(view, { replace = false } = {}) {
  const target = view === "admin" ? "/admin" : "/";
  if (normalizeAppPath() === target) {
    return;
  }

  const statePayload = { view };
  const historyAction = replace ? "replaceState" : "pushState";
  if (typeof history[historyAction] === "function") {
    history[historyAction](statePayload, "", target);
  }
}

async function syncRouteFromLocation() {
  state.mobileView = isAdminPath() ? "admin" : "call";
  if (isAdminPath()) {
    await openSettingsProtected({ fromRoute: true });
    return;
  }

  if (state.mobileView === "admin") {
    setMobileView("call");
  }
}

function closeAdminPanel({ replaceHistory = true } = {}) {
  setMobileView("call");
  setRouteState("call", { replace: replaceHistory });
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
  const ton = config.tonPayments || {};
  $("#tonWalletAddressInput").value = ton.walletAddress || "";
  $("#tonCreditsPerTonInput").value = ton.creditsPerTon || 1000;
  $("#tonMinAmountInput").value = ton.minTonAmount || 0.1;
  const usdt = config.usdtPayments || {};
  $("#usdtWalletAddressInput").value = usdt.walletAddress || "";
  $("#usdtNetworkInput").value = usdt.network || "TRC20";
  $("#usdtCreditsPerUsdtInput").value = usdt.creditsPerUsdt || 100;
  $("#usdtMinAmountInput").value = usdt.minUsdtAmount || 1;
  renderTonAdminPayments();
}

function renderBalance() {
  $("#balanceAmount").textContent = formatBalance(state.balance.balance);
  renderTonTopup();
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
        <span>+${escapeHtml(formatBalance(code.amount))} кредитов · активации ${escapeHtml(limit)}</span>
      </div>
      <button type="button" class="secondary-button" data-toggle-promo-id="${code.id}" data-active="${code.active ? "false" : "true"}">
        <span class="material-symbols-rounded">${code.active ? "block" : "check_circle"}</span>
        ${status}
      </button>
    </article>`;
  }).join("") : `<div class="empty-history">Промокодов пока нет.</div>`;
}

function renderAdminUsers() {
  const list = $("#adminUsersList");
  if (!list) return;
  const users = Array.isArray(state.adminUsers) ? state.adminUsers : [];
  list.innerHTML = users.length ? users.map(user => {
    const duration = formatDuration(user.totalDurationSeconds || 0) || "0:00";
    return `<article class="admin-user-item">
      <div class="admin-user-main">
        <strong>${escapeHtml(user.username)}</strong>
        <span>Создан ${escapeHtml(formatShortDate(user.createdAt))}</span>
      </div>
      <div class="admin-user-stats">
        <span><strong>${escapeHtml(formatBalance(user.balance))}</strong><small>кредиты</small></span>
        <span><strong>${escapeHtml(String(user.totalCalls || 0))}</strong><small>звонки</small></span>
        <span><strong>${escapeHtml(String(user.completedCalls || 0))}</strong><small>завершено</small></span>
        <span><strong>${escapeHtml(String(user.missedCalls || 0))}</strong><small>не ответили</small></span>
        <span><strong>${escapeHtml(duration)}</strong><small>время</small></span>
      </div>
      <span class="admin-user-last">Последний звонок: ${escapeHtml(formatShortDate(user.lastCallAt))}</span>
    </article>`;
  }).join("") : `<div class="empty-history">Пользователей пока нет.</div>`;
}

function renderTonTopup() {
  const panel = $("#tonTopupPanel");
  if (!panel) return;
  const currency = $("#topupCurrencyInput")?.value === "USDT" ? "USDT" : "TON";
  const tonConfig = state.config.tonPayments || {};
  const usdtConfig = state.config.usdtPayments || {};
  const config = currency === "USDT" ? usdtConfig : tonConfig;
  const minAmount = Number(currency === "USDT"
    ? (config.minUsdtAmount || 1)
    : (config.minTonAmount || 0.1));
  const amountInput = $("#tonAmountInput");
  const amountLabel = $("#topupAmountLabel");
  if (amountLabel) amountLabel.textContent = `Сумма ${currency}`;
  if (amountInput && minAmount) {
    amountInput.min = String(minAmount);
    amountInput.step = currency === "USDT" ? "0.01" : "0.01";
    if (Number(amountInput.value || 0) < minAmount) {
      amountInput.value = String(minAmount);
    }
  }

  const box = $("#tonPaymentBox");
  if (box) box.hidden = !config.enabled;
  if (config.enabled) {
    const amount = Math.max(Number(amountInput?.value || minAmount), minAmount);
    if (currency === "USDT") {
      const credits = amount * Number(config.creditsPerUsdt || 0);
      $("#tonPaymentTitle").textContent = "USDT реквизиты";
      $("#tonPaymentText").textContent = `${formatUsdt(amount)} USDT (${config.network || "TRC20"}) -> ${formatBalance(credits)} кредитов (USD). Адрес: ${config.walletAddress || ""}. Комментарий: ${config.comment || ""}. После перевода отправьте tx id администратору для зачисления.`;
      $("#tonPaymentLink").href = "#";
      $("#tonPaymentLink").dataset.copyAddress = config.walletAddress || "";
      $("#tonPaymentLinkLabel").textContent = "Скопировать адрес";
      $("#tonPaymentQr").src = usdtQrUrl(amount);
      $("#tonPaymentQr").alt = "QR код для оплаты USDT";
      $("#markTonPaidButton").hidden = true;
    } else {
      const credits = amount * Number(config.creditsPerTon || 0);
      $("#tonPaymentTitle").textContent = "TON реквизиты";
      $("#tonPaymentText").textContent = `${formatTon(amount)} TON -> ${formatBalance(credits)} кредитов (USD). Комментарий: ${config.comment || ""}. После перевода можно закрыть приложение, сервер сам зачислит поступление.`;
      $("#tonPaymentLink").href = tonTransferLink(config.walletAddress, amount, config.comment);
      delete $("#tonPaymentLink").dataset.copyAddress;
      $("#tonPaymentLinkLabel").textContent = "Открыть кошелёк";
      $("#tonPaymentQr").src = tonQrUrl(amount);
      $("#tonPaymentQr").alt = "QR код для оплаты TON";
      $("#markTonPaidButton").hidden = false;
    }
    $("#markTonPaidButton").removeAttribute("data-ton-payment-id");
  }

  const list = $("#tonUserPayments");
  if (list) {
    const payments = Array.isArray(state.tonPayments) ? state.tonPayments.slice(0, 3) : [];
    list.innerHTML = payments.length ? payments.map(item => `<article class="ton-payment-item confirmed">
      <div>
        <strong>${escapeHtml(formatTon(item.tonAmount))} TON</strong>
        <span>${escapeHtml(formatBalance(item.creditsAmount))} кредитов · зачислено</span>
        <small>${escapeHtml(item.comment)}</small>
      </div>
    </article>`).join("") : "";
  }
}

function renderTonAdminPayments() {
  const list = $("#tonAdminList");
  if (!list) return;
  const payments = Array.isArray(state.adminTonPayments) ? state.adminTonPayments : [];
  list.innerHTML = payments.length ? payments.slice(0, 20).map(payment => `<article class="ton-admin-item confirmed">
    <div>
      <strong>${escapeHtml(formatTon(payment.tonAmount))} TON -> ${escapeHtml(formatBalance(payment.creditsAmount))}</strong>
      <span>Зачислено · ${escapeHtml(payment.comment)}</span>
      <small>${escapeHtml(payment.clientId)}${payment.senderAddress ? ` · ${escapeHtml(payment.senderAddress)}` : ""}</small>
    </div>
  </article>`).join("") : `<div class="empty-history">TON-пополнений пока нет.</div>`;
}

function renderHistory() {
  const calls = visibleCalls();

  $("#callHistory").innerHTML = `<div class="history-group-label">${state.calls.length ? "Сегодня" : "Пример"}</div>` + calls.slice(0, 8).map(call => {
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
      <button class="history-menu-button material-symbols-rounded" type="button" data-menu-call-id="${call.id}" aria-label="Меню звонка" aria-expanded="false" ${isDemoCall(call) ? "hidden" : ""}>more_vert</button>
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
  const normalizedStatus = statusName(call?.status);
  const live = isLive(call);
  const latest = call?.transcript?.at(-1);
  const remoteSpeaking = live && speakerName(latest) === "Remote";
  document.body.classList.toggle("has-active-call", !!call);
  document.body.classList.toggle("is-live-call", live);
  document.body.classList.toggle("remote-speaking", remoteSpeaking);
  document.body.classList.remove("call-status-dialing", "call-status-live", "call-status-complete", "call-status-failed", "call-status-idle");
  const statusClass = !call
    ? "call-status-idle"
    : isFailedCall(call) || ["Busy", "NoAnswer", "Canceled"].includes(normalizedStatus)
      ? "call-status-failed"
      : normalizedStatus === "Completed"
        ? "call-status-complete"
        : normalizedStatus === "InProgress"
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
  const headerDuration = callDurationSeconds(call);
  $("#callTimer").textContent = live
    ? formatDuration(headerDuration) || "00:00"
    : (headerDuration && headerDuration > 0 ? formatDuration(headerDuration) : "");
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
      : isAutoLanguage(call.language)
        ? `Автоопределение языка → ${languageName(call.userLanguage)}`
        : call.userLanguage === call.language
        ? `Без перевода: ${languageName(call.userLanguage)}`
        : `Перевод: ${languageName(call.userLanguage)} → ${languageName(call.language)}`)
    : "Ответы и перевод появятся только после старта звонка";
  $("#messageInput").placeholder = call
    ? (live ? `Напишите ответ: ${languageName(call.userLanguage)}` : "Звонок уже завершён")
    : "Сначала начните реальный звонок";

  renderReplyArea(call, live, status);
}

function renderReplyArea(call, live, status) {
  const translationStatus = document.querySelector(".translation-status");
  const suggestions = $("#suggestions");
  const messageForm = $("#messageForm");
  const actionState = $("#callActionState");
  const replyArea = document.querySelector(".reply-area");
  const normalizedStatus = statusName(call?.status);
  const showActionState = !!call && !live;

  if (replyArea) {
    replyArea.classList.toggle("compact-state", showActionState);
    replyArea.hidden = !!call && !live && !showActionState;
  }

  if (translationStatus) {
    translationStatus.hidden = !live;
  }

  if (suggestions) {
    suggestions.hidden = !live;
  }

  if (messageForm) {
    messageForm.hidden = !live;
  }

  if (!actionState) {
    return;
  }

  actionState.hidden = !showActionState;
  actionState.classList.remove("complete", "failed", "warning");

  if (!showActionState) {
    return;
  }

  const title = $("#callActionStateTitle");
  const text = $("#callActionStateText");
  const icon = $("#callActionStateIcon");

  actionState.classList.add(status.className === "complete" ? "complete" : status.className === "failed" ? "failed" : "warning");
  if (icon) icon.textContent = status.icon;
  if (title) title.textContent = normalizedStatus === "Completed" ? "Звонок завершён" : status.text;
  if (text) {
    text.textContent = normalizedStatus === "NoAnswer"
      ? "Собеседник не поднял трубку. Можно попробовать позже или начать новый звонок."
      : normalizedStatus === "Busy"
        ? "Линия была занята. Можно попробовать ещё раз позже."
        : normalizedStatus === "Canceled"
          ? "Звонок отменён до разговора."
          : normalizedStatus === "Failed"
            ? (call.error || "Звонок не удалось выполнить.")
            : "Разговор завершён. Можно посмотреть итог выше или начать новый звонок.";
  }
}

function messageMarkup(entry) {
  const speaker = speakerName(entry);
  if (speaker === "System") {
    return `<div class="system-message">${escapeHtml(entry.text)}</div>`;
  }
  const assistant = speaker === "Assistant";
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
  if (!call || isLive(call)) {
    return "";
  }

  const transcript = call.transcript || [];
  const normalizedStatus = statusName(call.status);
  if (!transcript.length && normalizedStatus !== "Completed") {
    const status = callStatusMeta(call);
    return `<section class="call-summary terminal">
      <div class="summary-icon material-symbols-rounded">${status.icon}</div>
      <div>
        <span class="eyebrow">Итог звонка</span>
        <strong>${escapeHtml(status.text)}</strong>
        <p>${escapeHtml(normalizedStatus === "NoAnswer" ? "Собеседник не поднял трубку." : call.error || "Разговора не было.")}</p>
      </div>
    </section>`;
  }

  if (!transcript.length) {
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
  const adminAuthenticated = isAdminUser();
  const terminalStatus = call && !isLive(call) ? callStatusMeta(call) : null;
  const emptyTitle = terminalStatus ? terminalStatus.text : (state.config.readyForRealCalls ? "Готов к реальному звонку" : "Реальный режим требует настройки");
  const emptyText = terminalStatus
    ? (statusName(call.status) === "NoAnswer" ? "Собеседник не поднял трубку." : call.error || "Разговора не было.")
    : state.config.readyForRealCalls
    ? "Здесь появится разговор после старта. Пока звонок не начат, никакой линии нет."
    : adminAuthenticated
      ? `${state.config.setupReason || "Проверьте настройки."} Откройте «Настройки» слева внизу.`
      : `${state.config.setupReason || "Сервис ещё не настроен."} Настройки доступны только пользователю admin.`;

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
  renderAdminUsers();
  renderTonAdminPayments();
  renderHistory();
  renderHeader();
  renderConversation();
  setMobileView(state.mobileView);
}

function clearCallState() {
  state.calls = [];
  state.activeCall = null;
  state.summaryRequests.clear();
  const connectedState = globalThis.signalR?.HubConnectionState?.Connected || "Connected";
  if (state.subscribedCallId && state.hub?.state === connectedState) {
    state.hub.invoke("UnsubscribeCall", state.subscribedCallId).catch(() => {});
  }
  state.subscribedCallId = null;
}

async function loadConfig() {
  state.config = await api("/api/config");
}

async function loadAuth() {
  state.auth = await api("/api/auth/me");
  if (!state.auth?.authenticated) {
    clearCallState();
  }
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

async function loadAdminUsers() {
  if (!isAdminUser()) {
    state.adminUsers = [];
    renderAdminUsers();
    return;
  }

  state.adminUsers = await api("/api/admin/users");
  renderAdminUsers();
}

async function loadTonDepositInfo() {
  if (!state.auth?.authenticated) {
    state.tonPayments = [];
    renderTonTopup();
    return;
  }

  try {
    const [tonInfo, usdtInfo] = await Promise.all([
      api("/api/ton/deposit-info").catch(() => null),
      api("/api/usdt/deposit-info").catch(() => null)
    ]);
    if (tonInfo) {
      state.config.tonPayments = { ...(state.config.tonPayments || {}), ...tonInfo, enabled: !!tonInfo.enabled };
    }
    if (usdtInfo) {
      state.config.usdtPayments = { ...(state.config.usdtPayments || {}), ...usdtInfo, enabled: !!usdtInfo.enabled };
    }
  } catch {
    state.config.tonPayments = { ...(state.config.tonPayments || {}), enabled: false };
    state.config.usdtPayments = { ...(state.config.usdtPayments || {}), enabled: false };
  }

  state.tonPayments = await api("/api/ton/deposits").catch(() => []);
  renderTonTopup();
}

async function refreshTonDeposits() {
  const result = await api("/api/ton/refresh", { method: "POST" });
  state.balance = result.balance || state.balance;
  state.tonPayments = result.deposits || [];
  renderBalance();
}

async function loadAdminTonPayments() {
  if (!isAdminUser()) {
    state.adminTonPayments = [];
    renderTonAdminPayments();
    return;
  }

  state.adminTonPayments = await api("/api/admin/ton-payments");
  renderTonAdminPayments();
}

async function saveTonSettings() {
  const payload = {
    walletAddress: $("#tonWalletAddressInput").value.trim(),
    creditsPerTon: Number($("#tonCreditsPerTonInput").value),
    minTonAmount: Number($("#tonMinAmountInput").value)
  };
  await api("/api/config/ton", {
    method: "POST",
    body: JSON.stringify(payload)
  });
  await loadConfig();
  render();
}

async function saveUsdtSettings() {
  const payload = {
    walletAddress: $("#usdtWalletAddressInput").value.trim(),
    network: $("#usdtNetworkInput").value.trim(),
    creditsPerUsdt: Number($("#usdtCreditsPerUsdtInput").value),
    minUsdtAmount: Number($("#usdtMinAmountInput").value)
  };
  await api("/api/config/usdt", {
    method: "POST",
    body: JSON.stringify(payload)
  });
  await loadConfig();
  render();
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

$("#openAuthButton")?.addEventListener("click", () => openAuthDialog("login"));

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
  await loadConfig().catch(() => {});
  await loadCalls(state.activeCall?.id);
  if (isAdminPath()) {
    await syncRouteFromLocation();
    return;
  }
  render();
}

async function logoutUser() {
  await api("/api/auth/logout", { method: "POST" });
  state.auth = { authenticated: false, user: null, balanceClientId: null };
  await loadConfig().catch(() => {});
  await loadBalance();
  await loadCalls();
  render();
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
      showToast(state.auth?.authenticated ? "Настройки доступны только пользователю admin." : "Войдите под пользователем admin.");
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
      showToast(state.auth?.authenticated ? "Настройки доступны только пользователю admin." : "Войдите под пользователем admin.");
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
      showToast(state.auth?.authenticated ? "Настройки доступны только пользователю admin." : "Войдите под пользователем admin.");
      return;
    }
    await new Promise(resolve => setTimeout(resolve, 1200));
    await loadConfig().catch(() => {});
    render();
    showToast(error.message || "Twilio не принял SID/Auth Token");
  }
}

async function loadCalls(selectId = null) {
  if (!state.auth?.authenticated) {
    clearCallState();
    state.activeCall = demoCall;
    render();
    return;
  }

  try {
    state.calls = await api("/api/calls");
    if (selectId) {
      state.activeCall = state.calls.find(call => call.id === selectId) || state.activeCall;
    } else if (state.activeCall?.id) {
      state.activeCall = state.calls.find(call => call.id === state.activeCall.id) || state.activeCall;
    } else {
      state.activeCall = state.calls.find(isLive) || state.calls[0] || demoCall;
    }
    if (state.activeCall?.id && !isDemoCall(state.activeCall)) {
      subscribeActiveCall(state.activeCall.id);
    }
    render();
  } catch (error) {
    if (error.status === 401) {
      state.auth = { authenticated: false, user: null, balanceClientId: null };
      clearCallState();
      state.activeCall = demoCall;
      render();
      return;
    }

    showToast("Не удалось обновить звонки");
  }
}

async function refreshActiveCall({ forceRender = false } = {}) {
  if (!state.activeCall?.id || isDemoCall(state.activeCall)) {
    return null;
  }

  try {
    const call = await api(`/api/calls/${state.activeCall.id}`);
    const changed = forceRender || hasCallChanged(state.activeCall, call);
    state.activeCall = call;
    upsertCall(call);
    if (isLive(call)) {
      state.startedAt = new Date(call.createdAt).getTime();
    }
    if (changed) {
      renderHeader();
      renderConversation();
      renderHistory();
      renderActiveHistoryDuration();
    }
    if (!isLive(call) && changed) {
      loadBalance().catch(() => {});
    }
    return call;
  } catch {
    return null;
  }
}

async function sendMessage(text, spokenText = null) {
  if (!state.activeCall?.id || isDemoCall(state.activeCall)) {
    showToast("Это пример. Пополните кредиты и начните реальный звонок.");
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
  if (!call?.id || isDemoCall(call) || isLive(call) || call.summary || !(call.transcript || []).length || state.summaryRequests.has(call.id)) {
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
  if (isDemoCall(id)) {
    state.activeCall = demoCall;
    state.startedAt = new Date(demoCall.createdAt).getTime();
    setMobileView("call");
    render();
    return;
  }

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

async function openSettingsProtected({ fromRoute = false } = {}) {
  if (fromRoute) {
    setMobileView("admin");
  }
  try {
    await loadConfig();
    if (isAdminUser()) {
      render();
      await Promise.all([
        loadPromoCodes().catch(() => {}),
        loadAdminUsers().catch(() => {}),
        loadAdminTonPayments().catch(() => {})
      ]);
      setMobileView("admin");
      if (!fromRoute) {
        setRouteState("admin");
      }
      return;
    }

    render();
    if (!state.auth?.authenticated) {
      openAuthDialog("login");
      showToast("Войдите под пользователем admin.");
      return;
    }

    showToast("Настройки доступны только пользователю admin.");
  } catch {
    showToast("Не удалось открыть настройки");
  }
}

function openNewCallDialog() {
  if (!state.auth?.authenticated) {
    openAuthDialog("login");
    return;
  }

  const price = callPrice();
  if (Number(state.balance?.balance || 0) < price) {
    showToast(`Одна минута стоит ${formatBalance(price)} USD (кредиты). Зайдите в профиль и пополните кредиты.`);
    return;
  }

  if (!state.config.readyForRealCalls) {
    showToast(isAdminUser()
      ? (state.config.setupReason || "Завершите настройки перед реальным звонком")
      : "Сервис ещё не настроен. Настройки доступны только пользователю admin.");
    return;
  }
  clearNewCallErrors();
  $("#newCallDialog").showModal();
}

$("#newCallButton").addEventListener("click", openNewCallDialog);
$("#mobileNewCallButton").addEventListener("click", openNewCallDialog);
document.querySelectorAll("[data-mobile-view]").forEach(button => {
  button.addEventListener("click", () => {
    if (button.dataset.mobileView === "admin") {
      openSettingsProtected();
      return;
    }
    setMobileView(button.dataset.mobileView);
  });
});
$("#closeNewCallDialog").addEventListener("click", () => {
  clearNewCallErrors();
  $("#newCallDialog").close();
});
$("#cancelNewCallDialog").addEventListener("click", () => {
  clearNewCallErrors();
  $("#newCallDialog").close();
});
$("#openSettingsButton").addEventListener("click", openSettingsProtected);
$("#settingsSidebarButton")?.addEventListener("click", openSettingsProtected);
$("#closeSettingsPage")?.addEventListener("click", () => closeAdminPanel());
$("#adminDoneButton")?.addEventListener("click", () => closeAdminPanel());
$("#refreshAdminUsersButton")?.addEventListener("click", async () => {
  try {
    await loadAdminUsers();
    showToast("Пользователи обновлены");
  } catch (error) {
    showToast(error.message || "Не удалось обновить пользователей");
  }
});
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
$("#saveTonSettingsButton")?.addEventListener("click", async () => {
  try {
    await saveTonSettings();
    await loadAdminTonPayments().catch(() => {});
    showToast("TON настройки сохранены");
  } catch (error) {
    showToast(error.message || "Не удалось сохранить TON");
  }
});
$("#saveUsdtSettingsButton")?.addEventListener("click", async () => {
  try {
    await saveUsdtSettings();
    showToast("USDT настройки сохранены");
  } catch (error) {
    showToast(error.message || "Не удалось сохранить USDT");
  }
});
$("#checkTwilioButton").addEventListener("click", checkTwilioSettings);
$("#checkTwilioTopButton").addEventListener("click", checkTwilioSettings);
$("#helpButton").addEventListener("click", () => $("#helpDialog").showModal());
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
window.addEventListener("popstate", () => {
  syncRouteFromLocation().catch(() => {});
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
$("#openTonTopupButton")?.addEventListener("click", async () => {
  if (!state.auth?.authenticated) {
    openAuthDialog("login");
    return;
  }
  const panel = $("#tonTopupPanel");
  panel.hidden = !panel.hidden;
  if (!panel.hidden) {
    await loadTonDepositInfo().catch(() => showToast("Пополнение пока не настроено"));
    if (!state.config.tonPayments?.enabled && !state.config.usdtPayments?.enabled) {
      showToast("Пополнение пока не настроено");
    }
  }
});
$("#tonAmountInput")?.addEventListener("input", renderTonTopup);
$("#topupCurrencyInput")?.addEventListener("change", renderTonTopup);
$("#tonTopupForm")?.addEventListener("submit", event => {
  event.preventDefault();
  renderTonTopup();
  $("#tonPaymentBox").hidden = false;
});
$("#tonPaymentLink")?.addEventListener("click", async event => {
  const address = event.currentTarget.dataset.copyAddress;
  if (!address) return;
  event.preventDefault();
  await navigator.clipboard?.writeText(address);
  showToast("USDT адрес скопирован");
});
$("#markTonPaidButton")?.addEventListener("click", async () => {
  try {
    await refreshTonDeposits();
    showToast("Проверил TON. Если транзакция уже пришла, кредиты обновлены.");
  } catch (error) {
    showToast(error.message || "Не удалось проверить TON");
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
  if (!state.activeCall?.id || isDemoCall(state.activeCall)) {
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
  if (!state.activeCall?.id || isDemoCall(state.activeCall)) {
    showToast("Нет активного звонка");
    return;
  }
  try {
    state.activeCall = await api(`/api/calls/${state.activeCall.id}/end`, { method: "POST" });
    upsertCall(state.activeCall);
    render();
    showToast("Звонок завершён");
  } catch (error) {
    const call = await refreshActiveCall({ forceRender: true });
    if (call && !isLive(call)) {
      showToast("Звонок уже завершён");
      return;
    }
    showToast(error.message || "Не удалось завершить звонок");
  }
});
$("#newCallForm").addEventListener("submit", async event => {
  event.preventDefault();
  clearNewCallErrors();
  const price = callPrice();
  if (Number(state.balance?.balance || 0) < price) {
    showToast(`Одна минута стоит ${formatBalance(price)} USD (кредиты). Пополните кредиты.`);
    return;
  }

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
    language: String(data.get("language") || "auto"),
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
    const duration = callDurationSeconds(state.activeCall);
    $("#callTimer").textContent = duration && duration > 0 ? formatDuration(duration) : "";
    return;
  }
  const elapsed = Math.max(0, Date.now() - state.startedAt);
  const minutes = Math.floor(elapsed / 60000).toString().padStart(2, "0");
  const seconds = Math.floor((elapsed % 60000) / 1000).toString().padStart(2, "0");
  $("#callTimer").textContent = `${minutes}:${seconds}`;
  renderActiveHistoryDuration();
}, 1000);

setInterval(() => {
  if (!state.activeCall?.id || !isLive(state.activeCall)) {
    return;
  }

  refreshActiveCall();
}, 2500);

(async function boot() {
  try {
    await loadConfig();
    await loadAuth().catch(() => {});
    await loadBalance().catch(() => {});
    await loadTonDepositInfo().catch(() => {});
    await loadCalls();
    await startLiveUpdates();
    await syncRouteFromLocation();
  } catch {
    showToast("Не удалось загрузить конфигурацию");
    render();
  }
})();
