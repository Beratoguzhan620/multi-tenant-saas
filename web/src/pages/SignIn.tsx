import { Building2, Loader2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { slugify } from "../format";
import { navigate } from "../router";
import type { Session } from "../types";

export function SignIn({ mode, onSession }: { mode: "signin" | "signup"; onSession: (session: Session) => void }) {
  const signUp = mode === "signup";
  const [form, setForm] = useState({ organizationName: "", slug: "", displayName: "", email: "", password: "" });
  const [slugTouched, setSlugTouched] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const set = (key: keyof typeof form) => (e: { target: { value: string } }) => {
    const value = e.target.value;
    setForm((f) => ({ ...f, [key]: value, ...(key === "organizationName" && !slugTouched ? { slug: slugify(value) } : {}) }));
  };
  const fieldError = (name: string) => error?.fields[name]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const session = signUp ? await api.signUp(form) : await api.login(form.email, form.password);
      onSession(session);
      navigate("/", true);
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="center">
      <form className="card auth-card" onSubmit={submit} noValidate>
        <div className="brand"><span className="brand-mark"><Building2 size={17} /></span>Helpdesk</div>
        <div>
          <h1>{signUp ? "Start your help desk" : "Sign in"}</h1>
          <p className="muted small">{signUp ? "Create an organization; you will be its owner. Free plan, no card." : "Welcome back."}</p>
        </div>

        {signUp && (
          <>
            <label className="field">
              Organization name
              <input className="input" value={form.organizationName} onChange={set("organizationName")} autoComplete="organization" required />
              {fieldError("organizationName") && <span className="field-error">{fieldError("organizationName")}</span>}
            </label>
            <label className="field">
              Address
              <input className="input mono" value={form.slug} onChange={(e) => { setSlugTouched(true); set("slug")(e); }} required />
              <span className="hint">Your support form will live at /support/{form.slug || "your-company"}</span>
              {fieldError("slug") && <span className="field-error">{fieldError("slug")}</span>}
            </label>
            <label className="field">
              Your name
              <input className="input" value={form.displayName} onChange={set("displayName")} autoComplete="name" required />
              {fieldError("displayName") && <span className="field-error">{fieldError("displayName")}</span>}
            </label>
          </>
        )}

        <label className="field">
          Email
          <input className="input" type="email" value={form.email} onChange={set("email")} autoComplete="email" required />
          {fieldError("email") && <span className="field-error">{fieldError("email")}</span>}
        </label>
        <label className="field">
          Password
          <input className="input" type="password" value={form.password} onChange={set("password")} autoComplete={signUp ? "new-password" : "current-password"} required />
          {signUp && <span className="hint">At least 10 characters.</span>}
          {fieldError("password") && <span className="field-error">{fieldError("password")}</span>}
        </label>

        {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}

        <button className="button primary wide" disabled={busy}>
          {busy && <Loader2 size={16} className="spin" />}
          {signUp ? "Create organization" : "Sign in"}
        </button>
        <p className="small muted" style={{ textAlign: "center" }}>
          {signUp ? "Already have an account? " : "New here? "}
          <a href={signUp ? "/signin" : "/signup"} onClick={(e) => { e.preventDefault(); setError(null); navigate(signUp ? "/signin" : "/signup"); }}>
            {signUp ? "Sign in" : "Create an organization"}
          </a>
        </p>
      </form>
    </div>
  );
}
