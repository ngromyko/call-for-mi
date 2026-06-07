import { AdminPage } from "./AdminPage.jsx";
import { AuthDialog } from "./AuthDialog.jsx";
import { CallWorkspace } from "./CallWorkspace.jsx";
import { HelpDialog } from "./HelpDialog.jsx";
import { MobileNavigation } from "./MobileNavigation.jsx";
import { NewCallDialog } from "./NewCallDialog.jsx";
import { Sidebar } from "./Sidebar.jsx";

export function AppView({ app }) {
  if (app.mobileView === "admin") {
    return (
      <>
        <AdminPage
          open
          auth={app.auth}
          config={app.config}
          isAdmin={app.isAdmin}
          users={app.admin.users}
          promoCodes={app.admin.promoCodes}
          tonPayments={app.admin.tonPayments}
          onClose={app.closeAdminPanel}
          onOpenAuth={app.openAuth}
          onRefreshConfig={app.admin.actions.refreshConfig}
          onRefreshUsers={app.admin.actions.refreshAdminUsers}
          onSaveOpenAi={app.admin.actions.saveOpenAi}
          onSaveTwilio={app.admin.actions.saveTwilio}
          onCheckTwilio={app.admin.actions.checkTwilio}
          onSaveTon={app.admin.actions.saveTon}
          onSaveUsdt={app.admin.actions.saveUsdt}
          onCreatePromo={app.admin.actions.createPromo}
          onTogglePromo={app.admin.actions.togglePromo}
        />
        <AuthDialog
          open={app.authDialog.open}
          mode={app.authDialog.mode}
          submitting={app.authSubmitting}
          onModeChange={mode => app.setAuthDialog(current => ({ ...current, mode }))}
          onClose={app.closeAuth}
          onSubmit={app.submitAuth}
        />
        <div className={`toast ${app.toast ? "visible" : ""}`} role="status">{app.toast}</div>
      </>
    );
  }

  return (
    <>
      <div className="app-shell">
        <Sidebar
          auth={app.auth}
          balance={app.balance}
          calls={app.visibleCalls}
          realCallsCount={app.calls.length}
          activeCall={app.activeCall}
          config={app.config}
          tonPayments={app.tonPayments}
          isAdmin={app.isAdmin}
          view={app.mobileView}
          onNewCall={app.callActions.openNewCall}
          onOpenAuth={app.openAuth}
          onLogout={app.logout}
          onRefreshCalls={app.onRefreshCalls}
          onSelectCall={app.callActions.selectCall}
          onHideCall={app.callActions.hideCall}
          onOpenSettings={app.openSettings}
          onOpenHelp={() => app.setHelpOpen(true)}
          onRedeemPromo={app.paymentActions.redeemPromo}
          onLoadDepositInfo={app.onLoadDepositInfo}
          onRefreshTon={app.paymentActions.refreshTonDeposits}
          onCopyAddress={app.paymentActions.copyPaymentAddress}
          onCopyComment={app.paymentActions.copyPaymentComment}
        />
        <CallWorkspace
          call={app.activeCall}
          calls={app.visibleCalls}
          realCallsCount={app.calls.length}
          config={app.config}
          isAdmin={app.isAdmin}
          elapsedSeconds={app.elapsedSeconds}
          draft={app.draft}
          onDraftChange={app.setDraft}
          onRefreshCalls={app.onRefreshCalls}
          onSelectCall={app.callActions.selectCall}
          onHideCall={app.callActions.hideCall}
          onSendMessage={app.callActions.sendMessage}
          onToggleAutoPilot={app.callActions.toggleAutoPilot}
          onEndCall={app.callActions.endCall}
          onEnsureSummary={app.callActions.ensureSummary}
        />
      </div>

      <MobileNavigation
        view={app.mobileView}
        isAdmin={app.isAdmin}
        ready={app.ready}
        onChangeView={app.setMobileView}
        onNewCall={app.callActions.openNewCall}
      />

      <NewCallDialog
        open={app.newCallOpen}
        submitting={app.newCallSubmitting}
        onClose={() => app.setNewCallOpen(false)}
        onSubmit={app.callActions.submitNewCall}
      />
      <AuthDialog
        open={app.authDialog.open}
        mode={app.authDialog.mode}
        submitting={app.authSubmitting}
        onModeChange={mode => app.setAuthDialog(current => ({ ...current, mode }))}
        onClose={app.closeAuth}
        onSubmit={app.submitAuth}
      />
      <HelpDialog open={app.helpOpen} onClose={() => app.setHelpOpen(false)} />
      <div className={`toast ${app.toast ? "visible" : ""}`} role="status">{app.toast}</div>
    </>
  );
}
