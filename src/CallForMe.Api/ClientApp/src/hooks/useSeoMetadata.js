import { useEffect } from "react";

const title = "Call for me — ИИ-помощник для звонков и переводов";
const description =
  "ИИ-помощник для телефонных звонков: переводит разговор, предлагает ответы и помогает общаться с сервисами на другом языке.";
const socialDescription =
  "ИИ помогает звонить в клиники, банки, службы доставки и поддержку на другом языке: переводит разговор, предлагает ответы и ведёт историю звонков.";
const productionSiteUrl = "https://callforme.xyz";

function upsertMeta(attribute, name, content) {
  let element = document.head.querySelector(`meta[${attribute}="${name}"]`);
  if (!element) {
    element = document.createElement("meta");
    element.setAttribute(attribute, name);
    document.head.appendChild(element);
  }

  element.setAttribute("content", content);
}

function upsertCanonical(url) {
  let element = document.head.querySelector('link[rel="canonical"]');
  if (!element) {
    element = document.createElement("link");
    element.setAttribute("rel", "canonical");
    document.head.appendChild(element);
  }

  element.setAttribute("href", url);
}

function isLocalHost(hostname) {
  return hostname === "localhost" || hostname === "127.0.0.1" || hostname === "::1";
}

function getSiteOrigin() {
  const configuredUrl = import.meta.env.VITE_PUBLIC_SITE_URL?.trim().replace(/\/+$/, "");
  if (configuredUrl) return configuredUrl;

  if (isLocalHost(window.location.hostname)) {
    return window.location.origin;
  }

  return productionSiteUrl;
}

export function useSeoMetadata() {
  useEffect(() => {
    const siteOrigin = getSiteOrigin();
    const siteUrl = new URL("/", siteOrigin).toString();
    const imageUrl = new URL("/icon-512.png", siteOrigin).toString();

    document.title = title;
    upsertCanonical(siteUrl);
    upsertMeta("name", "description", description);
    upsertMeta("name", "robots", "index, follow");
    upsertMeta("property", "og:type", "website");
    upsertMeta("property", "og:site_name", "Call for me");
    upsertMeta("property", "og:title", title);
    upsertMeta("property", "og:description", socialDescription);
    upsertMeta("property", "og:locale", "ru_RU");
    upsertMeta("property", "og:url", siteUrl);
    upsertMeta("property", "og:image", imageUrl);
    upsertMeta("name", "twitter:card", "summary");
    upsertMeta("name", "twitter:title", title);
    upsertMeta("name", "twitter:description", "Перевод телефонных разговоров, подсказки ответов и история звонков в одном веб-приложении.");
    upsertMeta("name", "twitter:image", imageUrl);
  }, []);
}
