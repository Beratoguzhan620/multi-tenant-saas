import { History, Loader2 } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { auditVerbs, dateTime } from "../format";
import { useLoad } from "../hooks";
import type { AuditEvent } from "../types";
import { Empty, Failed, Loading } from "../components/States";

export function Audit() {
  const [first, reload] = useLoad(() => api.audit(), []);
  const [more, setMore] = useState<AuditEvent[]>([]);
  const [next, setNext] = useState<string | null | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (first.state === "loading" && !first.data) return <Loading />;
  if (first.state === "failed" && !first.data) return <Failed message={first.error} onRetry={reload} />;
  const cursor = next === undefined ? first.data!.next : next;
  const events = [...first.data!.items, ...more];

  async function loadMore() {
    setBusy(true);
    setError(null);
    try {
      const page = await api.audit(cursor);
      setMore((m) => [...m, ...page.items]);
      setNext(page.next);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Audit log</h1>
          <p className="muted small">Changes to the team, the plan, settings and tickets. It can be added to, never edited.</p>
        </div>
      </div>
      <div className="card">
        {events.length === 0 ? <Empty title="Nothing yet" icon={<History size={22} />} /> : events.map((e) => (
          <div key={e.id} className="audit-row">
            <span className="small muted">{dateTime(e.at)}</span>
            <span>
              <strong style={{ fontWeight: 600 }}>{e.actorName}</strong> {auditVerbs[e.action] ?? e.action} <strong style={{ fontWeight: 500 }}>{e.target}</strong>
              {e.details && <span className="muted"> · {e.details}</span>}
            </span>
          </div>
        ))}
      </div>
      {error && <p className="form-error" role="alert" style={{ marginTop: 12 }}>{error}</p>}
      {cursor && (
        <div style={{ marginTop: 12, textAlign: "center" }}>
          <button className="button" onClick={loadMore} disabled={busy}>{busy && <Loader2 size={16} className="spin" />}Show older</button>
        </div>
      )}
    </>
  );
}
