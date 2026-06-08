import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { mkdirSync, writeFileSync } from "node:fs";
import { resolve } from "node:path";

const defaultSiteUrl = "https://callforme.xyz";

function normalizeSiteUrl(value) {
  return value?.trim().replace(/\/+$/, "") ?? "";
}

function readSiteUrl() {
  return normalizeSiteUrl(process.env.VITE_PUBLIC_SITE_URL || process.env.PUBLIC_SITE_URL || defaultSiteUrl);
}

function escapeHtml(value) {
  return value
    .replaceAll("&", "&amp;")
    .replaceAll('"', "&quot;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;");
}

function seoFilesPlugin() {
  let outDir = "";

  return {
    name: "callforme-seo-files",
    apply: "build",
    configResolved(config) {
      outDir = resolve(config.root, config.build.outDir);
    },
    transformIndexHtml(html) {
      const siteUrl = readSiteUrl();
      if (!siteUrl) {
        return html;
      }

      const homeUrl = `${siteUrl}/`;
      const imageUrl = `${siteUrl}/og-preview.png?v=20260608`;
      return html
        .replace(/<meta property="og:url" content="[^"]*">/, `<meta property="og:url" content="${escapeHtml(homeUrl)}">`)
        .replace(/<meta property="og:image" content="[^"]*">/, `<meta property="og:image" content="${escapeHtml(imageUrl)}">`)
        .replace(/<meta property="og:image:secure_url" content="[^"]*">/, `<meta property="og:image:secure_url" content="${escapeHtml(imageUrl)}">`)
        .replace(/<meta name="twitter:image" content="[^"]*">/, `<meta name="twitter:image" content="${escapeHtml(imageUrl)}">`)
        .replace(/<link rel="canonical" href="[^"]*">/, `<link rel="canonical" href="${escapeHtml(homeUrl)}">`);
    },
    closeBundle() {
      const siteUrl = readSiteUrl();
      const robotsLines = [
        "User-agent: *",
        "Allow: /",
        "Disallow: /admin",
        "Disallow: /admin/",
        "Disallow: /api/",
        "Disallow: /twilio/",
        "Disallow: /hubs/"
      ];

      mkdirSync(outDir, { recursive: true });

      if (siteUrl) {
        robotsLines.push("", `Sitemap: ${siteUrl}/sitemap.xml`);
        writeFileSync(
          resolve(outDir, "sitemap.xml"),
          [
            '<?xml version="1.0" encoding="UTF-8"?>',
            '<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">',
            "  <url>",
            `    <loc>${siteUrl}/</loc>`,
            "    <changefreq>weekly</changefreq>",
            "    <priority>1.0</priority>",
            "  </url>",
            "</urlset>",
            ""
          ].join("\n"),
          "utf8"
        );
      }

      writeFileSync(resolve(outDir, "robots.txt"), `${robotsLines.join("\n")}\n`, "utf8");
    }
  };
}

export default defineConfig({
  base: "/",
  plugins: [react(), seoFilesPlugin()],
  build: {
    outDir: "../wwwroot",
    emptyOutDir: false,
    manifest: false,
    rollupOptions: {
      output: {
        entryFileNames: "assets/[name]-[hash].js",
        chunkFileNames: "assets/[name]-[hash].js",
        assetFileNames: "assets/[name]-[hash][extname]"
      }
    }
  }
});
