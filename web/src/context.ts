import { createContext, useContext } from "react";
import type { Session } from "./types";

export interface SessionContext {
  session: Session;
  /** Replaces the session, for example after switching organization; the page reloads its data. */
  setSession: (session: Session) => void;
  signOut: () => void;
}

export const SessionCtx = createContext<SessionContext | null>(null);

export function useSession(): SessionContext {
  const value = useContext(SessionCtx);
  if (!value) throw new Error("useSession outside of a signed-in page");
  return value;
}
