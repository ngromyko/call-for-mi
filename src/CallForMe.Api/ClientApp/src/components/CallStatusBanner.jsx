import { useEffect, useState } from "react";
import { Icon } from "./Dialog.jsx";
import { callStatusMeta, isLive, statusName } from "../utils/callState.js";
import { useI18n } from "../i18n/I18nContext.jsx";

export function CallStatusBanner({ call }) {
  const { t } = useI18n();
  const normalizedStatus = statusName(call?.status);
  const bannerKey = call ? `${call.id}:${normalizedStatus}` : "";
  const [dismissedKey, setDismissedKey] = useState("");

  useEffect(() => {
    if (dismissedKey && dismissedKey !== bannerKey) {
      setDismissedKey("");
    }
  }, [bannerKey, dismissedKey]);

  if (!call || isLive(call)) return null;
  if (dismissedKey === bannerKey) return null;

  const status = callStatusMeta(call, t);

  return (
    <div className={`call-action-state ${status.className === "complete" ? "complete" : status.className === "failed" ? "failed" : "warning"}`}>
      <Icon>{status.icon}</Icon>
      <div>
        <strong>{normalizedStatus === "Completed" ? t("call.ended") : status.text}</strong>
        <span>{actionText(normalizedStatus, call, t)}</span>
      </div>
      <button
        type="button"
        className="call-action-close"
        onClick={() => setDismissedKey(bannerKey)}
        aria-label={t("dialogs.close")}
      >
        <Icon>close</Icon>
      </button>
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
