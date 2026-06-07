import { CallHeader } from "./CallHeader.jsx";
import { Conversation } from "./Conversation.jsx";
import { HistoryPanel } from "./HistoryPanel.jsx";
import { ReplyArea } from "./ReplyArea.jsx";

export function CallWorkspace({
  call,
  calls,
  realCallsCount,
  config,
  isAdmin,
  elapsedSeconds,
  draft,
  onDraftChange,
  onRefreshCalls,
  onSelectCall,
  onHideCall,
  onSendMessage,
  onToggleAutoPilot,
  onEndCall,
  onEnsureSummary
}) {
  return (
    <main className="main">
      <CallHeader call={call} config={config} elapsedSeconds={elapsedSeconds} />
      <section className="mobile-home-history" aria-label="Call history">
        <HistoryPanel
          calls={calls}
          activeCall={call}
          realCallsCount={realCallsCount}
          onRefresh={onRefreshCalls}
          onSelect={onSelectCall}
          onHide={onHideCall}
        />
      </section>
      <section className="conversation-card">
        <div className="call-energy" aria-hidden="true">
          <span /><span /><span /><span /><span />
          <span /><span /><span /><span /><span />
          <span /><span /><span /><span /><span />
        </div>
        <Conversation call={call} config={config} isAdmin={isAdmin} onEnsureSummary={onEnsureSummary} />
        <ReplyArea
          call={call}
          draft={draft}
          onDraftChange={onDraftChange}
          onSendMessage={onSendMessage}
          onToggleAutoPilot={onToggleAutoPilot}
          onEndCall={onEndCall}
        />
      </section>
    </main>
  );
}
