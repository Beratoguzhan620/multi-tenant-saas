import type {
  Attachment, AuditPage, Comment, Invitation, InvitationCreated, InvitationPreview, Member, Organization, Plan, PlanId,
  PublicOrganization, PublicTicket, PublicTicketCreated, Role, Session, Ticket, TicketPage, TicketPriority, TicketStatus, Usage,
} from "./types";

/** An API error with the stable code from the problem response, such as "plan_limit_reached". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch {
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }

  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown): RequestInit => ({ method: "POST", body: json(body) });

export interface TicketQuery { status?: TicketStatus; assignee?: string; q?: string; page?: number; pageSize?: number }

export function ticketQueryString(query: TicketQuery): string {
  const params = new URLSearchParams();
  if (query.status) params.set("status", query.status);
  if (query.assignee) params.set("assignee", query.assignee);
  if (query.q?.trim()) params.set("q", query.q.trim());
  if (query.page && query.page > 1) params.set("page", String(query.page));
  if (query.pageSize) params.set("pageSize", String(query.pageSize));
  const text = params.toString();
  return text ? `?${text}` : "";
}

export const api = {
  signUp: (body: { organizationName: string; slug: string; displayName: string; email: string; password: string }) =>
    request<Session>("/api/auth/signup", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  switchTo: (organizationId: string) => request<Session>("/api/auth/switch", post({ organizationId })),
  createOrganization: (name: string, slug: string) => request<Session>("/api/organizations", post({ name, slug })),

  organization: () => request<Organization>("/api/organization"),
  renameOrganization: (name: string) => request<Organization>("/api/organization", { method: "PATCH", body: json({ name }) }),

  tickets: (query: TicketQuery) => request<TicketPage>(`/api/tickets${ticketQueryString(query)}`),
  ticket: (number: number) => request<Ticket>(`/api/tickets/${number}`),
  openTicket: (body: { subject: string; requesterName: string; requesterEmail: string; message: string; priority: TicketPriority; assigneeId?: string }) =>
    request<Ticket>("/api/tickets", post(body)),
  updateTicket: (number: number, body: { subject?: string; status?: TicketStatus; priority?: TicketPriority; assigneeId?: string; unassign?: boolean }) =>
    request<Ticket>(`/api/tickets/${number}`, { method: "PATCH", body: json(body) }),
  comments: (number: number) => request<Comment[]>(`/api/tickets/${number}/comments`),
  addComment: (number: number, body: string, internal: boolean) =>
    request<Comment>(`/api/tickets/${number}/comments`, post({ body, internal })),

  attachments: (number: number) => request<Attachment[]>(`/api/tickets/${number}/attachments`),
  upload: (number: number, file: File) => {
    const form = new FormData();
    form.append("file", file);
    return request<Attachment>(`/api/tickets/${number}/attachments`, { method: "POST", body: form });
  },
  download: async (attachment: Attachment) => {
    const response = await send(`/api/attachments/${attachment.id}`);
    return response.blob();
  },
  deleteAttachment: (id: string) => request<void>(`/api/attachments/${id}`, { method: "DELETE" }),

  members: () => request<Member[]>("/api/members"),
  changeRole: (userId: string, role: Role) => request<Member>(`/api/members/${userId}`, { method: "PATCH", body: json({ role }) }),
  removeMember: (userId: string) => request<void>(`/api/members/${userId}`, { method: "DELETE" }),
  transfer: (userId: string) => request<Member[]>("/api/organization/transfer", post({ userId })),

  invitations: () => request<Invitation[]>("/api/invitations"),
  invite: (email: string, role: Role) => request<InvitationCreated>("/api/invitations", post({ email, role })),
  revoke: (id: string) => request<void>(`/api/invitations/${id}`, { method: "DELETE" }),
  previewInvitation: (token: string) => request<InvitationPreview>("/api/invitations/preview", post({ token })),
  acceptInvitation: (token: string) => request<Session>("/api/invitations/accept", post({ token })),
  join: (token: string, displayName: string, password: string) => request<Session>("/api/auth/join", post({ token, displayName, password })),

  plans: () => request<Plan[]>("/api/plans"),
  usage: () => request<Usage>("/api/organization/usage"),
  changePlan: (plan: PlanId) => request<Usage>("/api/organization/plan", { method: "PUT", body: json({ plan }) }),

  audit: (before?: string | null) => request<AuditPage>(`/api/audit?limit=30${before ? `&before=${before}` : ""}`),

  publicOrganization: (slug: string) => request<PublicOrganization>(`/api/public/${encodeURIComponent(slug)}`),
  publicOpen: (slug: string, body: { name: string; email: string; subject: string; message: string }) =>
    request<PublicTicketCreated>(`/api/public/${encodeURIComponent(slug)}/tickets`, post(body)),
  publicTicket: (slug: string, number: number, key: string) =>
    request<PublicTicket>(`/api/public/${encodeURIComponent(slug)}/tickets/${number}`, { headers: { "X-Ticket-Key": key } }),
  publicReply: (slug: string, number: number, key: string, message: string) =>
    request<PublicTicket>(`/api/public/${encodeURIComponent(slug)}/tickets/${number}/replies`, {
      method: "POST",
      body: json({ message }),
      headers: { "X-Ticket-Key": key },
    }),
};
