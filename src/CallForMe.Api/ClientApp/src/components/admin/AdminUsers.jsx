import { Icon } from "../Dialog.jsx";
import { useI18n } from "../../i18n/I18nContext.jsx";
import { formatBalance, formatDuration, formatShortDate } from "../../utils/format.js";

export function AdminUsers({ users, onRefresh }) {
  const { t } = useI18n();

  return (
    <section className="admin-users-section">
      <div className="settings-section-heading">
        <div>
          <h3>{t("admin.users")}</h3>
          <p>{t("admin.usersHelp")}</p>
        </div>
        <button type="button" className="secondary-button" onClick={onRefresh}>
          <Icon>refresh</Icon>
          {t("admin.refresh")}
        </button>
      </div>
      <div className="admin-users-list">
        {(users || []).length ? users.map(user => {
          const duration = formatDuration(user.totalDurationSeconds || 0) || "0:00";
          return (
            <article key={user.id || user.username} className="admin-user-item">
              <div className="admin-user-main">
                <strong>{user.displayName || user.username}</strong>
                <span>{t("admin.created", { date: formatShortDate(user.createdAt) })}</span>
              </div>
              <div className="admin-user-stats">
                <span><strong>{formatBalance(user.balance)}</strong><small>{t("admin.credits")}</small></span>
                <span><strong>{user.totalCalls || 0}</strong><small>{t("admin.calls")}</small></span>
                <span><strong>{user.completedCalls || 0}</strong><small>{t("admin.completed")}</small></span>
                <span><strong>{user.missedCalls || 0}</strong><small>{t("admin.missed")}</small></span>
                <span><strong>{duration}</strong><small>{t("admin.time")}</small></span>
              </div>
              <span className="admin-user-last">{t("admin.lastCall", { date: formatShortDate(user.lastCallAt) })}</span>
            </article>
          );
        }) : <div className="empty-history">{t("admin.emptyUsers")}</div>}
      </div>
    </section>
  );
}
