import { Building2, Loader2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { Session } from "../types";
import { Failed, Loading } from "../components/States";

/** Opened from an invitation link: /join#token. */
export function Join({ token, session, onSession }: { token: string; session: Session | null; onSession: (s: Session) => void }) {
  const [preview, reload] = useLoad(() => api.previewInvitation(token), [token]);
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  async function run(action: () => Promise<Session>) {
    setBusy(true);
    setError(null);
    try {
      onSession(await action());
      navigate("/", true);
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  const submit = (e: FormEvent) => { e.preventDefault(); run(() => api.join(token, displayName, password)); };

  return (
    <div className="center">
      <div className="card auth-card">
        <div className="brand"><span className="brand-mark"><Building2 size={17} /></span>Helpdesk</div>
        {preview.state === "loading" && <Loading label="Checking the invitation" />}
        {preview.state === "failed" && <Failed message={preview.error} onRetry={reload} />}
        {preview.state === "ready" && (() => {
          const invitation = preview.data;
          if (invitation.expired) {
            return <p className="form-error" role="alert">This invitation to {invitation.organizationName} has expired. Ask for a new link.</p>;
          }

          const signedInAsInvitee = session?.user.email === invitation.email;
          return (
            <>
              <div>
                <h1>Join {invitation.organizationName}</h1>
                <p className="muted small">You were invited as {invitation.role === "Admin" ? "an admin" : "an agent"}, with {invitation.email}.</p>
              </div>
              {signedInAsInvitee ? (
                <button className="button primary wide" disabled={busy} onClick={() => run(() => api.acceptInvitation(token))}>
                  {busy && <Loader2 size={16} className="spin" />}Accept and join
                </button>
              ) : invitation.hasAccount ? (
                <p className="small">
                  You already have an account. <a href="/signin" onClick={(e) => { e.preventDefault(); navigate(`/signin`); }}>Sign in</a>, then open this link again.
                </p>
              ) : (
                <form className="stack" onSubmit={submit} noValidate>
                  <label className="field">
                    Email
                    <input className="input" value={invitation.email} readOnly />
                  </label>
                  <label className="field">
                    Your name
                    <input className="input" value={displayName} onChange={(e) => setDisplayName(e.target.value)} autoComplete="name" required />
                    {error?.fields.displayName && <span className="field-error">{error.fields.displayName[0]}</span>}
                  </label>
                  <label className="field">
                    Password
                    <input className="input" type="password" value={password} onChange={(e) => setPassword(e.target.value)} autoComplete="new-password" required />
                    <span className="hint">At least 10 characters.</span>
                    {error?.fields.password && <span className="field-error">{error.fields.password[0]}</span>}
                  </label>
                  <button className="button primary wide" disabled={busy}>{busy && <Loader2 size={16} className="spin" />}Create account and join</button>
                </form>
              )}
              {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
            </>
          );
        })()}
      </div>
    </div>
  );
}
