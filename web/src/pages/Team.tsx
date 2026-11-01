import { Check, Copy, Crown, Loader2, Mail, UserPlus, X } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useSession } from "../context";
import { canManage, relative } from "../format";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { Invitation, Member, Role } from "../types";
import { initials } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { Empty, Failed, SkeletonRows } from "../components/States";

function InviteDialog({ myRole, onClose, onInvited }: { myRole: Role; onClose: () => void; onInvited: (i: Invitation) => void }) {
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Agent");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);
  const [link, setLink] = useState<string | null>(null);
  const [copied, setCopied] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await api.invite(email, role);
      onInvited(created.invitation);
      setLink(`${location.origin}/join#${created.token}`);
    } catch (err) {
      setError(err as ApiError);
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog title="Invite someone" onClose={onClose}>
      {link ? (
        <>
          <p className="small">Send this link to <strong>{email}</strong>. It works once, for that address, for 7 days, and is not shown again.</p>
          <div className="key-box">
            <code>{link}</code>
            <button className="button small" style={{ justifySelf: "start" }} onClick={() => navigator.clipboard?.writeText(link).then(() => setCopied(true), () => {})}>
              {copied ? <Check size={14} /> : <Copy size={14} />}{copied ? "Copied" : "Copy link"}
            </button>
          </div>
          <div className="dialog-actions"><button className="button primary" onClick={onClose}>Done</button></div>
        </>
      ) : (
        <form className="stack" onSubmit={submit} noValidate>
          <label className="field">Email<input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus /></label>
          <label className="field">
            Role
            <select className="select" value={role} onChange={(e) => setRole(e.target.value as Role)}>
              <option value="Agent">Agent: works on tickets</option>
              {(myRole === "Owner" || myRole === "Admin") && <option value="Admin">Admin: also manages the team and settings</option>}
            </select>
          </label>
          {error && (
            <p className="form-error" role="alert">
              {error.message}
              {error.code === "plan_limit_reached" && <> <a href="/plan" onClick={(e) => { e.preventDefault(); onClose(); navigate("/plan"); }}>See plans</a></>}
            </p>
          )}
          <div className="dialog-actions">
            <button type="button" className="button" onClick={onClose}>Cancel</button>
            <button className="button primary" disabled={busy}>{busy && <Loader2 size={16} className="spin" />}Create invitation</button>
          </div>
        </form>
      )}
    </Dialog>
  );
}

