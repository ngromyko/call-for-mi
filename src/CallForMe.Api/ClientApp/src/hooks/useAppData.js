import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../api/client.js";
import { demoCall } from "../data/demoCall.js";
import { defaultAuth, defaultBalance, defaultConfig } from "../data/defaults.js";
import { getClientId } from "../utils/format.js";
import { isLive } from "../utils/callState.js";

export function useAppData({ showToast, t }) {
  const [config, setConfig] = useState(defaultConfig);
  const [auth, setAuth] = useState(defaultAuth);
  const [balance, setBalance] = useState(defaultBalance);
  const [calls, setCalls] = useState([]);
  const [activeCall, setActiveCall] = useState(demoCall);
  const [tonPayments, setTonPayments] = useState([]);
  const [adminUsers, setAdminUsers] = useState([]);
  const [adminTonPayments, setAdminTonPayments] = useState([]);
  const [promoCodes, setPromoCodes] = useState([]);

  const activeCallRef = useRef(activeCall);
  const authRef = useRef(auth);

  useEffect(() => {
    activeCallRef.current = activeCall;
  }, [activeCall]);

  useEffect(() => {
    authRef.current = auth;
  }, [auth]);

  const isAdmin = !!auth.authenticated && auth.user?.username?.toLowerCase() === "admin";
  const visibleCalls = auth.authenticated && calls.length ? calls : [demoCall];
  const ready = !!config.readyForRealCalls;

  const mergeCall = useCallback(call => {
    if (!call?.id) return;
    setCalls(current => {
      const next = current.some(item => item.id === call.id)
        ? current.map(item => item.id === call.id ? call : item)
        : [call, ...current];
      return next.sort((left, right) => new Date(right.createdAt) - new Date(left.createdAt));
    });
    setActiveCall(current => current?.id === call.id ? call : current);
  }, []);

  const loadConfig = useCallback(async () => {
    const nextConfig = await apiClient.config();
    setConfig(current => ({ ...current, ...nextConfig }));
    return nextConfig;
  }, []);

  const loadBalance = useCallback(async (authValue = authRef.current) => {
    const clientId = getClientId(authValue);
    const nextBalance = await apiClient.balance(clientId);
    setBalance(nextBalance);
    return nextBalance;
  }, []);

  const clearCallState = useCallback(() => {
    setCalls([]);
    setActiveCall(demoCall);
  }, []);

  const loadCalls = useCallback(async (selectId = null) => {
    if (!authRef.current?.authenticated) {
      clearCallState();
      return [];
    }

    try {
      const nextCalls = await apiClient.calls();
      setCalls(nextCalls);
      setActiveCall(current => {
        const selected = selectId ? nextCalls.find(call => call.id === selectId) : null;
        const retained = current?.id ? nextCalls.find(call => call.id === current.id) : null;
        return selected || retained || nextCalls.find(isLive) || nextCalls[0] || demoCall;
      });
      return nextCalls;
    } catch (error) {
      if (error.status === 401) {
        setAuth(defaultAuth);
        clearCallState();
        return [];
      }
      showToast(t("call.updateFailed"));
      return [];
    }
  }, [clearCallState, showToast, t]);

  const loadTonDepositInfo = useCallback(async () => {
    if (!authRef.current?.authenticated) {
      setTonPayments([]);
      return;
    }

    const [tonInfo, usdtInfo] = await Promise.all([
      apiClient.tonDepositInfo().catch(() => null),
      apiClient.usdtDepositInfo().catch(() => null)
    ]);

    setConfig(current => ({
      ...current,
      tonPayments: tonInfo ? { ...(current.tonPayments || {}), ...tonInfo, enabled: !!tonInfo.enabled } : { ...(current.tonPayments || {}), enabled: false },
      usdtPayments: usdtInfo ? { ...(current.usdtPayments || {}), ...usdtInfo, enabled: !!usdtInfo.enabled } : { ...(current.usdtPayments || {}), enabled: false }
    }));

    setTonPayments(await apiClient.tonDeposits().catch(() => []));
  }, []);

  const loadAdminData = useCallback(async () => {
    if (!authRef.current?.authenticated || authRef.current.user?.username?.toLowerCase() !== "admin") {
      setAdminUsers([]);
      setAdminTonPayments([]);
      setPromoCodes([]);
      return;
    }

    const [users, payments, promos] = await Promise.all([
      apiClient.adminUsers().catch(() => []),
      apiClient.adminTonPayments().catch(() => []),
      apiClient.promoCodes().catch(() => [])
    ]);
    setAdminUsers(users);
    setAdminTonPayments(payments);
    setPromoCodes(promos);
  }, []);

  return {
    config,
    setConfig,
    auth,
    setAuth,
    authRef,
    balance,
    setBalance,
    calls,
    setCalls,
    activeCall,
    setActiveCall,
    activeCallRef,
    tonPayments,
    setTonPayments,
    adminUsers,
    setAdminUsers,
    adminTonPayments,
    promoCodes,
    setPromoCodes,
    isAdmin,
    visibleCalls,
    ready,
    mergeCall,
    loadConfig,
    loadBalance,
    clearCallState,
    loadCalls,
    loadTonDepositInfo,
    loadAdminData
  };
}
