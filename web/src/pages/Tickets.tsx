import { ChevronLeft, ChevronRight, Inbox, Loader2, Plus, Search } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { priorities, relative, statuses } from "../format";
import { useDebounced, useLoad } from "../hooks";
import { navigate } from "../router";
import type { Member, TicketPriority, TicketStatus } from "../types";
import { PriorityLabel, StatusBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { Empty, Failed, SkeletonRows } from "../components/States";

const PAGE_SIZE = 25;

function NewTicketDialog({ members, onClose }: { members: Member[]; onClose: () => void }) {
  const [form, setForm] = useState({ subject: "", requesterName: "", requesterEmail: "", message: "", priority: "Normal" as TicketPriority, assigneeId: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const ticket = await api.openTicket({ ...form, assigneeId: form.assigneeId || undefined });
      onClose();
      navigate(`/tickets/${ticket.number}`);
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title="New ticket" onClose={onClose}>
      <p className="small muted">For a request that came by phone or email. The message is recorded as the customer's.</p>
      <form className="stack" onSubmit={submit} noValidate>
        <div className="two">
          <label className="field">Customer name<input className="input" value={form.requesterName} onChange={set("requesterName")} required /></label>
          <label className="field">Customer email<input className="input" type="email" value={form.requesterEmail} onChange={set("requesterEmail")} required /></label>
        </div>
        <label className="field">Subject<input className="input" value={form.subject} onChange={set("subject")} maxLength={200} required /></label>
        <label className="field">Message<textarea className="textarea" value={form.message} onChange={set("message")} required /></label>
        <div className="two">
          <label className="field">
            Priority
            <select className="select" value={form.priority} onChange={set("priority")}>
              {priorities.map((p) => <option key={p}>{p}</option>)}
            </select>
          </label>
          <label className="field">
            Assignee
            <select className="select" value={form.assigneeId} onChange={set("assigneeId")}>
              <option value="">Nobody yet</option>
              {members.map((m) => <option key={m.id} value={m.id}>{m.displayName}</option>)}
            </select>
          </label>
        </div>
        {error && <p className="form-error" role="alert">{error.message}</p>}
        <div className="dialog-actions">
          <button type="button" className="button" onClick={onClose}>Cancel</button>
          <button className="button primary" disabled={busy}>{busy && <Loader2 size={16} className="spin" />}Open ticket</button>
        </div>
      </form>
    </Dialog>
  );
}

export function Tickets() {
  const [status, setStatus] = useState<TicketStatus | "">("");
  const [assignee, setAssignee] = useState("");
  const [text, setText] = useState("");
  const [page, setPage] = useState(1);
  const [creating, setCreating] = useState(false);
  const q = useDebounced(text);

  const [tickets, reload] = useLoad(
    () => api.tickets({ status: status || undefined, assignee: assignee || undefined, q, page, pageSize: PAGE_SIZE }),
    [status, assignee, q, page],
  );
  const [members] = useLoad(() => api.members(), []);
  const memberList = members.state === "ready" ? members.data : [];
  const filtered = Boolean(status || assignee || q);
  const data = tickets.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  const choose = <T,>(setter: (value: T) => void) => (value: T) => { setter(value); setPage(1); };

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Tickets</h1>
          <p className="muted small">{data ? `${data.total} ${data.total === 1 ? "ticket" : "tickets"}${filtered ? " match" : ""}` : " "}</p>
        </div>
        <button className="button primary" onClick={() => setCreating(true)}><Plus size={16} />New ticket</button>
      </div>

      <div className="card">
        <div className="toolbar">
          <div className="tabs" role="tablist" aria-label="Status">
            {(["", ...statuses] as const).map((s) => (
              <button key={s || "all"} role="tab" aria-selected={status === s} className={`tab${status === s ? " active" : ""}`} onClick={() => choose(setStatus)(s)}>
                {s || "All"}
              </button>
            ))}
          </div>
          <select className="select" aria-label="Assignee" value={assignee} onChange={(e) => choose(setAssignee)(e.target.value)}>
            <option value="">Anyone</option>
            <option value="me">Assigned to me</option>
            <option value="unassigned">Unassigned</option>
            {memberList.map((m) => <option key={m.id} value={m.id}>{m.displayName}</option>)}
          </select>
          <label className="search">
            <Search size={15} />
            <span className="sr-only">Search tickets</span>
            <input className="input" placeholder="Search subject, customer or #number" value={text} onChange={(e) => { setText(e.target.value); setPage(1); }} />
          </label>
        </div>

        {tickets.state === "failed" && !data ? (
          <Failed message={tickets.error} onRetry={reload} />
        ) : data && data.items.length === 0 ? (
          filtered ? (
            <Empty title="No tickets match" icon={<Search size={22} />}>
              <button className="link small" onClick={() => { setStatus(""); setAssignee(""); setText(""); }}>Clear filters</button>
            </Empty>
          ) : (
            <Empty title="No tickets yet" icon={<Inbox size={24} />}>
              <span className="small">Share your support form, or open a ticket for a customer.</span>
            </Empty>
          )
        ) : (
          <table className="table" aria-busy={tickets.state === "loading"}>
            <thead>
              <tr>
                <th style={{ width: 80 }}>#</th>
                <th>Subject</th>
                <th className="hide-sm">Customer</th>
                <th>Status</th>
                <th className="hide-sm">Priority</th>
                <th className="hide-sm">Assignee</th>
                <th className="hide-sm">Updated</th>
              </tr>
            </thead>
            {!data ? <SkeletonRows columns={7} /> : (
              <tbody style={{ opacity: tickets.state === "loading" ? 0.6 : 1 }}>
                {data.items.map((t) => (
                  <tr key={t.number} className="clickable" onClick={() => navigate(`/tickets/${t.number}`)}>
                    <td className="ticket-no">#{t.number}</td>
                    <td>
                      <a href={`/tickets/${t.number}`} className="subject" style={{ color: "inherit", textDecoration: "none" }} onClick={(e) => e.preventDefault()}>{t.subject}</a>
                    </td>
                    <td className="hide-sm"><span className="ellipsis" style={{ display: "block", maxWidth: 200 }}>{t.requesterName}</span><span className="small muted">{t.requesterEmail}</span></td>
                    <td><StatusBadge status={t.status} /></td>
                    <td className="hide-sm"><PriorityLabel priority={t.priority} /></td>
                    <td className="hide-sm">{t.assignee?.name ?? <span className="muted">—</span>}</td>
                    <td className="hide-sm muted small" title={new Date(t.updatedAt).toLocaleString()}>{relative(t.updatedAt)}</td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
        )}

        {data && data.total > PAGE_SIZE && (
          <div className="pager">
            <span>Page {page} of {pages}</span>
            <div className="row">
              <button className="button small" disabled={page <= 1} onClick={() => setPage(page - 1)} aria-label="Previous page"><ChevronLeft size={15} /></button>
              <button className="button small" disabled={page >= pages} onClick={() => setPage(page + 1)} aria-label="Next page"><ChevronRight size={15} /></button>
            </div>
          </div>
        )}
      </div>

      {creating && <NewTicketDialog members={memberList} onClose={() => setCreating(false)} />}
    </>
  );
}
