import { Building2, Loader2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useSession } from "../context";
import { slugify } from "../format";
import { navigate } from "../router";
import { Dialog } from "../components/Dialog";

function OrganizationForm({ onDone, submitLabel }: { onDone: () => void; submitLabel: string }) {
  const { setSession } = useSession();
  const [name, setName] = useState("");
  const [slug, setSlug] = useState("");
  const [touched, setTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      setSession(await api.createOrganization(name, slug));
      onDone();
      navigate("/");
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <form className="stack" onSubmit={submit} noValidate>
      <label className="field">
        Organization name
        <input className="input" value={name} onChange={(e) => { setName(e.target.value); if (!touched) setSlug(slugify(e.target.value)); }} required />
        {error?.fields.name && <span className="field-error">{error.fields.name[0]}</span>}
      </label>
      <label className="field">
        Address
        <input className="input mono" value={slug} onChange={(e) => { setTouched(true); setSlug(e.target.value); }} required />
        {error?.fields.slug && <span className="field-error">{error.fields.slug[0]}</span>}
      </label>
      {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
      <div className="dialog-actions">
        <button className="button primary" disabled={busy}>{busy && <Loader2 size={16} className="spin" />}{submitLabel}</button>
      </div>
    </form>
  );
}

export function NewOrganizationDialog({ onClose }: { onClose: () => void }) {
  return (
    <Dialog title="New organization" onClose={onClose}>
      <p className="small muted">You will be its owner, on the Free plan. Its tickets and team are separate from your other organizations.</p>
      <OrganizationForm onDone={onClose} submitLabel="Create" />
    </Dialog>
  );
}

/** Someone who left or was removed from every organization they had. */
export function NoOrganization() {
  const { session, signOut } = useSession();
  return (
    <div className="center">
      <div className="card auth-card">
        <div className="brand"><span className="brand-mark"><Building2 size={17} /></span>Helpdesk</div>
        <div>
          <h1>No organization yet</h1>
          <p className="muted small">{session.user.email} does not belong to an organization. Create one, or open an invitation link someone sent you.</p>
        </div>
        <OrganizationForm onDone={() => {}} submitLabel="Create organization" />
        <button className="link small" onClick={signOut}>Sign out</button>
      </div>
    </div>
  );
}
