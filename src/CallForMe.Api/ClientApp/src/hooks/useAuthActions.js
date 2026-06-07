import { useCallback } from "react";
import { apiClient } from "../api/client.js";
import { defaultAuth } from "../data/defaults.js";
import { pushRoute } from "../utils/routing.js";

export function useAuthActions({
  authDialogMode,
  authRef,
  clearCallState,
  loadBalance,
  loadCalls,
  loadConfig,
  loadTonDepositInfo,
  resetRealtime,
  setAuth,
  setAuthSubmitting,
  setMobileView,
  showToast,
  t
}) {
  const submitAuth = useCallback(async payload => {
    setAuthSubmitting(true);
    try {
      const nextAuth = authDialogMode === "register"
        ? await apiClient.register(payload)
        : await apiClient.login(payload);
      setAuth(nextAuth);
      authRef.current = nextAuth;
      await loadBalance(nextAuth);
      await loadConfig().catch(() => {});
      await loadTonDepositInfo().catch(() => {});
      await loadCalls();
      showToast(authDialogMode === "register" ? t("auth.registerDone") : t("auth.loginDone"));
    } finally {
      setAuthSubmitting(false);
    }
  }, [authDialogMode, loadBalance, loadCalls, loadConfig, loadTonDepositInfo, setAuth, setAuthSubmitting, showToast, t]);

  const submitTelegramAuth = useCallback(async payload => {
    setAuthSubmitting(true);
    try {
      const nextAuth = await apiClient.telegramLogin(payload);
      setAuth(nextAuth);
      authRef.current = nextAuth;
      await loadBalance(nextAuth);
      await loadConfig().catch(() => {});
      await loadTonDepositInfo().catch(() => {});
      await loadCalls();
      showToast(t("auth.telegramDone"));
    } finally {
      setAuthSubmitting(false);
    }
  }, [authRef, loadBalance, loadCalls, loadConfig, loadTonDepositInfo, setAuth, setAuthSubmitting, showToast, t]);

  const logout = useCallback(async () => {
    try {
      await apiClient.logout();
      setAuth(defaultAuth);
      authRef.current = defaultAuth;
      clearCallState();
      resetRealtime();
      setMobileView("call");
      pushRoute("call", { replace: true });
      await loadConfig().catch(() => {});
      await loadBalance(defaultAuth).catch(() => {});
      showToast(t("auth.logoutDone"));
    } catch {
      showToast(t("auth.logoutFailed"));
    }
  }, [clearCallState, loadBalance, loadConfig, resetRealtime, setAuth, setMobileView, showToast, t]);

  return { logout, submitAuth, submitTelegramAuth };
}
