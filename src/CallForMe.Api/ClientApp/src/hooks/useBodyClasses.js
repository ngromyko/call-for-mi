import { useEffect } from "react";
import { isFailedCall, isLive, speakerName, statusName } from "../utils/callState.js";

export function useBodyClasses({ activeCall, mobileView }) {
  useEffect(() => {
    function applyClasses() {
      const transcript = activeCall?.transcript || [];
      const latest = transcript.length ? transcript[transcript.length - 1] : null;
      const remoteSpeaking = isLive(activeCall) && speakerName(latest) === "Remote";
      const normalizedStatus = statusName(activeCall?.status);
      const statusClass = !activeCall
        ? "call-status-idle"
        : isFailedCall(activeCall) || ["Busy", "NoAnswer", "Canceled"].includes(normalizedStatus)
          ? "call-status-failed"
          : normalizedStatus === "Completed"
            ? "call-status-complete"
            : normalizedStatus === "Ringing"
              ? "call-status-ringing"
            : normalizedStatus === "InProgress"
              ? "call-status-live"
              : "call-status-dialing";

      document.body.className = [
        `mobile-view-${mobileView}`,
        activeCall ? "has-active-call" : "",
        isLive(activeCall) ? "is-live-call" : "",
        remoteSpeaking ? "remote-speaking" : "",
        statusClass
      ].filter(Boolean).join(" ");
    }

    applyClasses();
  }, [activeCall, mobileView]);
}
