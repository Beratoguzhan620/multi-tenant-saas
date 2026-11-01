import { Check, Copy, LifeBuoy, Loader2, Send } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { dateTime } from "../format";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { PublicTicket, PublicTicketCreated } from "../types";
import { StatusBadge } from "../components/Badges";
import { Failed, Loading } from "../components/States";

function PublicFrame({ name, children }: { name?: string; children: React.ReactNode }) {
  return (
    <div className="public">
      <div className="card public-card">
        <div className="brand"><span className="brand-mark" style={{ background: "var(--accent)" }}><LifeBuoy size={17} /></span>{name ?? "Support"}</div>
        {children}
      </div>
      <p className="public-footer">Powered by Helpdesk</p>
    </div>
  );
}

const ticketLink = (slug: string, created: PublicTicketCreated) =>
  `${location.origin}/support/${encodeURIComponent(slug)}/tickets/${created.number}#${created.accessKey}`;

/** The customer's form at /support/{slug}. No account; the answer is a private link to follow the request. */
export function SupportForm({ slug }: { slug: string }) {
  const [organization] = useLoad(() => api.publicOrganization(slug), [slug]);
  const [form, setForm] = useState({ name: "", email: "", subject: "", message: "" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [created, setCreated] = useState<PublicTicketCreated | null>(null);
  const [copied, setCopied] = useState(false);
  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => setForm((f) => ({ ...f, [key]: e.target.value }));

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setCreated(await api.publicOpen(slug, form));
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  if (organization.state === "loading") return <PublicFrame><Loading /></PublicFrame>;
  if (organization.state === "failed") return <PublicFrame><Failed message={organization.error} /></PublicFrame>;
  const name = organization.data.name;

  if (created) {
    const link = ticketLink(slug, created);
    return (
      <PublicFrame name={name}>
        <div>
          <h1>Request #{created.number} received</h1>
          <p className="muted">The {name} team will answer here. Keep this link: it is the only way to follow your request, and it cannot be shown again.</p>
        </div>
        <div className="key-box">
          <code>{link}</code>
          <div className="row">
            <button className="button small" onClick={() => { navigator.clipboard?.writeText(link).then(() => setCopied(true), () => {}); }}>
              {copied ? <Check size={14} /> : <Copy size={14} />}{copied ? "Copied" : "Copy link"}
            </button>
            <a className="button small primary" href={link} onClick={(e) => { e.preventDefault(); navigate(link.slice(location.origin.length)); }}>Open my request</a>
          </div>
        </div>
      </PublicFrame>
    );
  }

  const fieldError = (key: string) => error?.fields[key]?.[0];
  return (
    <PublicFrame name={name}>
      <div>
        <h1>How can we help?</h1>
        <p className="muted">Send a request to the {name} team. You will get a link to follow the answer.</p>
      </div>
      <form className="stack" onSubmit={submit} noValidate>
        <div className="two">
          <label className="field">Your name<input className="input" value={form.name} onChange={set("name")} autoComplete="name" required /></label>
          <label className="field">Email<input className="input" type="email" value={form.email} onChange={set("email")} autoComplete="email" required /></label>
        </div>
        <label className="field">Subject<input className="input" value={form.subject} onChange={set("subject")} maxLength={200} required />{fieldError("subject") && <span className="field-error">{fieldError("subject")}</span>}</label>
        <label className="field">Message<textarea className="textarea" rows={6} value={form.message} onChange={set("message")} required /></label>
        {error && <p className="form-error" role="alert">{error.message}</p>}
        <div className="row" style={{ justifyContent: "flex-end" }}>
          <button className="button primary" disabled={busy}>{busy ? <Loader2 size={16} className="spin" /> : <Send size={15} />}Send request</button>
        </div>
      </form>
    </PublicFrame>
  );
}

/** The customer's view of their request: messages from the team (never internal notes) and a reply box. */
export function SupportTicket({ slug, number, accessKey }: { slug: string; number: number; accessKey: string }) {
  const [organization] = useLoad(() => api.publicOrganization(slug), [slug]);
  const [ticket, reload, setTicket] = useLoad(() => api.publicTicket(slug, number, accessKey), [slug, number, accessKey]);
  const [reply, setReply] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function send(e: FormEvent) {
    e.preventDefault();
    if (!reply.trim()) return;
    setBusy(true);
    setError(null);
    try {
      setTicket(await api.publicReply(slug, number, accessKey, reply));
      setReply("");
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const name = organization.state === "ready" ? organization.data.name : undefined;
  if (ticket.state === "loading" && !ticket.data) return <PublicFrame name={name}><Loading /></PublicFrame>;
  if (ticket.state === "failed" && !ticket.data) return <PublicFrame name={name}><Failed message={ticket.error} onRetry={reload} /></PublicFrame>;
  const t: PublicTicket = ticket.data!;

  return (
    <PublicFrame name={name}>
      <div className="row" style={{ alignItems: "flex-start" }}>
        <div className="grow">
          <p className="ticket-no">#{t.number}</p>
          <h1>{t.subject}</h1>
        </div>
        <StatusBadge status={t.status} />
      </div>
      <div className="stack" style={{ gap: 10 }}>
        {t.messages.map((m) => (
          <div key={m.id} className={`message${m.byTeam ? " team" : ""}`}>
            <div className="message-head">
              <strong>{m.byTeam ? `${m.authorName} · ${name ?? "Support"}` : "You"}</strong>
              <span className="muted">{dateTime(m.createdAt)}</span>
            </div>
            <div className="message-body">{m.body}</div>
          </div>
        ))}
      </div>
      {t.status === "Closed" ? (
        <p className="muted small">This request is closed. Send a new one if you need more help.</p>
      ) : (
        <form className="stack" onSubmit={send}>
          <label className="field">Reply<textarea className="textarea" rows={4} value={reply} onChange={(e) => setReply(e.target.value)} /></label>
          {error && <p className="form-error" role="alert">{error}</p>}
          <div className="row" style={{ justifyContent: "flex-end" }}>
            <button className="button primary" disabled={busy || !reply.trim()}>{busy ? <Loader2 size={16} className="spin" /> : <Send size={15} />}Send reply</button>
          </div>
        </form>
      )}
    </PublicFrame>
  );
}
