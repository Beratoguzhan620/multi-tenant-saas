import { Check, Copy, ExternalLink, Loader2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { useSession } from "../context";
import { canManage } from "../format";

export function Settings() {
  const { session, setSession } = useSession();
  const org = session.organization!;
  const admin = canManage(org.role);
  const [name, setName] = useState(org.name);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const [copied, setCopied] = useState(false);
  const formUrl = `${location.origin}/support/${org.slug}`;

  async function save(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      await api.renameOrganization(name);
      setSession(await api.switchTo(org.id));
      setMessage({ ok: true, text: "Saved." });
    } catch (err) {
      setMessage({ ok: false, text: (err as Error).message });
    } finally {
      setBusy(false);
    }
  }

  return (
    <>
      <div className="page-head"><div><h1>Settings</h1></div></div>
      <section className="card section" aria-label="Support form">
        <h2>Support form</h2>
        <p className="small muted">Customers send requests here without an account. Link it from your site or email signature.</p>
        <div className="row wrap">
          <code className="mono grow" style={{ padding: "8px 10px", background: "var(--surface-2)", borderRadius: 6, wordBreak: "break-all" }}>{formUrl}</code>
          <button className="button small" onClick={() => navigator.clipboard?.writeText(formUrl).then(() => setCopied(true), () => {})}>
            {copied ? <Check size={14} /> : <Copy size={14} />}{copied ? "Copied" : "Copy"}
          </button>
          <a className="button small" href={formUrl} target="_blank" rel="noreferrer"><ExternalLink size={14} />Open</a>
        </div>
      </section>
      <section className="card section" aria-label="Organization">
        <h2>Organization</h2>
        <form className="stack" onSubmit={save} style={{ maxWidth: 420 }}>
          <label className="field">
            Name
            <input className="input" value={name} onChange={(e) => setName(e.target.value)} disabled={!admin} />
          </label>
          <label className="field">
            Address
            <input className="input mono" value={org.slug} readOnly />
            <span className="hint">The address cannot change: links to the support form keep working.</span>
          </label>
          {message && <p className={message.ok ? "form-ok" : "form-error"} role={message.ok ? "status" : "alert"}>{message.text}</p>}
          {admin && (
            <div><button className="button primary" disabled={busy || name.trim() === org.name}>{busy && <Loader2 size={16} className="spin" />}Save</button></div>
          )}
        </form>
      </section>
    </>
  );
}
