import { useEffect, useState } from "react";

/** The pages of the app. Secrets (invitation tokens, customer keys) travel in the fragment, which never reaches a server log. */
export type Route =
  | { page: "signin" }
  | { page: "signup" }
  | { page: "join"; token: string }
  | { page: "support"; slug: string }
  | { page: "support-ticket"; slug: string; number: number; key: string }
  | { page: "tickets" }
  | { page: "ticket"; number: number }
  | { page: "team" }
  | { page: "plan" }
  | { page: "audit" }
  | { page: "settings" }
  | { page: "not-found" };

export function parseRoute(pathname: string, hash: string): Route {
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const fragment = hash.replace(/^#/, "");
  const [first, second, third, fourth] = parts;

  if (parts.length === 0 || (first === "tickets" && parts.length === 1)) return { page: "tickets" };
  if (first === "tickets" && parts.length === 2 && /^\d+$/.test(second)) return { page: "ticket", number: Number(second) };
  if (parts.length === 1 && first === "signin") return { page: "signin" };
  if (parts.length === 1 && first === "signup") return { page: "signup" };
  if (parts.length === 1 && first === "join" && fragment) return { page: "join", token: fragment };
  if (parts.length === 1 && (first === "team" || first === "plan" || first === "audit" || first === "settings")) return { page: first };
  if (first === "support" && parts.length === 2) return { page: "support", slug: second };
  if (first === "support" && third === "tickets" && parts.length === 4 && /^\d+$/.test(fourth) && fragment) {
    return { page: "support-ticket", slug: second, number: Number(fourth), key: fragment };
  }
  return { page: "not-found" };
}

/** Pages a visitor without an account can open. */
export const isPublic = (route: Route) =>
  route.page === "signin" || route.page === "signup" || route.page === "join" || route.page === "support" || route.page === "support-ticket";

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo(0, 0);
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.hash);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    window.addEventListener("hashchange", update);
    return () => {
      window.removeEventListener("popstate", update);
      window.removeEventListener("hashchange", update);
    };
  }, []);
  return route;
}
