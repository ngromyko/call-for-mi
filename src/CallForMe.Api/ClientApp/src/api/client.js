export class ApiError extends Error {
  constructor(message, problem = null, status = 0) {
    super(message);
    this.name = "ApiError";
    this.problem = problem;
    this.status = status;
    this.errors = problem?.errors || {};
  }
}

export async function api(url, options = {}) {
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

    throw new ApiError(detail || `HTTP ${response.status}`, problem, response.status);
  }

  return response.status === 204 ? null : response.json();
}

export const apiClient = {
  config: () => api("/api/config"),
  currentUser: () => api("/api/auth/me"),
  login: payload => api("/api/auth/login", { method: "POST", body: JSON.stringify(payload) }),
  register: payload => api("/api/auth/register", { method: "POST", body: JSON.stringify(payload) }),
  logout: () => api("/api/auth/logout", { method: "POST" }),

  balance: clientId => api(`/api/balance/${encodeURIComponent(clientId)}`),
  redeemPromoCode: payload => api("/api/promocodes/redeem", { method: "POST", body: JSON.stringify(payload) }),

  calls: () => api("/api/calls"),
  call: id => api(`/api/calls/${id}`),
  createCall: payload => api("/api/calls", { method: "POST", body: JSON.stringify(payload) }),
  sendMessage: (id, payload) => api(`/api/calls/${id}/messages`, { method: "POST", body: JSON.stringify(payload) }),
  setAutoPilot: (id, enabled) => api(`/api/calls/${id}/autopilot`, { method: "POST", body: JSON.stringify({ enabled }) }),
  endCall: id => api(`/api/calls/${id}/end`, { method: "POST" }),
  summarizeCall: id => api(`/api/calls/${id}/summary`, { method: "POST" }),
  hideCall: id => api(`/api/calls/${id}/hide`, { method: "POST" }),

  tonDepositInfo: () => api("/api/ton/deposit-info"),
  usdtDepositInfo: () => api("/api/usdt/deposit-info"),
  tonDeposits: () => api("/api/ton/deposits"),
  refreshTonDeposits: () => api("/api/ton/refresh", { method: "POST" }),

  adminUsers: () => api("/api/admin/users"),
  adminTonPayments: () => api("/api/admin/ton-payments"),
  promoCodes: () => api("/api/admin/promocodes"),
  createPromoCode: payload => api("/api/admin/promocodes", { method: "POST", body: JSON.stringify(payload) }),
  togglePromoCode: (id, active) => api(`/api/admin/promocodes/${id}`, { method: "PATCH", body: JSON.stringify({ active }) }),

  saveOpenAi: payload => api("/api/config/openai", { method: "POST", body: JSON.stringify(payload) }),
  saveTwilio: payload => api("/api/config/twilio", { method: "POST", body: JSON.stringify(payload) }),
  checkTwilio: () => api("/api/config/twilio/check", { method: "POST" }),
  saveTon: payload => api("/api/config/ton", { method: "POST", body: JSON.stringify(payload) }),
  saveUsdt: payload => api("/api/config/usdt", { method: "POST", body: JSON.stringify(payload) })
};
