import { useEffect } from "react";

function isTelegramWebView() {
  return /Telegram|TelegramBot|tgWebApp/i.test(navigator.userAgent || "");
}

export function useServiceWorker() {
  useEffect(() => {
    if (!("serviceWorker" in navigator)) return;

    if (isTelegramWebView()) {
      navigator.serviceWorker.getRegistrations()
        .then(registrations => registrations.forEach(registration => registration.unregister()))
        .catch(() => {});
      return;
    }

    window.addEventListener("load", () => {
      navigator.serviceWorker.register("/service-worker.js").catch(() => {});
    }, { once: true });
  }, []);
}
