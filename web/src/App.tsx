import { useCallback, useEffect, useMemo, useState } from "react";
import { setToken } from "./api";
import { SessionCtx } from "./context";
import { isPublic, navigate, useRoute } from "./router";
import { loadSession, saveSession } from "./session";
import type { Session } from "./types";
import { Shell } from "./components/Shell";
import { SignIn } from "./pages/SignIn";
import { Join } from "./pages/Join";
import { SupportForm, SupportTicket } from "./pages/Support";
import { Tickets } from "./pages/Tickets";
import { TicketDetail } from "./pages/TicketDetail";
import { Team } from "./pages/Team";
import { PlanPage } from "./pages/Plan";
import { Audit } from "./pages/Audit";
import { Settings } from "./pages/Settings";
import { NoOrganization } from "./pages/NoOrganization";
import { Empty } from "./components/States";

export function App() {
  const route = useRoute();
  const [session, setSessionState] = useState<Session | null>(loadSession);

  const signOut = useCallback(() => {
    saveSession(null);
    setSessionState(null);
    navigate("/signin", true);
  }, []);

  const setSession = useCallback((next: Session) => {
    saveSession(next);
    setSessionState(next);
  }, []);

  // Set synchronously so the first requests of a page already carry the token.
  setToken(session?.accessToken ?? null, signOut);

  useEffect(() => {
    if (!session && !isPublic(route)) navigate("/signin", true);
    if (session?.organization && (route.page === "signin" || route.page === "signup")) navigate("/", true);
  }, [session, route]);

  const context = useMemo(() => (session ? { session, setSession, signOut } : null), [session, setSession, signOut]);

  switch (route.page) {
    case "support":
      return <SupportForm slug={route.slug} />;
    case "support-ticket":
      return <SupportTicket slug={route.slug} number={route.number} accessKey={route.key} />;
    case "join":
      return <Join token={route.token} session={session} onSession={setSession} />;
    case "signin":
    case "signup":
      return <SignIn mode={route.page} onSession={setSession} />;
  }

  if (!context) return null;
  if (!context.session.organization) {
    return (
      <SessionCtx.Provider value={context}>
        <NoOrganization />
      </SessionCtx.Provider>
    );
  }

  // Keyed by organization: switching starts every page fresh, so nothing from the previous one is shown.
  const orgKey = context.session.organization.id;
  return (
    <SessionCtx.Provider value={context}>
      <Shell route={route}>
        <div key={orgKey}>
          {route.page === "tickets" && <Tickets />}
          {route.page === "ticket" && <TicketDetail number={route.number} />}
          {route.page === "team" && <Team />}
          {route.page === "plan" && <PlanPage />}
          {route.page === "audit" && <Audit />}
          {route.page === "settings" && <Settings />}
          {route.page === "not-found" && <Empty title="This page does not exist"><a href="/" onClick={(e) => { e.preventDefault(); navigate("/"); }}>Go to tickets</a></Empty>}
        </div>
      </Shell>
    </SessionCtx.Provider>
  );
}
