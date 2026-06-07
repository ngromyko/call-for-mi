export function formatPhone(phone, t) {
  return phone || t("call.chooseNumber");
}

export function formatTime(value) {
  return new Intl.DateTimeFormat(undefined, { hour: "2-digit", minute: "2-digit", hour12: false }).format(new Date(value));
}

export function formatDuration(seconds) {
  if (!Number.isFinite(seconds) || seconds < 0) return "";

  const total = Math.max(0, Math.round(seconds));
  const hours = Math.floor(total / 3600);
  const minutes = Math.floor((total % 3600) / 60);
  const rest = total % 60;
  if (hours > 0) {
    return `${hours}:${minutes.toString().padStart(2, "0")}:${rest.toString().padStart(2, "0")}`;
  }

  return `${minutes}:${rest.toString().padStart(2, "0")}`;
}

export function formatShortDate(value) {
  if (!value) return "-";
  return new Intl.DateTimeFormat(undefined, {
    day: "2-digit",
    month: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false
  }).format(new Date(value));
}

export function formatBalance(value) {
  const amount = Number(value || 0);
  return Number.isFinite(amount)
    ? amount.toLocaleString(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 })
    : "0.00";
}

export function formatTon(value) {
  const amount = Number(value || 0);
  return amount.toLocaleString(undefined, { maximumFractionDigits: 9 });
}

export function formatUsdt(value) {
  const amount = Number(value || 0);
  return amount.toLocaleString(undefined, { maximumFractionDigits: 6 });
}

export function getClientId(auth) {
  if (auth?.authenticated && auth.balanceClientId) return auth.balanceClientId;

  const key = "callforme_client_id";
  let clientId = localStorage.getItem(key);
  if (!clientId) {
    clientId = crypto.randomUUID ? crypto.randomUUID() : `client-${Date.now()}-${Math.random().toString(16).slice(2)}`;
    localStorage.setItem(key, clientId);
  }
  return clientId;
}

export const USDT_TON_JETTON_MASTER = "EQCxE6mUtQJKFnGfaROTKOt1lZbDiiX1kCixRv7Nw2Id_sDs";

export function tonTransferLink(walletAddress, tonAmount, comment) {
  const nanotons = Math.max(0, Math.round(Number(tonAmount || 0) * 1_000_000_000));
  return `https://app.tonkeeper.com/transfer/${encodeURIComponent(walletAddress || "")}?amount=${nanotons}&text=${encodeURIComponent(comment || "")}`;
}

export function isTonUsdtNetwork(network) {
  const normalized = String(network || "TON").trim().toUpperCase();
  return !normalized || normalized === "TON" || normalized === "TON-USDT" || normalized === "USDT-TON";
}

export function usdtTransferLink(walletAddress, usdtAmount, comment, network = "TON", jettonMasterAddress = USDT_TON_JETTON_MASTER) {
  if (!isTonUsdtNetwork(network)) return "";

  const micros = Math.max(0, Math.round(Number(usdtAmount || 0) * 1_000_000));
  return `https://app.tonkeeper.com/transfer/${encodeURIComponent(walletAddress || "")}?jetton=${encodeURIComponent(jettonMasterAddress || USDT_TON_JETTON_MASTER)}&amount=${micros}&text=${encodeURIComponent(comment || "")}`;
}

export function isFullTwilioAccountSid(value) {
  return /^AC[0-9a-fA-F]{32}$/.test(value);
}
