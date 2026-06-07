import { AdminUsers } from "./admin/AdminUsers.jsx";
import { MissingBox } from "./admin/MissingBox.jsx";
import { OpenAiSettings } from "./admin/OpenAiSettings.jsx";
import { PromoSettings } from "./admin/PromoSettings.jsx";
import { SettingsStatus } from "./admin/SettingsStatus.jsx";
import { TonSettings } from "./admin/TonSettings.jsx";
import { TwilioSettings } from "./admin/TwilioSettings.jsx";
import { UsdtSettings } from "./admin/UsdtSettings.jsx";
import { Brand } from "./Brand.jsx";
import { Icon } from "./Dialog.jsx";
import { useI18n } from "../i18n/I18nContext.jsx";
import { useState } from "react";

const setupCommandTemplate = `dotnet user-secrets init --project src/CallForMe.Api
dotnet user-secrets set "Twilio:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:AccountSid" "ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:AuthToken" "your_twilio_auth_token" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:FromNumber" "+1234567890" --project src/CallForMe.Api
dotnet user-secrets set "Twilio:PublicBaseUrl" "https://your-public-url.example" --project src/CallForMe.Api
dotnet user-secrets set "AI:Enabled" "true" --project src/CallForMe.Api
dotnet user-secrets set "AI:ApiKey" "sk-..." --project src/CallForMe.Api`;

export function AdminPage({
  open,
  auth,
  config,
  isAdmin,
  users,
  promoCodes,
  tonPayments,
  onClose,
  onOpenAuth,
  onRefreshUsers,
  onSaveOpenAi,
  onSaveTwilio,
  onCheckTwilio,
  onSaveTon,
  onSaveUsdt,
  onCreatePromo,
  onTogglePromo
}) {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState("overview");
  const tabs = [
    ["overview", "dashboard", t("admin.tabOverview")],
    ["settings", "tune", t("admin.tabSettings")],
    ["payments", "account_balance_wallet", t("admin.tabPayments")],
    ["promo", "sell", t("admin.tabPromo")],
    ["commands", "terminal", t("admin.tabCommands")]
  ];

  return (
    <section className="admin-page" hidden={!open}>
      <div className="admin-page-inner">
        <Brand className="screen-brand" />
        <div className="admin-page-header">
          <div>
            <span className="eyebrow">{t("admin.eyebrow")}</span>
            <h2>{t("admin.title")}</h2>
            <p>{t("admin.help")}</p>
          </div>
          <button type="button" className="icon-button" onClick={onClose} aria-label={t("admin.back")}>
            <Icon>arrow_back</Icon>
          </button>
        </div>

        {isAdmin ? (
          <>
            <div className="admin-tabs" role="tablist" aria-label={t("admin.tabsLabel")}>
              {tabs.map(([id, icon, label]) => (
                <button
                  key={id}
                  type="button"
                  className={activeTab === id ? "active" : ""}
                  role="tab"
                  aria-selected={activeTab === id}
                  aria-controls={`admin-panel-${id}`}
                  id={`admin-tab-${id}`}
                  onClick={() => setActiveTab(id)}
                >
                  <Icon>{icon}</Icon>
                  <span>{label}</span>
                </button>
              ))}
            </div>

            <div
              id="admin-panel-overview"
              className="admin-tab-panel"
              role="tabpanel"
              aria-labelledby="admin-tab-overview"
              hidden={activeTab !== "overview"}
            >
              <SettingsStatus config={config} onCheckTwilio={onCheckTwilio} />
              <MissingBox config={config} />
              <AdminUsers users={users} onRefresh={onRefreshUsers} />
            </div>

            <div
              id="admin-panel-settings"
              className="admin-tab-panel"
              role="tabpanel"
              aria-labelledby="admin-tab-settings"
              hidden={activeTab !== "settings"}
            >
              <OpenAiSettings config={config} onSave={onSaveOpenAi} />
              <TwilioSettings config={config} onSave={onSaveTwilio} onCheck={onCheckTwilio} />
            </div>

            <div
              id="admin-panel-payments"
              className="admin-tab-panel"
              role="tabpanel"
              aria-labelledby="admin-tab-payments"
              hidden={activeTab !== "payments"}
            >
              <TonSettings config={config} payments={tonPayments} onSave={onSaveTon} />
              <UsdtSettings config={config} onSave={onSaveUsdt} />
            </div>

            <div
              id="admin-panel-promo"
              className="admin-tab-panel"
              role="tabpanel"
              aria-labelledby="admin-tab-promo"
              hidden={activeTab !== "promo"}
            >
              <PromoSettings users={users} promoCodes={promoCodes} onCreate={onCreatePromo} onToggle={onTogglePromo} />
            </div>

            <div
              id="admin-panel-commands"
              className="admin-tab-panel"
              role="tabpanel"
              aria-labelledby="admin-tab-commands"
              hidden={activeTab !== "commands"}
            >
              <div className="settings-section collapsed-help admin-commands-panel">
                <details open>
                  <summary>{t("admin.commands")}</summary>
                  <pre>{setupCommandTemplate}</pre>
                </details>
              </div>
            </div>
          </>
        ) : (
          <AdminAccessGate authenticated={auth?.authenticated} onOpenAuth={onOpenAuth} />
        )}
      </div>
    </section>
  );
}

function AdminAccessGate({ authenticated, onOpenAuth }) {
  const { t } = useI18n();

  return (
    <section className="settings-section">
      <div className="settings-section-heading">
        <div>
          <h3>{t("admin.title")}</h3>
          <p>{authenticated ? t("setup.adminOnlySettings") : t("setup.loginAsAdmin")}</p>
        </div>
        {!authenticated ? (
          <button type="button" className="primary-button" onClick={() => onOpenAuth?.("login")}>
            <Icon>login</Icon>
            {t("auth.login")}
          </button>
        ) : null}
      </div>
    </section>
  );
}
