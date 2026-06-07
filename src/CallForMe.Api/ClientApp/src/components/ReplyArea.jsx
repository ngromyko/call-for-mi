import { useState } from "react";
import { Icon } from "./Dialog.jsx";
import { isLive, translationStatus } from "../utils/callState.js";
import { callLanguageName } from "../data/languages.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function ReplyArea({ call, onSendMessage, onToggleAutoPilot, onEndCall, draft, onDraftChange }) {
  const { t } = useI18n();
  const [sending, setSending] = useState(false);
  const live = isLive(call);
  const latest = call?.transcript?.at(-1);
  const suggestions = Array.isArray(call?.suggestions) ? call.suggestions : [];
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

  if (!live) return null;

  return (
    <div className="reply-area">
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
    </div>
  );
}
