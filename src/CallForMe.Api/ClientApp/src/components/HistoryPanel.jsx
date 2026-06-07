import { useState } from "react";
import { Icon } from "./Dialog.jsx";
import { callDurationSeconds, callStatusMeta, contactName, isDemoCall, isLive } from "../utils/callState.js";
import { formatDuration, formatPhone, formatTime } from "../utils/format.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function HistoryPanel({ calls, activeCall, realCallsCount, onRefresh, onSelect, onHide }) {
  const { t } = useI18n();
  const [openMenuId, setOpenMenuId] = useState(null);

  return (
    <>
      <div className="history-heading">
        <span>{t("sidebar.history")}</span>
        <button className="icon-button" type="button" onClick={onRefresh} aria-label={t("sidebar.refreshHistory")}>
          <Icon>refresh</Icon>
        </button>
      </div>
      <nav className="call-history" aria-label={t("sidebar.history")}>
        <div className="history-group-label">{realCallsCount ? t("sidebar.historyToday") : t("sidebar.historyDemo")}</div>
        {calls.slice(0, 8).map(call => {
          const live = isLive(call);
          const active = call.id === activeCall?.id;
          const status = callStatusMeta(call, t);
          const duration = formatDuration(callDurationSeconds(call));

          return (
            <article key={call.id} className={`history-item ${active ? "active" : ""}`} data-call-id={call.id}>
              <button className="history-main" type="button" onClick={() => onSelect(call.id)}>
                <span className="history-icon material-symbols-rounded">{live ? "phone_in_talk" : "call"}</span>
                <span className="history-details">
                  <strong>{contactName(call, t)}</strong>
                  <span>{formatPhone(call.phoneNumber, t)}</span>
                  <span>{call.prompt}</span>
                  <span className={`history-status ${status.className}`}>
                    <Icon>{status.icon}</Icon>
                    {status.text}
                  </span>
                </span>
                <span className="history-meta">
                  <span className="history-time">{formatTime(call.createdAt)}</span>
                  {duration ? <span className="history-duration">{duration}</span> : null}
                </span>
              </button>

              {!isDemoCall(call) ? (
                <>
                  <button
                    className="history-menu-button material-symbols-rounded"
                    type="button"
                    aria-label={t("call.menu")}
                    aria-expanded={openMenuId === call.id}
                    onClick={event => {
                      event.stopPropagation();
                      setOpenMenuId(openMenuId === call.id ? null : call.id);
                    }}
                  >
                    more_vert
                  </button>
                  <div className="history-menu" hidden={openMenuId !== call.id}>
                    <button
                      type="button"
                      onClick={() => {
                        setOpenMenuId(null);
                        onHide(call.id);
                      }}
                    >
                      <Icon>visibility_off</Icon>
                      {t("call.hide")}
                    </button>
                  </div>
                </>
              ) : null}
            </article>
          );
        })}
      </nav>
    </>
  );
}
