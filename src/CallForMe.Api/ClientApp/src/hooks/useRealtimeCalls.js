import { useCallback, useEffect, useRef, useState } from "react";
import { apiClient } from "../api/client.js";
import { hasCallChanged } from "../utils/callMetrics.js";
import { isDemoCall, isLive } from "../utils/callState.js";

export function useRealtimeCalls({
  activeCall,
  activeCallRef,
  auth,
  authRef,
  loadBalance,
  mergeCall,
  setActiveCall
}) {
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const hubRef = useRef(null);
  const subscribedCallIdRef = useRef(null);

  const resetRealtime = useCallback(() => {
    subscribedCallIdRef.current = null;
  }, []);

  const subscribeActiveCall = useCallback(async callId => {
    const hub = hubRef.current;
    if (!hub || !callId || isDemoCall(callId) || subscribedCallIdRef.current === callId) return;

    try {
      if (subscribedCallIdRef.current) {
        await hub.invoke("UnsubscribeCall", subscribedCallIdRef.current);
      }
      await hub.invoke("SubscribeCall", callId);
      subscribedCallIdRef.current = callId;
    } catch {
      subscribedCallIdRef.current = null;
    }
  }, []);

  useEffect(() => {
    if (!auth.authenticated || hubRef.current || !window.signalR) return;

    const hub = new window.signalR.HubConnectionBuilder()
      .withUrl("/hubs/calls")
      .withAutomaticReconnect()
      .build();

    hub.on("CallUpdated", call => {
      if (!authRef.current?.authenticated) return;
      mergeCall(call);
      if (!isLive(call)) {
        loadBalance().catch(() => {});
      }
    });

    hub.on("TranscriptAdded", entry => {
      if (!authRef.current?.authenticated) return;
      const current = activeCallRef.current;
      if (!current?.id || current.transcript?.some(item => item.id === entry.id)) return;

      const updated = { ...current, transcript: [...(current.transcript || []), entry] };
      mergeCall(updated);
      setActiveCall(updated);
    });

    hub.onreconnected(() => {
      if (activeCallRef.current?.id) {
        subscribeActiveCall(activeCallRef.current.id);
      }
    });

    hub.start()
      .then(() => {
        hubRef.current = hub;
        if (activeCallRef.current?.id) subscribeActiveCall(activeCallRef.current.id);
      })
      .catch(() => {
        hubRef.current = null;
      });
  }, [activeCallRef, auth.authenticated, authRef, loadBalance, mergeCall, setActiveCall, subscribeActiveCall]);

  useEffect(() => {
    if (activeCall?.id) {
      subscribeActiveCall(activeCall.id);
    }
  }, [activeCall?.id, subscribeActiveCall]);

  useEffect(() => {
    const interval = setInterval(() => {
      const current = activeCallRef.current;
      if (!isLive(current)) {
        setElapsedSeconds(current?.durationSeconds || 0);
        return;
      }
      setElapsedSeconds(Math.max(0, (Date.now() - new Date(current.createdAt).getTime()) / 1000));
    }, 1000);

    return () => clearInterval(interval);
  }, [activeCallRef]);

  useEffect(() => {
    const interval = setInterval(async () => {
      const current = activeCallRef.current;
      if (!current?.id || isDemoCall(current) || !isLive(current)) return;

      try {
        const nextCall = await apiClient.call(current.id);
        if (hasCallChanged(current, nextCall)) {
          mergeCall(nextCall);
          setActiveCall(nextCall);
          if (!isLive(nextCall)) {
            loadBalance().catch(() => {});
          }
        }
      } catch {
      }
    }, 2500);

    return () => clearInterval(interval);
  }, [activeCallRef, loadBalance, mergeCall, setActiveCall]);

  return {
    elapsedSeconds,
    resetRealtime,
    subscribeActiveCall
  };
}
