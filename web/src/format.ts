import type { Role, TicketPriority, TicketStatus } from "./types";

export function bytes(value: number): string {
  if (value < 1024) return `${value} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let size = value / 1024;
  let unit = 0;
  while (size >= 1024 && unit < units.length - 1) {
    size /= 1024;
    unit++;
  }
  return `${size >= 10 || Number.isInteger(size) ? Math.round(size) : size.toFixed(1)} ${units[unit]}`;
}

/** "just now", "5 min ago", "3 h ago", "yesterday", then a date. */
export function relative(iso: string, now: Date = new Date()): string {
  const then = new Date(iso);
  const seconds = Math.round((now.getTime() - then.getTime()) / 1000);
  if (seconds < 45) return "just now";
  const minutes = Math.round(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.floor(hours / 24);
  if (days === 1) return "yesterday";
  if (days < 7) return `${days} days ago`;
  return then.toLocaleDateString("en-GB", { day: "numeric", month: "short", year: then.getFullYear() === now.getFullYear() ? undefined : "numeric" });
}

export function dateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
}

export const statuses: TicketStatus[] = ["Open", "Pending", "Solved", "Closed"];
export const priorities: TicketPriority[] = ["Low", "Normal", "High", "Urgent"];

export const statusHint: Record<TicketStatus, string> = {
  Open: "Waiting for the team",
  Pending: "Waiting for the customer",
  Solved: "Solved, can still be reopened",
  Closed: "Closed for good",
};

export function canManage(role: Role | undefined): boolean {
  return role === "Admin" || role === "Owner";
}

/** A slug suggestion from an organization name: "Kargo Hızlı A.Ş." → "kargo-hizli-a-s". */
export function slugify(name: string): string {
  const map: Record<string, string> = { ı: "i", İ: "i", ş: "s", Ş: "s", ğ: "g", Ğ: "g", ü: "u", Ü: "u", ö: "o", Ö: "o", ç: "c", Ç: "c" };
  return name
    .replace(/[ıİşŞğĞüÜöÖçÇ]/g, (c) => map[c])
    .normalize("NFKD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-+|-+$/g, "")
    .slice(0, 40)
    .replace(/-+$/g, "");
}

/** Plain-language audit lines: "member.role_changed" → "changed a role". */
export const auditVerbs: Record<string, string> = {
  "organization.renamed": "renamed the organization",
  "plan.changed": "changed the plan",
  "member.invited": "invited",
  "invitation.revoked": "revoked the invitation for",
  "member.joined": "joined",
  "member.role_changed": "changed the role of",
  "member.removed": "removed",
  "ownership.transferred": "handed the organization to",
  "ticket.assigned": "assigned",
  "ticket.status_changed": "changed the status of",
  "attachment.deleted": "deleted a file on",
};

export function percent(used: number, limit: number | null): number {
  if (!limit) return 0;
  return Math.min(100, Math.round((used / limit) * 100));
}