export function Team() {
  const { session, setSession, signOut } = useSession();
  const me = session.organization!;
  const admin = canManage(me.role);
  const [members, reloadMembers, setMembers] = useLoad(() => api.members(), []);
  const [invitations, reloadInvitations, setInvitations] = useLoad<Invitation[]>(() => (admin ? api.invitations() : Promise.resolve([])), [admin]);
  const [usage, reloadUsage] = useLoad(() => api.usage(), []);
  const [inviting, setInviting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [transferTo, setTransferTo] = useState<Member | null>(null);

  async function act(action: () => Promise<void>) {
    setError(null);
    try {
      await action();
      reloadUsage();
    } catch (e) {
      setError((e as Error).message);
    }
  }

  const changeRole = (m: Member, role: Role) => act(async () => {
    const updated = await api.changeRole(m.id, role);
    setMembers((members.data ?? []).map((x) => (x.id === m.id ? updated : x)));
  });

  const remove = (m: Member) => act(async () => {
    await api.removeMember(m.id);
    if (m.id === session.user.id) {
      // Left the organization: start over in another one, or on the empty page.
      const others = session.organizations.filter((o) => o.id !== me.id);
      if (others.length > 0) setSession(await api.switchTo(others[0].id));
      else signOut();
      navigate("/");
      return;
    }
    setMembers((members.data ?? []).filter((x) => x.id !== m.id));
  });

  const revoke = (i: Invitation) => act(async () => {
    await api.revoke(i.id);
    setInvitations((invitations.data ?? []).filter((x) => x.id !== i.id));
  });

  async function transfer() {
    if (!transferTo) return;
    await act(async () => {
      await api.transfer(transferTo.id);
      setSession(await api.switchTo(me.id));
    });
    setTransferTo(null);
  }

  const canEdit = (m: Member) => m.role !== "Owner" && m.id !== session.user.id && (me.role === "Owner" || (me.role === "Admin" && m.role === "Agent"));
  const seats = usage.data ? `${usage.data.seats} of ${usage.data.plan.seats} seats used` : " ";

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Team</h1>
          <p className="muted small">{seats}</p>
        </div>
        {admin && <button className="button primary" onClick={() => setInviting(true)}><UserPlus size={16} />Invite</button>}
      </div>
      {error && <p className="form-error" role="alert" style={{ marginBottom: 12 }}>{error}</p>}

      <div className="card">
        {members.state === "failed" && !members.data ? <Failed message={members.error} onRetry={reloadMembers} /> : (
          <table className="table">
            <thead><tr><th>Name</th><th className="hide-sm">Email</th><th>Role</th><th className="hide-sm">Joined</th><th /></tr></thead>
            {!members.data ? <SkeletonRows columns={5} rows={3} /> : (
              <tbody>
                {members.data.map((m) => (
                  <tr key={m.id}>
                    <td>
                      <div className="row">
                        <span className="avatar">{initials(m.displayName)}</span>
                        <span>{m.displayName}{m.id === session.user.id && <span className="muted small"> (you)</span>}</span>
                      </div>
                    </td>
                    <td className="hide-sm muted">{m.email}</td>
                    <td>
                      {canEdit(m) ? (
                        <select className="select" style={{ width: 120, height: 32, fontSize: 14 }} value={m.role} aria-label={`Role of ${m.displayName}`}
                          onChange={(e) => changeRole(m, e.target.value as Role)} disabled={me.role !== "Owner" && m.role === "Admin"}>
                          <option value="Agent">Agent</option>
                          <option value="Admin" disabled={me.role !== "Owner"}>Admin</option>
                        </select>
                      ) : (
                        <span className={`badge ${m.role === "Owner" ? "accent" : "plain"}`}>{m.role === "Owner" && <Crown size={12} />}{m.role}</span>
                      )}
                    </td>
                    <td className="hide-sm muted small">{relative(m.joinedAt)}</td>
                    <td style={{ textAlign: "right", whiteSpace: "nowrap" }}>
                      {me.role === "Owner" && m.id !== session.user.id && <button className="button small" onClick={() => setTransferTo(m)}>Make owner</button>}{" "}
                      {canEdit(m) && <button className="button small danger" onClick={() => remove(m)}>Remove</button>}
                      {m.id === session.user.id && m.role !== "Owner" && <button className="button small danger" onClick={() => remove(m)}>Leave</button>}
                    </td>
                  </tr>
                ))}
              </tbody>
            )}
          </table>
        )}
      </div>

      {admin && (
        <section className="card section" style={{ marginTop: 16 }} aria-label="Open invitations">
          <h2>Open invitations</h2>
          {invitations.state === "failed" && !invitations.data && <Failed message={invitations.error} onRetry={reloadInvitations} />}
          {invitations.data?.length === 0 && <Empty title="No open invitations" icon={<Mail size={22} />}><span className="small">Invitations take a seat until they are accepted.</span></Empty>}
          {invitations.data?.map((i) => (
            <div key={i.id} className="row">
              <Mail size={16} className="muted" />
              <span className="grow">{i.email} <span className="muted small">· {i.role} · {i.expired ? "expired" : `expires ${relative(i.expiresAt).replace(" ago", "")}`.replace("expires just now", "expires soon")}</span></span>
              <button className="button small" onClick={() => revoke(i)}><X size={14} />Revoke</button>
            </div>
          ))}
        </section>
      )}

      {inviting && (
        <InviteDialog
          myRole={me.role}
          onClose={() => setInviting(false)}
          onInvited={(i) => { setInvitations([i, ...(invitations.data ?? []).filter((x) => x.email !== i.email)]); reloadUsage(); }}
        />
      )}
      {transferTo && (
        <Dialog title="Hand over the organization" onClose={() => setTransferTo(null)}>
          <p>{transferTo.displayName} becomes the owner of {me.name}: the plan and the team are theirs to manage. You stay as an admin.</p>
          <div className="dialog-actions">
            <button className="button" onClick={() => setTransferTo(null)}>Cancel</button>
            <button className="button primary" onClick={transfer}>Make {transferTo.displayName} owner</button>
          </div>
        </Dialog>
      )}
    </>
  );
}
