import { useCallback } from "react";
import { apiClient } from "../api/client.js";
import { getClientId } from "../utils/format.js";

export function usePaymentActions({
  authRef,
  balance,
  setBalance,
  setTonPayments,
  showToast,
  t
}) {
  const redeemPromo = useCallback(async (code, afterSuccess) => {
    const trimmed = code.trim();
    if (!trimmed) {
      showToast(t("promo.enter"));
      return;
    }

    try {
      const clientId = getClientId(authRef.current);
      const nextBalance = await apiClient.redeemPromoCode({ clientId, code: trimmed });
      setBalance(nextBalance);
      afterSuccess?.();
      showToast(t("promo.applied"));
    } catch (error) {
      showToast(error.message || t("promo.failed"));
    }
  }, [authRef, setBalance, showToast, t]);

  const refreshTonDeposits = useCallback(async () => {
    try {
      const result = await apiClient.refreshTonDeposits();
      setBalance(result.balance || balance);
      setTonPayments(result.deposits || []);
      showToast(t("topup.checked"));
    } catch (error) {
      showToast(error.message || t("topup.checkFailed"));
    }
  }, [balance, setBalance, setTonPayments, showToast, t]);

  const copyPaymentAddress = useCallback(async address => {
    if (!address) return;
    await navigator.clipboard?.writeText(address);
    showToast(t("topup.copied"));
  }, [showToast, t]);

  const copyPaymentComment = useCallback(async comment => {
    if (!comment) return;
    await navigator.clipboard?.writeText(comment);
    showToast(t("topup.commentCopied"));
  }, [showToast, t]);

  return { copyPaymentAddress, copyPaymentComment, redeemPromo, refreshTonDeposits };
}
