import { useState } from "react";
import { Icon } from "./Dialog.jsx";
import { callStatusMeta, isLive, statusName, translationStatus } from "../utils/callState.js";
import { callLanguageName } from "../data/languages.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function ReplyArea({ call, onSendMessage, onToggleAutoPilot, onEndCall, draft, onDraftChange }) {
  const { t } = useI18n();
  const [sending, setSending] = useState(false);
  const live = isLive(call);
  const status = callStatusMeta(call, t);
  const normalizedStatus = statusName(call?.status);
  const latest = call?.transcript?.at(-1);
  const suggestions = Array.isArray(call?.suggestions) ? call.suggestions : [];
  const showActionState = !!call && !live;
  const placeholder = call
    ? (live
      ? t("conversation.messagePlaceholderLive", { language: callLanguageName(call.userLanguage, t("languages.autoShort")) })
      : t("conversation.messagePlaceholderDone"))
    : t("conversation.messagePlaceholderIdle");

  async function submit(text, spokenText = null) {
    const value = text.trim();
    if (!value || sending) return;
    setSending(true);
    try {
      await onSendMessage(value, spokenText);
      onDraftChange("");
    } finally {
      setSending(false);
    }
  }

  return (
    <div className={`reply-area ${showActionState ? "compact-state" : ""}`}>
      <div className="translation-status" hidden={!live}>
        <Icon>graphic_eq</Icon>
        <span>{translationStatus(call, latest, t)}</span>
      </div>

      <div className="suggestions" hidden={!live}>
        {suggestions.map((item, index) => {
          const suggestion = typeof item === "string" ? { text: item, spokenText: item } : item;
          const icon = ["calendar_month", "schedule", "event_available"][index] || "chat";
          return (
            <button
              className="suggestion-button"
              key={`${suggestion.text}-${index}`}
              type="button"
              onClick={() => submit(suggestion.text, suggestion.spokenText)}
            >
              <Icon>{icon}</Icon>
              <span>{suggestion.text}</span>
            </button>
          );
        })}
      </div>

      <form
        className="composer"
        hidden={!live}
        onSubmit={event => {
          event.preventDefault();
          submit(draft);
        }}
      >
        <div className="composer-field">
          <textarea
            rows="2"
            placeholder={placeholder}
            aria-label={t("conversation.messageAria")}
            value={draft}
            disabled={!call || !live || sending}
            onChange={event => onDraftChange(event.target.value)}
          />
          <button type="submit" className="send-button" aria-label={t("conversation.send")} disabled={!draft.trim() || sending}>
            <Icon>send</Icon>
          </button>
        </div>
        <label className="autopilot-control">
          <input
            type="checkbox"
            checked={!!call?.autoPilot}
            disabled={!call || !live}
            onChange={event => onToggleAutoPilot(event.target.checked)}
          />
          <span className="toggle-visual"><span /></span>
          <span>
            <strong>{t("conversation.autopilotTitle")}</strong>
            <small>{t("conversation.autopilotHelp")}</small>
          </span>
        </label>
        <button type="button" className="end-call-button" disabled={!call || !live} onClick={onEndCall}>
          <Icon>call_end</Icon>
          {t("conversation.end")}
        </button>
      </form>

      <div className={`call-action-state ${status.className === "complete" ? "complete" : status.className === "failed" ? "failed" : "warning"}`} hidden={!showActionState}>
        <Icon>{status.icon}</Icon>
        <div>
          <strong>{normalizedStatus === "Completed" ? t("call.ended") : status.text}</strong>
          <span>{actionText(normalizedStatus, call, t)}</span>
        </div>
      </div>
    </div>
  );
}

function actionText(status, call, t) {
  if (status === "NoAnswer") return t("conversation.actionNoAnswer");
  if (status === "Busy") return t("conversation.actionBusy");
  if (status === "Canceled") return t("conversation.actionCanceled");
  if (status === "Failed") return call?.error || t("conversation.actionFailed");
  return t("conversation.actionCompleted");
}
