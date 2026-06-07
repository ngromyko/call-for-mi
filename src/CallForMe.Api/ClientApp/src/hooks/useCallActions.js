import { useCallback } from "react";
import { apiClient } from "../api/client.js";
import { demoCall } from "../data/demoCall.js";
import { callPrice } from "../utils/callMetrics.js";
import { formatBalance } from "../utils/format.js";
import { isDemoCall, isLive } from "../utils/callState.js";

export function useCallActions({
  activeCallRef,
  auth,
  balance,
  config,
  isAdmin,
  loadBalance,
  mergeCall,
  openAuth,
  setActiveCall,
  setCalls,
  setMobileView,
  setNewCallOpen,
  setNewCallSubmitting,
  showToast,
  summaryRequestsRef,
  subscribeActiveCall,
  t
}) {
  const openNewCall = useCallback(() => {
    if (!auth.authenticated) {
      openAuth("login");
      return;
    }

    const price = callPrice(config);
    if (Number(balance?.balance || 0) < price) {
      showToast(t("call.oneMinuteCostProfile", { price: formatBalance(price) }));
      return;
    }

    if (!config.readyForRealCalls) {
      showToast(isAdmin ? (config.setupReason || t("setup.finishBeforeCall")) : t("setup.adminOnly"));
      return;
    }

    setNewCallOpen(true);
  }, [auth.authenticated, balance?.balance, config, isAdmin, openAuth, setNewCallOpen, showToast, t]);

  const submitNewCall = useCallback(async payload => {
    setNewCallSubmitting(true);
    try {
      const call = await apiClient.createCall(payload);
      mergeCall(call);
      setActiveCall(call);
      setMobileView("call");
      await subscribeActiveCall(call.id);
      await loadBalance().catch(() => {});
      showToast(t("call.realStarted"));
    } finally {
      setNewCallSubmitting(false);
    }
  }, [loadBalance, mergeCall, setActiveCall, setMobileView, setNewCallSubmitting, showToast, subscribeActiveCall, t]);

  const selectCall = useCallback(async id => {
    if (isDemoCall(id)) {
      setActiveCall(demoCall);
      setMobileView("call");
      return;
    }

    try {
      const call = await apiClient.call(id);
      mergeCall(call);
      setActiveCall(call);
      setMobileView("call");
      await subscribeActiveCall(call.id);
    } catch {
      showToast(t("call.openFailed"));
    }
  }, [mergeCall, setActiveCall, setMobileView, showToast, subscribeActiveCall, t]);

  const hideCall = useCallback(async id => {
    try {
      await apiClient.hideCall(id);
      setCalls(current => current.filter(call => call.id !== id));
      setActiveCall(current => current?.id === id ? demoCall : current);
      showToast(t("call.hidden"));
    } catch (error) {
      showToast(error.message || t("call.hideFailed"));
    }
  }, [setActiveCall, setCalls, showToast, t]);

  const sendMessage = useCallback(async (text, spokenText = null) => {
    const current = activeCallRef.current;
    if (!current?.id || isDemoCall(current)) {
      showToast(t("conversation.demoToast"));
      return;
    }

    try {
      const call = await apiClient.sendMessage(current.id, { text, spokenText });
      mergeCall(call);
      setActiveCall(call);
    } catch (error) {
      showToast(error.message || t("conversation.sendFailed"));
    }
  }, [activeCallRef, mergeCall, setActiveCall, showToast, t]);

  const toggleAutoPilot = useCallback(async enabled => {
    const current = activeCallRef.current;
    if (!current?.id || isDemoCall(current)) {
      showToast(t("call.startFirst"));
      return;
    }

    try {
      const call = await apiClient.setAutoPilot(current.id, enabled);
      mergeCall(call);
      setActiveCall(call);
      showToast(enabled ? t("conversation.autopilotOn") : t("conversation.autopilotOff"));
    } catch {
      showToast(t("conversation.autopilotFailed"));
    }
  }, [activeCallRef, mergeCall, setActiveCall, showToast, t]);

  const endCall = useCallback(async () => {
    const current = activeCallRef.current;
    if (!current?.id || isDemoCall(current)) {
      showToast(t("call.noCall"));
      return;
    }

    try {
      const call = await apiClient.endCall(current.id);
      mergeCall(call);
      setActiveCall(call);
      showToast(t("call.ended"));
    } catch (error) {
      try {
        const call = await apiClient.call(current.id);
        mergeCall(call);
        setActiveCall(call);
        if (!isLive(call)) {
          showToast(t("call.alreadyEnded"));
          return;
        }
      } catch {
      }
      showToast(error.message || t("call.endFailed"));
    }
  }, [activeCallRef, mergeCall, setActiveCall, showToast, t]);

  const ensureSummary = useCallback(async call => {
    if (!call?.id || isDemoCall(call) || isLive(call) || call.summary || !(call.transcript || []).length || summaryRequestsRef.current.has(call.id)) {
      return;
    }

    summaryRequestsRef.current.add(call.id);
    try {
      const nextCall = await apiClient.summarizeCall(call.id);
      mergeCall(nextCall);
      setActiveCall(nextCall);
    } catch (error) {
      showToast(error.message || t("conversation.summarizeFailed"));
    } finally {
      summaryRequestsRef.current.delete(call.id);
    }
  }, [mergeCall, setActiveCall, showToast, summaryRequestsRef, t]);

  return {
    endCall,
    ensureSummary,
    hideCall,
    openNewCall,
    selectCall,
    sendMessage,
    submitNewCall,
    toggleAutoPilot
  };
}
