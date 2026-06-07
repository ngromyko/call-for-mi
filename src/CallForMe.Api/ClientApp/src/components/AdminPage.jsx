import { AdminUsers } from "./admin/AdminUsers.jsx";
import { MissingBox } from "./admin/MissingBox.jsx";
import { OpenAiSettings } from "./admin/OpenAiSettings.jsx";
import { PromoSettings } from "./admin/PromoSettings.jsx";
import { SettingsStatus } from "./admin/SettingsStatus.jsx";
import { TonSettings } from "./admin/TonSettings.jsx";
import { TwilioSettings } from "./admin/TwilioSettings.jsx";
import { UsdtSettings } from "./admin/UsdtSettings.jsx";
import { Icon } from "./Dialog.jsx";
import { useI18n } from "../i18n/I18nContext.jsx";

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
  onRefreshConfig,
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

  return (
    <section className="admin-page" hidden={!open}>
      <div className="admin-page-inner">
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
            <AdminUsers users={users} onRefresh={onRefreshUsers} />
            <SettingsStatus config={config} onCheckTwilio={onCheckTwilio} />
            <MissingBox config={config} />
            <OpenAiSettings config={config} onSave={onSaveOpenAi} />
            <TwilioSettings config={config} onSave={onSaveTwilio} onCheck={onCheckTwilio} />
            <TonSettings config={config} payments={tonPayments} onSave={onSaveTon} />
            <UsdtSettings config={config} onSave={onSaveUsdt} />
            <PromoSettings promoCodes={promoCodes} onCreate={onCreatePromo} onToggle={onTogglePromo} />
          </>
        ) : (
          <AdminAccessGate authenticated={auth?.authenticated} onOpenAuth={onOpenAuth} />
        )}

        {isAdmin ? (
          <div className="settings-section collapsed-help">
            <details>
              <summary>{t("admin.commands")}</summary>
              <pre>{setupCommandTemplate}</pre>
            </details>
          </div>
        ) : null}

        <div className="modal-actions">
          <button type="button" className="secondary-button" onClick={onRefreshConfig}>
            <Icon>refresh</Icon>
            {t("admin.checkAgain")}
          </button>
          <button type="button" className="primary-button" onClick={onClose}>{t("admin.done")}</button>
        </div>
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
