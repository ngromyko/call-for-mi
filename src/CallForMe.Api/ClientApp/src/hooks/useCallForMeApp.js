import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../api/client.js";
import { defaultAuth } from "../data/defaults.js";
import { useI18n } from "../i18n/I18nContext.jsx";
import { pushRoute, isAdminPath } from "../utils/routing.js";
import { useAdminActions } from "./useAdminActions.js";
import { useAppData } from "./useAppData.js";
import { useAuthActions } from "./useAuthActions.js";
import { useBodyClasses } from "./useBodyClasses.js";
import { useCallActions } from "./useCallActions.js";
import { usePaymentActions } from "./usePaymentActions.js";
import { useRealtimeCalls } from "./useRealtimeCalls.js";
import { useServiceWorker } from "./useServiceWorker.js";
import { useToast } from "./useToast.js";

const storedViewKey = "callforme_selected_view";

function storedMobileView() {
  try {
    const view = localStorage.getItem(storedViewKey);
    return view === "account" ? "account" : "call";
  } catch {
    return "call";
  }
}

function saveMobileView(view) {
  if (view !== "account" && view !== "call") return;
  try {
    localStorage.setItem(storedViewKey, view);
  } catch {
  }
}

export function useCallForMeApp() {
  const { t } = useI18n();
  const { toast, showToast } = useToast();
  const [mobileView, setMobileView] = useState(() => isAdminPath() ? "admin" : storedMobileView());
  const [draft, setDraft] = useState("");
  const [authDialog, setAuthDialog] = useState({ open: false, mode: "login" });
  const [newCallOpen, setNewCallOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [newCallSubmitting, setNewCallSubmitting] = useState(false);
  const [authSubmitting, setAuthSubmitting] = useState(false);
  const bootedRef = useRef(false);
  const summaryRequestsRef = useRef(new Set());

  const data = useAppData({ showToast, t });
  const realtime = useRealtimeCalls({
    activeCall: data.activeCall,
    activeCallRef: data.activeCallRef,
    auth: data.auth,
    authRef: data.authRef,
    loadBalance: data.loadBalance,
    mergeCall: data.mergeCall,
    setActiveCall: data.setActiveCall
  });

  useServiceWorker();
  useBodyClasses({ activeCall: data.activeCall, mobileView });

  useEffect(() => {
    if (!isAdminPath()) {
      saveMobileView(mobileView);
    }
  }, [mobileView]);

  const openAuth = useCallback((mode = "login") => {
    setAuthDialog({ open: true, mode });
  }, []);

  const closeAuth = useCallback(() => {
    setAuthDialog(current => ({ ...current, open: false }));
  }, []);

  const openSettings = useCallback(async ({ fromRoute = false } = {}) => {
    if (fromRoute) {
      setMobileView("admin");
    }

    try {
      await data.loadConfig();
      if (data.authRef.current?.authenticated && data.authRef.current.user?.username?.toLowerCase() === "admin") {
        await data.loadAdminData();
        setMobileView("admin");
        if (!fromRoute) pushRoute("admin");
        return;
      }

      if (!data.authRef.current?.authenticated) {
        openAuth("login");
        showToast(t("setup.loginAsAdmin"));
        return;
      }

      showToast(t("setup.adminOnlySettings"));
    } catch {
      showToast(t("setup.openFailed"));
    }
  }, [data, openAuth, showToast, t]);

  const closeAdminPanel = useCallback(() => {
    setMobileView("call");
    pushRoute("call", { replace: true });
  }, []);

  const authActions = useAuthActions({
    authDialogMode: authDialog.mode,
    authRef: data.authRef,
    clearCallState: data.clearCallState,
    loadBalance: data.loadBalance,
    loadCalls: data.loadCalls,
    loadConfig: data.loadConfig,
    loadTonDepositInfo: data.loadTonDepositInfo,
    resetRealtime: realtime.resetRealtime,
    setAuth: data.setAuth,
    setAuthSubmitting,
    setMobileView,
    showToast,
    t
  });

  const callActions = useCallActions({
    activeCallRef: data.activeCallRef,
    auth: data.auth,
    balance: data.balance,
    config: data.config,
    isAdmin: data.isAdmin,
    loadBalance: data.loadBalance,
    mergeCall: data.mergeCall,
    openAuth,
    setActiveCall: data.setActiveCall,
    setCalls: data.setCalls,
    setMobileView,
    setNewCallOpen,
    setNewCallSubmitting,
    showToast,
    summaryRequestsRef,
    subscribeActiveCall: realtime.subscribeActiveCall,
    t
  });

  const paymentActions = usePaymentActions({
    authRef: data.authRef,
    balance: data.balance,
    setBalance: data.setBalance,
    setTonPayments: data.setTonPayments,
    showToast,
    t
  });

  const adminActions = useAdminActions({
    authRef: data.authRef,
    config: data.config,
    loadAdminData: data.loadAdminData,
    loadConfig: data.loadConfig,
    setAdminUsers: data.setAdminUsers,
    setPromoCodes: data.setPromoCodes,
    showToast,
    t
  });

  useEffect(() => {
    if (bootedRef.current) return;
    bootedRef.current = true;

    async function boot() {
      try {
        await data.loadConfig();
        const nextAuth = await apiClient.currentUser().catch(() => defaultAuth);
        data.setAuth(nextAuth);
        data.authRef.current = nextAuth;
        await data.loadBalance(nextAuth).catch(() => {});
        await data.loadTonDepositInfo().catch(() => {});
        await data.loadCalls();
        if (isAdminPath()) {
          await openSettings({ fromRoute: true });
        }
      } catch {
        showToast(t("boot.configFailed"));
      }
    }

    boot();
  }, [data, openSettings, showToast, t]);

  useEffect(() => {
    function onPopState() {
      if (isAdminPath()) {
        openSettings({ fromRoute: true });
      } else {
        setMobileView(storedMobileView());
      }
    }

    window.addEventListener("popstate", onPopState);
    return () => window.removeEventListener("popstate", onPopState);
  }, [openSettings]);

  const changeMobileView = useCallback(view => {
    if (view === "admin") {
      openSettings();
      return;
    }
    setMobileView(view === "account" ? "account" : "call");
  }, [openSettings]);

  return {
    admin: {
      actions: adminActions,
      tonPayments: data.adminTonPayments,
      users: data.adminUsers,
      promoCodes: data.promoCodes
    },
    auth: data.auth,
    authDialog,
    authSubmitting,
    balance: data.balance,
    calls: data.calls,
    callActions,
    closeAdminPanel,
    closeAuth,
    config: data.config,
    draft,
    elapsedSeconds: realtime.elapsedSeconds,
    helpOpen,
    isAdmin: data.isAdmin,
    mobileView,
    newCallOpen,
    newCallSubmitting,
    openAuth,
    openSettings,
    paymentActions,
    ready: data.ready,
    setAuthDialog,
    setDraft,
    setHelpOpen,
    setNewCallOpen,
    setMobileView: changeMobileView,
    toast,
    tonPayments: data.tonPayments,
    visibleCalls: data.visibleCalls,
    onLoadDepositInfo: async () => {
      await data.loadTonDepositInfo();
      if (!data.config.tonPayments?.enabled && !data.config.usdtPayments?.enabled) {
        showToast(t("topup.notConfigured"));
      }
    },
    onRefreshCalls: () => data.loadCalls(data.activeCall?.id),
    submitAuth: authActions.submitAuth,
    logout: authActions.logout,
    activeCall: data.activeCall
  };
}
