import { Icon } from "./Dialog.jsx";
import { callStatusMeta, isLive, statusName } from "../utils/callState.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function CallSummary({ call }) {
  const { t } = useI18n();
  if (!call || isLive(call)) return null;

  const transcript = call.transcript || [];
  const normalizedStatus = statusName(call.status);

  if (!transcript.length && normalizedStatus !== "Completed") {
    const status = callStatusMeta(call, t);
    return (
      <section className="call-summary terminal">
        <div className="summary-icon material-symbols-rounded">{status.icon}</div>
        <div>
          <span className="eyebrow">{t("conversation.finalSummaryTitle")}</span>
          <strong>{status.text}</strong>
          <p>{normalizedStatus === "NoAnswer" ? t("conversation.emptyNoAnswer") : call.error || t("conversation.emptyNoConversation")}</p>
        </div>
      </section>
    );
  }

  if (!transcript.length) return null;

  const summary = call.summary;
  if (!summary) {
    return (
      <section className="call-summary loading">
        <div className="summary-icon material-symbols-rounded">auto_awesome</div>
        <div>
          <span className="eyebrow">{t("conversation.summaryTitle")}</span>
          <strong>{t("conversation.summaryLoadingTitle")}</strong>
          <p>{t("conversation.summaryLoadingText")}</p>
        </div>
      </section>
    );
  }

  return (
    <section className="call-summary">
      <Icon className="summary-icon">summarize</Icon>
      <div>
        <span className="eyebrow">{t("conversation.summaryTitle")}</span>
        <strong>{summary.outcome || t("conversation.summaryFallbackOutcome")}</strong>
        <p>{summary.keyPoint || t("conversation.summaryFallbackPoint")}</p>
        <div className="summary-stats">
          {summary.tone ? <span>{summary.tone}</span> : null}
          {call.durationSeconds ? <span>{Math.max(1, Math.round(call.durationSeconds / 60))} min.</span> : null}
        </div>
        {summary.nextStep ? <small>{summary.nextStep}</small> : null}
      </div>
    </section>
  );
}
