import { useCallback } from "react";
import { apiClient } from "../api/client.js";
import { validateTwilioPayload } from "../utils/adminValidation.js";
import { wait } from "../utils/callMetrics.js";

export function useAdminActions({
  authRef,
  config,
  loadAdminData,
  loadConfig,
  setAdminUsers,
  setPromoCodes,
  showToast,
  t
}) {
  const saveOpenAi = useCallback(async (payload, afterSuccess) => {
    if (!payload.apiKey?.trim()) {
      showToast(t("admin.openAiKeyRequired"));
      return;
    }

    try {
      await apiClient.saveOpenAi({ apiKey: payload.apiKey.trim(), model: payload.model?.trim() });
      afterSuccess?.();
      await loadConfig();
      showToast(t("admin.openAiSaved"));
    } catch (error) {
      showToast(error.status === 401
        ? (authRef.current?.authenticated ? t("setup.adminOnlySettings") : t("setup.loginAsAdmin"))
        : (error.message || t("admin.openAiSaveFailed")));
    }
  }, [authRef, loadConfig, showToast, t]);

  const saveTwilio = useCallback(async (payload, afterSuccess) => {
    const validation = validateTwilioPayload(payload, config, t);
    if (validation) {
      showToast(validation);
      return;
    }

    try {
      await apiClient.saveTwilio({
        accountSid: payload.accountSid,
        authToken: payload.authToken,
        fromNumber: payload.fromNumber.trim(),
        publicBaseUrl: payload.publicBaseUrl.trim()
      });
      afterSuccess?.();
      await loadConfig();
      showToast(t("admin.twilioSaved"));
    } catch (error) {
      showToast(error.status === 401
        ? (authRef.current?.authenticated ? t("setup.adminOnlySettings") : t("setup.loginAsAdmin"))
        : (error.message || t("admin.twilioSaveFailed")));
    }
  }, [authRef, config, loadConfig, showToast, t]);

  const checkTwilio = useCallback(async () => {
    try {
      await apiClient.checkTwilio();
      await wait(700);
      await loadConfig();
      showToast(t("admin.twilioValid"));
    } catch (error) {
      await wait(700);
      await loadConfig().catch(() => {});
      showToast(error.status === 401
        ? (authRef.current?.authenticated ? t("setup.adminOnlySettings") : t("setup.loginAsAdmin"))
        : (error.message || t("admin.twilioInvalid")));
    }
  }, [authRef, loadConfig, showToast, t]);

  const saveTon = useCallback(async payload => {
    try {
      await apiClient.saveTon(payload);
      await loadConfig();
      await loadAdminData();
      showToast(t("admin.tonSaved"));
    } catch (error) {
      showToast(error.message || t("admin.tonSaveFailed"));
    }
  }, [loadAdminData, loadConfig, showToast, t]);

  const saveUsdt = useCallback(async payload => {
    try {
      await apiClient.saveUsdt(payload);
      await loadConfig();
      showToast(t("admin.usdtSaved"));
    } catch (error) {
      showToast(error.message || t("admin.usdtSaveFailed"));
    }
  }, [loadConfig, showToast, t]);

  const createPromo = useCallback(async (payload, afterSuccess) => {
    try {
      await apiClient.createPromoCode(payload);
      afterSuccess?.();
      setPromoCodes(await apiClient.promoCodes());
      showToast(t("admin.promoSaved"));
    } catch (error) {
      showToast(error.message || t("admin.promoSaveFailed"));
    }
  }, [setPromoCodes, showToast, t]);

  const togglePromo = useCallback(async (id, active) => {
    try {
      await apiClient.togglePromoCode(id, active);
      setPromoCodes(await apiClient.promoCodes());
      showToast(t("admin.promoUpdated"));
    } catch (error) {
      showToast(error.message || t("admin.promoUpdateFailed"));
    }
  }, [setPromoCodes, showToast, t]);

  const refreshAdminUsers = useCallback(async () => {
    try {
      setAdminUsers(await apiClient.adminUsers());
      showToast(t("admin.usersLoaded"));
    } catch (error) {
      showToast(error.message || t("admin.usersFailed"));
    }
  }, [setAdminUsers, showToast, t]);

  const refreshConfig = useCallback(async () => {
    try {
      await loadConfig();
      showToast(t("admin.configRefreshed"));
    } catch {
      showToast(t("admin.configRefreshFailed"));
    }
  }, [loadConfig, showToast, t]);

  return {
    checkTwilio,
    createPromo,
    refreshAdminUsers,
    refreshConfig,
    saveOpenAi,
    saveTon,
    saveTwilio,
    saveUsdt,
    togglePromo
  };
}
