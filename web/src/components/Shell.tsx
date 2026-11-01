import { Building2, Check, ChevronsUpDown, CreditCard, History, Inbox, LogOut, Plus, Settings, Users } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { api } from "../api";
import { useSession } from "../context";
import { canManage } from "../format";
import { navigate, type Route } from "../router";
import { initials } from "./Badges";
import { NewOrganizationDialog } from "../pages/NoOrganization";

function NavLink({ href, active, icon, children }: { href: string; active: boolean; icon: ReactNode; children: ReactNode }) {
  return (
    <a
      href={href}
      className={`nav-link${active ? " active" : ""}`}
      aria-current={active ? "page" : undefined}
      onClick={(e) => { e.preventDefault(); navigate(href); }}
    >
      {icon}
      {children}
    </a>
  );
}

function OrganizationSwitcher() {
  const { session, setSession } = useSession();
  const [open, setOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const ref = useRef<HTMLDivElement>(null);
  const current = session.organization!;

  useEffect(() => {
    if (!open) return;
    const close = (e: MouseEvent) => { if (!ref.current?.contains(e.target as Node)) setOpen(false); };
    document.addEventListener("mousedown", close);
    return () => document.removeEventListener("mousedown", close);
  }, [open]);

  async function switchTo(id: string) {
    setOpen(false);
    if (id === current.id) return;
    try {
      setSession(await api.switchTo(id));
      navigate("/");
    } catch (e) {
      setError((e as Error).message);
    }
  }

  return (
    <div className="org-switch" ref={ref}>
      <button className="org-button" onClick={() => setOpen(!open)} aria-expanded={open} aria-haspopup="menu" aria-label={`Organization: ${current.name}`}>
        <span className="org-avatar">{initials(current.name)}</span>
        <span className="grow">
          <span className="org-name" style={{ display: "block" }}>{current.name}</span>
          <span className="small muted">{current.plan} plan · {current.role}</span>
        </span>
        <ChevronsUpDown size={16} className="muted" />
      </button>
      {open && (
        <div className="org-menu" role="menu">
          {session.organizations.map((org) => (
            <button key={org.id} role="menuitem" className={`menu-item${org.id === current.id ? " active" : ""}`} onClick={() => switchTo(org.id)}>
              <span className="org-avatar" style={{ width: 22, height: 22, fontSize: 11 }}>{initials(org.name)}</span>
              <span className="grow ellipsis">{org.name}</span>
              {org.id === current.id && <Check size={15} />}
            </button>
          ))}
          <div className="menu-divider" />
          <button role="menuitem" className="menu-item" onClick={() => { setOpen(false); setCreating(true); }}>
            <Plus size={16} /> New organization
          </button>
        </div>
      )}
      {error && <p className="field-error" role="alert">{error}</p>}
      {creating && <NewOrganizationDialog onClose={() => setCreating(false)} />}
    </div>
  );
}

export function Shell({ route, children }: { route: Route; children: ReactNode }) {
  const { session, signOut } = useSession();
  const admin = canManage(session.organization?.role);
  const page = route.page;

  return (
    <div className="shell">
      <nav className="nav" aria-label="Main">
        <div className="nav-top">
          <div className="brand small"><span className="brand-mark" style={{ width: 24, height: 24 }}><Building2 size={14} /></span>Helpdesk</div>
          <OrganizationSwitcher />
        </div>
        <div className="nav-links">
          <NavLink href="/" active={page === "tickets" || page === "ticket"} icon={<Inbox size={17} />}>Tickets</NavLink>
          <NavLink href="/team" active={page === "team"} icon={<Users size={17} />}>Team</NavLink>
          <NavLink href="/plan" active={page === "plan"} icon={<CreditCard size={17} />}>Plan & usage</NavLink>
          {admin && <NavLink href="/audit" active={page === "audit"} icon={<History size={17} />}>Audit log</NavLink>}
          <NavLink href="/settings" active={page === "settings"} icon={<Settings size={17} />}>Settings</NavLink>
        </div>
        <div className="nav-bottom">
          <span className="avatar">{initials(session.user.displayName)}</span>
          <span className="grow">
            <span className="ellipsis" style={{ display: "block", fontWeight: 500, fontSize: 14 }}>{session.user.displayName}</span>
            <span className="ellipsis small muted" style={{ display: "block" }}>{session.user.email}</span>
          </span>
          <button className="icon-button" onClick={signOut} aria-label="Sign out" title="Sign out"><LogOut size={17} /></button>
        </div>
      </nav>
      <main className="content">{children}</main>
    </div>
  );
}
