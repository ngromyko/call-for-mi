import { isFullTwilioAccountSid } from "./format.js";

export function validateTwilioPayload(payload, config, t) {
  if (payload.accountSid.includes("\u2026") || payload.accountSid.includes("...")) {
    return t("admin.maskedSid");
  }
  if (payload.accountSid && !isFullTwilioAccountSid(payload.accountSid)) {
    return t("admin.fullSidIfChanging");
  }
  if (!payload.accountSid && !config.accountSid) {
    return t("admin.fullSidRequired");
  }
  if (!payload.authToken && !config.hasAuthToken) {
    return t("admin.authTokenRequired");
  }
  if (!payload.fromNumber || !payload.publicBaseUrl) {
    return t("admin.twilioNumberUrlRequired");
  }
  return "";
}
