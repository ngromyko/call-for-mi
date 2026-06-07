import { callLanguageName } from "../data/languages.js";

export function statusName(status) {
  const statuses = ["Created", "Queued", "Calling", "Ringing", "InProgress", "Completed", "Failed", "Busy", "NoAnswer", "Canceled"];
  return statuses[Number(status)] || status;
}

export function isLive(call) {
  return ["Created", "Queued", "Calling", "Ringing", "InProgress"].includes(statusName(call?.status));
}

export function isDemoCall(callOrId) {
  return callOrId === "demo-call" || callOrId?.id === "demo-call" || callOrId?.isDemo === true;
}

export function speakerName(entry) {
  const speaker = entry?.speaker;
  if (speaker === 0 || speaker === "0") return "Remote";
  if (speaker === 1 || speaker === "1") return "Assistant";
  if (speaker === 2 || speaker === "2") return "System";
  return speaker || "Remote";
}

export function isFailedCall(call) {
  const status = statusName(call?.status);
  return status === "Failed" || (!!call?.error && status !== "Completed" && !isLive(call));
}

export function callStatusMeta(call, t) {
  const status = statusName(call?.status) || "Created";
  if (status === "Completed") return { className: "complete", icon: "check_circle", text: t("status.completed") };
  if (status === "Failed" || (!!call?.error && !isLive(call))) return { className: "failed", icon: "error", text: t("status.failed") };
  if (status === "Busy") return { className: "warning", icon: "phone_missed", text: t("status.busy") };
  if (status === "NoAnswer") return { className: "warning", icon: "phone_missed", text: t("status.noAnswer") };
  if (status === "Canceled") return { className: "warning", icon: "cancel", text: t("status.canceled") };
  if (status === "Ringing") return { className: "ringing", icon: "phone_in_talk", text: t("status.ringing") };
  if (status === "Queued" || status === "Calling") {
    return { className: "live", icon: "radio_button_checked", text: t("status.dialing") };
  }
  if (isLive(call)) return { className: "live", icon: "radio_button_checked", text: t("status.live") };
  return { className: "warning", icon: "info", text: status };
}

export function contactName(call, t) {
  if (!call) return t("call.noActive");
  if (call.displayName) return call.displayName;

  const prompt = (call.prompt || "").toLowerCase();
  if (prompt.includes("врач") || prompt.includes("клиник")) return t("contacts.clinic");
  if (prompt.includes("банк")) return t("contacts.bank");
  if (prompt.includes("достав")) return t("contacts.delivery");
  if (prompt.includes("документ") || prompt.includes("карта")) return t("contacts.office");
  if (prompt.includes("статус") || prompt.includes("обращен")) return t("contacts.support");
  return t("contacts.call");
}

export function callDurationSeconds(call) {
  if (!call) return null;
  if (Number.isFinite(call.durationSeconds)) return call.durationSeconds;
  if (isLive(call)) return (Date.now() - new Date(call.createdAt).getTime()) / 1000;
  if (call.createdAt && call.updatedAt) {
    const fallback = (new Date(call.updatedAt).getTime() - new Date(call.createdAt).getTime()) / 1000;
    return fallback > 0 ? fallback : null;
  }
  return null;
}

export function shouldShowTranslation(entry, call) {
  if (!entry?.translation) return false;
  if (normalize(entry.text) === normalize(entry.translation)) return false;
  if (!call) return true;
  if (call.language === call.userLanguage) return false;
  return true;
}

export function translationStatus(call, latestEntry, t) {
  if (!call) return t("conversation.translationIdle");
  const remoteSpeaking = isLive(call) && speakerName(latestEntry) === "Remote";
  if (remoteSpeaking) return t("conversation.remoteSpeaking");
  if (!call.language || call.language === "auto") {
    return t("conversation.autoTranslate", { userLanguage: callLanguageName(call.userLanguage, t("languages.autoShort")) });
  }
  if (call.userLanguage === call.language) {
    return t("conversation.noTranslation", { language: callLanguageName(call.userLanguage, t("languages.autoShort")) });
  }
  return t("conversation.translationPair", {
    userLanguage: callLanguageName(call.userLanguage, t("languages.autoShort")),
    remoteLanguage: callLanguageName(call.language, t("languages.autoShort"))
  });
}

function normalize(text = "") {
  return text.trim().replace(/\s+/g, " ").toLocaleLowerCase();
}
