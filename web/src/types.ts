export type Role = "Agent" | "Admin" | "Owner";
export type PlanId = "Free" | "Team" | "Business";
export type TicketStatus = "Open" | "Pending" | "Solved" | "Closed";
export type TicketPriority = "Low" | "Normal" | "High" | "Urgent";
export type TicketChannel = "Web" | "Agent";

export interface User { id: string; email: string; displayName: string }
export interface OrganizationSummary { id: string; name: string; slug: string; plan: PlanId; role: Role }
export interface Session {
  accessToken: string;
  expiresAt: string;
  user: User;
  organization: OrganizationSummary | null;
  organizations: OrganizationSummary[];
}
export interface Organization { id: string; name: string; slug: string; plan: PlanId; createdAt: string }

export interface PersonRef { id: string; name: string }
export interface Ticket {
  number: number;
  subject: string;
  status: TicketStatus;
  priority: TicketPriority;
  channel: TicketChannel;
  requesterName: string;
  requesterEmail: string;
  assignee: PersonRef | null;
  createdAt: string;
  updatedAt: string;
}
export interface TicketPage { items: Ticket[]; total: number; page: number; pageSize: number }
export interface Comment { id: string; authorName: string; byTeam: boolean; isInternal: boolean; body: string; createdAt: string }
export interface Attachment { id: string; fileName: string; contentType: string; sizeBytes: number; createdAt: string }

export interface Member { id: string; displayName: string; email: string; role: Role; joinedAt: string }
export interface Invitation { id: string; email: string; role: Role; createdAt: string; expiresAt: string; expired: boolean }
export interface InvitationCreated { invitation: Invitation; token: string }
export interface InvitationPreview { organizationName: string; email: string; role: Role; expired: boolean; hasAccount: boolean }

export interface Plan {
  id: PlanId;
  name: string;
  seats: number;
  ticketsPerMonth: number | null;
  storageBytes: number;
  requestsPerMinute: number;
  priceUsd: number;
}
export interface Usage { plan: Plan; seats: number; ticketsThisMonth: number; storageBytes: number }

export interface AuditEvent { id: string; actorName: string; action: string; target: string; details: string | null; at: string }
export interface AuditPage { items: AuditEvent[]; next: string | null }

export interface PublicOrganization { name: string; slug: string }
export interface PublicTicketCreated { number: number; accessKey: string }
export interface PublicTicket { number: number; subject: string; status: TicketStatus; createdAt: string; messages: Comment[] }
