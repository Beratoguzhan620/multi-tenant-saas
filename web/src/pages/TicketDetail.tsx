import { ArrowLeft, Download, FileText, Loader2, Lock, MessageSquare, Paperclip, Trash2 } from "lucide-react";
import { useRef, useState, type FormEvent } from "react";
import { api } from "../api";
import { useSession } from "../context";
import { bytes, canManage, dateTime, priorities, relative, statuses, statusHint } from "../format";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { Attachment, Ticket, TicketPriority, TicketStatus } from "../types";
import { StatusBadge } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";

function Attachments({ number, closed }: { number: number; closed: boolean }) {
  const { session } = useSession();
  const [files, reload, setFiles] = useLoad(() => api.attachments(number), [number]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const input = useRef<HTMLInputElement>(null);

  async function upload(file: File) {
    setBusy(true);
    setError(null);
    try {
      const added = await api.upload(number, file);
      setFiles([...(files.data ?? []), added]);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
      if (input.current) input.current.value = "";
    }
  }

  async function download(file: Attachment) {
    try {
      const blob = await api.download(file);
      const url = URL.createObjectURL(blob);
      const link = document.createElement("a");
      link.href = url;
      link.download = file.fileName;
      link.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function remove(file: Attachment) {
    setError(null);
    try {
      await api.deleteAttachment(file.id);
      setFiles((files.data ?? []).filter((f) => f.id !== file.id));
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <section className="card section" aria-label="Files">
      <div className="row">
        <h2 className="grow">Files</h2>
        {!closed && (
          <>
            <input ref={input} type="file" hidden aria-label="Choose a file" onChange={(e) => e.target.files?.[0] && upload(e.target.files[0])} />
            <button className="button small" disabled={busy} onClick={() => input.current?.click()}>
              {busy ? <Loader2 size={14} className="spin" /> : <Paperclip size={14} />}Attach
            </button>
          </>
        )}
      </div>
      {files.state === "failed" && !files.data && <Failed message={files.error} onRetry={reload} />}
      {files.state === "loading" && !files.data && <div className="skeleton" />}
      {files.data?.length === 0 && <p className="small muted">No files. Images, PDFs, text, CSV, ZIP, Word or Excel, up to 10 MB.</p>}
      {files.data?.map((file) => (
        <div key={file.id} className="file">
          <FileText size={16} className="muted" />
          <span className="grow">
            <button className="name ellipsis" style={{ display: "block", maxWidth: "100%" }} onClick={() => download(file)} title="Download">{file.fileName}</button>
            <span className="small muted">{bytes(file.sizeBytes)} · {relative(file.createdAt)}</span>
          </span>
          <button className="icon-button" onClick={() => download(file)} aria-label={`Download ${file.fileName}`}><Download size={15} /></button>
          {canManage(session.organization?.role) && (
            <button className="icon-button" onClick={() => remove(file)} aria-label={`Delete ${file.fileName}`}><Trash2 size={15} /></button>
          )}
        </div>
      ))}
      {error && <p className="field-error" role="alert">{error}</p>}
    </section>
  );
}

export function TicketDetail({ number }: { number: number }) {
  const [ticket, reloadTicket, setTicket] = useLoad(() => api.ticket(number), [number]);
  const [comments, reloadComments, setComments] = useLoad(() => api.comments(number), [number]);
  const [members] = useLoad(() => api.members(), []);
  const [body, setBody] = useState("");
  const [internal, setInternal] = useState(false);
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (ticket.state === "loading" && !ticket.data) return <Loading label="Loading the ticket" />;
  if (ticket.state === "failed" && !ticket.data) {
    return (
      <>
        <a href="/" className="back" onClick={(e) => { e.preventDefault(); navigate("/"); }}><ArrowLeft size={14} />Tickets</a>
        <Failed message={ticket.error} onRetry={reloadTicket} />
      </>
    );
  }
  const t: Ticket = ticket.data!;
  const closed = t.status === "Closed";

  async function update(change: Parameters<typeof api.updateTicket>[1]) {
    setError(null);
    try {
      setTicket(await api.updateTicket(number, change));
    } catch (e) {
      setError((e as Error).message);
    }
  }

  async function send(e: FormEvent) {
    e.preventDefault();
    if (!body.trim()) return;
    setSending(true);
    setError(null);
    try {
      const comment = await api.addComment(number, body, internal);
      setComments([...(comments.data ?? []), comment]);
      setBody("");
      reloadTicket();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setSending(false);
    }
  }

  return (
    <>
      <a href="/" className="back" onClick={(e) => { e.preventDefault(); navigate("/"); }}><ArrowLeft size={14} />Tickets</a>
      <div className="page-head">
        <div>
          <p className="ticket-no">#{t.number} · {t.channel === "Web" ? "support form" : "opened by the team"} · {dateTime(t.createdAt)}</p>
          <h1>{t.subject}</h1>
        </div>
        <StatusBadge status={t.status} />
      </div>

      <div className="ticket-layout">
        <section className="card" aria-label="Conversation">
          {comments.state === "failed" && !comments.data && <Failed message={comments.error} onRetry={reloadComments} />}
          {comments.state === "loading" && !comments.data && <Loading />}
          {comments.data?.length === 0 && <Empty title="No messages" icon={<MessageSquare size={22} />} />}
          {comments.data && comments.data.length > 0 && (
            <div className="thread">
              {comments.data.map((c) => (
                <article key={c.id} className={`message${c.isInternal ? " note" : c.byTeam ? " team" : ""}`}>
                  <div className="message-head">
                    <strong>{c.authorName}</strong>
                    {c.isInternal ? <span className="badge plain"><Lock size={11} />Internal note</span> : c.byTeam ? <span className="badge accent">Team</span> : <span className="badge plain">Customer</span>}
                    <span className="muted grow" style={{ textAlign: "right" }}>{dateTime(c.createdAt)}</span>
                  </div>
                  <div className="message-body">{c.body}</div>
                </article>
              ))}
            </div>
          )}
          {closed ? (
            <p className="composer muted small">This ticket is closed and takes no more replies.</p>
          ) : (
            <form className={`composer${internal ? " note" : ""}`} onSubmit={send}>
              <div className="tabs" role="tablist" aria-label="Message type" style={{ justifySelf: "start" }}>
                <button type="button" role="tab" aria-selected={!internal} className={`tab${!internal ? " active" : ""}`} onClick={() => setInternal(false)}>Reply to customer</button>
                <button type="button" role="tab" aria-selected={internal} className={`tab${internal ? " active" : ""}`} onClick={() => setInternal(true)}><Lock size={12} style={{ marginRight: 4 }} />Internal note</button>
              </div>
              <label className="sr-only" htmlFor="composer">{internal ? "Internal note" : "Reply"}</label>
              <textarea
                id="composer"
                className="textarea"
                rows={4}
                value={body}
                onChange={(e) => setBody(e.target.value)}
                placeholder={internal ? "Only your team sees this." : `Write to ${t.requesterName}…`}
              />
              <div className="row">
                <span className="small muted grow">{internal ? "The status stays as it is." : "Sending moves the ticket to Pending until the customer answers."}</span>
                <button className="button primary" disabled={sending || !body.trim()}>
                  {sending && <Loader2 size={16} className="spin" />}{internal ? "Add note" : "Send reply"}
                </button>
              </div>
              {error && <p className="form-error" role="alert">{error}</p>}
            </form>
          )}
        </section>

        <aside className="side">
          <section className="card section" aria-label="Details">
            <label className="field">
              Status
              <select className="select" value={t.status} onChange={(e) => update({ status: e.target.value as TicketStatus })} disabled={closed}>
                {statuses.map((s) => <option key={s} value={s}>{s}</option>)}
              </select>
              <span className="hint">{statusHint[t.status]}</span>
            </label>
            <label className="field">
              Priority
              <select className="select" value={t.priority} onChange={(e) => update({ priority: e.target.value as TicketPriority })} disabled={closed}>
                {priorities.map((p) => <option key={p}>{p}</option>)}
              </select>
            </label>
            <label className="field">
              Assignee
              <select
                className="select"
                value={t.assignee?.id ?? ""}
                onChange={(e) => update(e.target.value ? { assigneeId: e.target.value } : { unassign: true })}
                disabled={closed}
              >
                <option value="">Nobody</option>
                {(members.data ?? (t.assignee ? [{ id: t.assignee.id, displayName: t.assignee.name }] : [])).map((m) => (
                  <option key={m.id} value={m.id}>{m.displayName}</option>
                ))}
              </select>
            </label>
            <dl className="facts">
              <div><dt>Customer</dt><dd>{t.requesterName}<br /><span className="small muted">{t.requesterEmail}</span></dd></div>
              <div><dt>Last activity</dt><dd>{relative(t.updatedAt)}</dd></div>
            </dl>
          </section>
          <Attachments number={number} closed={closed} />
        </aside>
      </div>
    </>
  );
}
