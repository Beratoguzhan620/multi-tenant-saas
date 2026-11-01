import { Check, Loader2 } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { useSession } from "../context";
import { bytes, percent } from "../format";
import { useLoad } from "../hooks";
import type { Plan, PlanId, Usage } from "../types";
import { Failed, Loading } from "../components/States";

function Meter({ label, used, limit, format = String }: { label: string; used: number; limit: number | null; format?: (n: number) => string }) {
  const share = percent(used, limit);
  return (
    <div className="card meter">
      <span className="small muted">{label}</span>
      <span className="meter-value">{format(used)} <span className="small muted" style={{ fontWeight: 400 }}>/ {limit === null ? "unlimited" : format(limit)}</span></span>
      <div className={`bar${share >= 100 ? " full" : ""}`} role="progressbar" aria-label={label} aria-valuenow={share} aria-valuemin={0} aria-valuemax={100}>
        <span style={{ width: `${limit === null ? 2 : Math.max(share, 2)}%` }} />
      </div>
    </div>
  );
}

export function PlanPage() {
  const { session, setSession } = useSession();
  const owner = session.organization!.role === "Owner";
  const [usage, reload, setUsage] = useLoad(() => api.usage(), []);
  const [plans] = useLoad(() => api.plans(), []);
  const [changing, setChanging] = useState<PlanId | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState<string | null>(null);

  async function change(plan: Plan) {
    setChanging(plan.id);
    setError(null);
    setDone(null);
    try {
      const updated: Usage = await api.changePlan(plan.id);
      setUsage(updated);
      // The plan is part of the organization summary in the session; refresh it.
      setSession(await api.switchTo(session.organization!.id));
      setDone(`You are now on the ${plan.name} plan.`);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setChanging(null);
    }
  }

  if (usage.state === "loading" && !usage.data) return <Loading />;
  if (usage.state === "failed" && !usage.data) return <Failed message={usage.error} onRetry={reload} />;
  const u = usage.data!;
  const order: PlanId[] = ["Free", "Team", "Business"];

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Plan & usage</h1>
          <p className="muted small">{session.organization!.name} is on the <strong>{u.plan.name}</strong> plan. Every organization has its own limits.</p>
        </div>
      </div>

      <div className="meters">
        <Meter label="Seats (members and open invitations)" used={u.seats} limit={u.plan.seats} />
        <Meter label="Tickets this month" used={u.ticketsThisMonth} limit={u.plan.ticketsPerMonth} />
        <Meter label="File storage" used={u.storageBytes} limit={u.plan.storageBytes} format={bytes} />
      </div>

      <h2 style={{ fontSize: 16, fontWeight: 600, margin: "28px 0 12px" }}>Plans</h2>
      {error && <p className="form-error" role="alert" style={{ marginBottom: 12 }}>{error}</p>}
      {done && <p className="form-ok" role="status" style={{ marginBottom: 12 }}>{done}</p>}
      {plans.state === "failed" && <Failed message={plans.error} />}
      <div className="plans">
        {plans.data?.map((plan) => {
          const current = plan.id === u.plan.id;
          const upgrade = order.indexOf(plan.id) > order.indexOf(u.plan.id);
          return (
            <div key={plan.id} className={`card plan${current ? " current" : ""}`} aria-label={`${plan.name} plan`}>
              <div className="row">
                <h3 className="grow" style={{ fontSize: 16, fontWeight: 600 }}>{plan.name}</h3>
                {current && <span className="badge accent">Current</span>}
              </div>
              <div className="plan-price">${plan.priceUsd}<span className="small muted" style={{ fontWeight: 400 }}> / month</span></div>
              <ul>
                <li><Check size={15} />{plan.seats} seats</li>
                <li><Check size={15} />{plan.ticketsPerMonth === null ? "Unlimited tickets" : `${plan.ticketsPerMonth.toLocaleString("en")} tickets a month`}</li>
                <li><Check size={15} />{bytes(plan.storageBytes)} of files</li>
                <li><Check size={15} />{plan.requestsPerMinute.toLocaleString("en")} API requests a minute</li>
              </ul>
              {!current && owner && (
                <button className={`button${upgrade ? " primary" : ""}`} disabled={changing !== null} onClick={() => change(plan)}>
                  {changing === plan.id && <Loader2 size={16} className="spin" />}{upgrade ? `Upgrade to ${plan.name}` : `Move to ${plan.name}`}
                </button>
              )}
            </div>
          );
        })}
      </div>
      {!owner && <p className="small muted" style={{ marginTop: 12 }}>Only the owner changes the plan.</p>}
      <p className="small muted" style={{ marginTop: 12 }}>This is a demo: changing plans charges nothing.</p>
    </>
  );
}
