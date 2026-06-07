import { Icon } from "./Dialog.jsx";
import { callDurationSeconds, callStatusMeta, contactName, isFailedCall, isLive, statusName } from "../utils/callState.js";
import { formatDuration, formatPhone } from "../utils/format.js";
import { callLanguageName } from "../data/languages.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function CallHeader({ call, config, elapsedSeconds }) {
  const { t } = useI18n();
  const live = isLive(call);
  const status = callStatusMeta(call, t);
  const duration = live ? elapsedSeconds : callDurationSeconds(call);
  const timer = live ? formatDuration(duration) || "00:00" : (duration && duration > 0 ? formatDuration(duration) : "");
  const goal = isFailedCall(call)
    ? call.error
    : call?.prompt || (config.readyForRealCalls ? t("call.selectNumberGoal") : (config.setupReason || t("setup.finishBeforeCall")));

  return (
    <header className="call-header">
      <div className="contact-avatar">
        <span className="avatar-orbit orbit-one" />
        <span className="avatar-orbit orbit-two" />
        <Icon id="contactAvatarIcon">{call ? "phone_in_talk" : "add_box"}</Icon>
      </div>
      <div className="contact-main">
        <span className="eyebrow">{call ? (live ? t("call.active") : t("call.past")) : t("call.panelIdle")}</span>
        <h1>{call ? contactName(call, t) : t("call.ready")}</h1>
        <span>{call?.phoneNumber ? formatPhone(call.phoneNumber, t) : t("call.tapNewCall")}</span>
      </div>
      <div className="header-divider" />
      <div className="call-goal">
        <span className="eyebrow">{t("call.goal")}</span>
        <strong>{goal}</strong>
      </div>
      <div className="header-divider" />
      <div className="call-state">
        <span className="live-label"><i /><span>{call ? status.text : t("call.waiting")}</span></span>
        <strong>{timer}</strong>
        <div className="mini-eq" aria-hidden="true">
          <span /><span /><span /><span /><span />
        </div>
      </div>
      <div className="language-badge">
        <Icon>auto_awesome</Icon>
        <span>
          {call ? (
            <>
              {t("call.languageBadge")}<br />{callLanguageName(call.language, t("languages.autoShort"))}
            </>
          ) : (
            <>
              {t("call.languageBadgeIdle")}<br />{t("call.languageBadgeIdleSecond")}
            </>
          )}
        </span>
      </div>
    </header>
  );
}
