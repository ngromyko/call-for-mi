export function isAdminPath(pathname = window.location.pathname) {
  const normalized = (pathname || "/").replace(/\/+$/, "").toLowerCase();
  return normalized === "/admin" || normalized.startsWith("/admin/");
}

export function pushRoute(view, { replace = false } = {}) {
  const target = view === "admin" ? "/admin" : "/";
  const normalized = (window.location.pathname || "/").replace(/\/+$/, "").toLowerCase() || "/";
  if (normalized === target) return;

  const method = replace ? "replaceState" : "pushState";
  window.history[method]?.({ view }, "", target);
}
