import { ArrowUp, ChevronsUp, Minus, ArrowDown } from "lucide-react";
import { statusHint } from "../format";
import type { TicketPriority, TicketStatus } from "../types";

export function StatusBadge({ status }: { status: TicketStatus }) {
  return (
    <span className={`badge status-${status}`} title={statusHint[status]}>
      <span className="dot" />
      {status}
    </span>
  );
}

const priorityIcons = { Low: ArrowDown, Normal: Minus, High: ArrowUp, Urgent: ChevronsUp };

export function PriorityLabel({ priority }: { priority: TicketPriority }) {
  const Icon = priorityIcons[priority];
  return (
    <span className={`priority priority-${priority}`}>
      <Icon size={14} />
      {priority}
    </span>
  );
}

export function initials(name: string): string {
  return name.split(/\s+/).filter(Boolean).slice(0, 2).map((part) => part[0]!.toLocaleUpperCase("tr")).join("");
}
