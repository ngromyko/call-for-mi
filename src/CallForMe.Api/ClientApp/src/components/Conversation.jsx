import { useEffect, useRef } from "react";
import { CallSummary } from "./CallSummary.jsx";
import { callStatusMeta, isLive, shouldShowTranslation, speakerName, statusName } from "../utils/callState.js";
import { formatTime } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function Conversation({ call, config, isAdmin, onEnsureSummary }) {
  const { t } = useI18n();
  const ref = useRef(null);
  const transcript = call?.transcript || [];
  const terminalStatus = call && !isLive(call) ? callStatusMeta(call, t) : null;

  useEffect(() => {
    if (ref.current) {
      ref.current.scrollTop = ref.current.scrollHeight;
    }
  }, [transcript.length, call?.id]);

  useEffect(() => {
    if (call && !isLive(call) && transcript.length && !call.summary) {
      onEnsureSummary(call);
    }
  }, [call, onEnsureSummary, transcript.length]);

  const emptyTitle = terminalStatus ? terminalStatus.text : (config.readyForRealCalls ? t("conversation.emptyReadyTitle") : t("conversation.emptySetupTitle"));
  const emptyText = terminalStatus
    ? (statusName(call.status) === "NoAnswer" ? t("conversation.emptyNoAnswer") : call.error || t("conversation.emptyNoConversation"))
    : config.readyForRealCalls
      ? t("conversation.emptyReadyText")
      : isAdmin
        ? `${config.setupReason || t("setup.checkBeforeCall")} ${t("sidebar.openSettings")}.`
        : `${config.setupReason || t("setup.adminOnly")}`;

  return (
    <>
      <div className="pinned-summary" hidden={!call || isLive(call)}>
        <CallSummary call={call} />
      </div>
      <div className="conversation" ref={ref} aria-live="polite">
        {transcript.length ? transcript.map(entry => (
          <Message key={entry.id || `${entry.timestamp}-${entry.text}`} entry={entry} call={call} />
        )) : (
          <div className="empty-state">
            <div>
              <span className="material-symbols-rounded">phone_in_talk</span>
              <strong>{emptyTitle}</strong>
              <span>{emptyText}</span>
            </div>
          </div>
        )}
      </div>
    </>
  );
}

function Message({ entry, call }) {
  const { t } = useI18n();
  const speaker = speakerName(entry);
  if (speaker === "System") {
    return <div className="system-message">{entry.text}</div>;
  }

  const assistant = speaker === "Assistant";
  return (
    <div className={`message-row ${assistant ? "assistant" : "remote"}`}>
      {!assistant ? <span className="speaker-avatar material-symbols-rounded">person</span> : null}
      <div className="message-wrap">
        <div className="message-meta">
          <span>{assistant ? t("conversation.speakerAssistant") : t("conversation.speakerRemote")}</span>
          <time>{formatTime(entry.timestamp)}</time>
        </div>
        <div className="message-bubble">
          <div className="message-original">{entry.text}</div>
          {shouldShowTranslation(entry, call) ? <div className="message-translation">{entry.translation}</div> : null}
        </div>
      </div>
    </div>
  );
}
