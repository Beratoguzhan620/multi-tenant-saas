// Many organizations at once against a running stack: every ticket must land in its own organization, with
// its own numbers, and no organization may read another's. Prints latencies and throughput.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 PUBLIC_REQUESTS_PER_MINUTE=100000 docker compose up --build -d
//        node tests/load/tenants.mjs [organizations=50] [ticketsEach=40] [concurrency=32]
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const [orgCount = 50, ticketsEach = 40, concurrency = 32] = process.argv.slice(2).map(Number);
const run = Date.now().toString(36);

async function call(path, { token, body, method = body ? "POST" : "GET", headers = {} } = {}) {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  const ms = performance.now() - started;
  const data = response.status === 204 ? null : await response.json().catch(() => null);
  return { status: response.status, data, ms };
}

/** Runs the tasks with at most `limit` in flight. */
async function pool(tasks, limit) {
  const results = new Array(tasks.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, tasks.length) }, async () => {
    while (next < tasks.length) {
      const i = next++;
      results[i] = await tasks[i]();
    }
  }));
  return results;
}

const pct = (values, p) => {
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
};
const summary = (values) => `p50 ${pct(values, 50).toFixed(0)} ms · p95 ${pct(values, 95).toFixed(0)} ms · p99 ${pct(values, 99).toFixed(0)} ms`;
const problems = [];
const fail = (message) => { problems.push(message); };

// 1. Organizations, each with an owner and a Business plan so the monthly ticket limit stays out of the way.
console.log(`${orgCount} organizations × ${ticketsEach} tickets, ${concurrency} requests in flight`);
const orgs = await pool(Array.from({ length: orgCount }, (_, i) => async () => {
  const slug = `load-${run}-${i}`;
  const signup = await call("/api/auth/signup", {
    body: { organizationName: `Load ${i}`, slug, displayName: `Owner ${i}`, email: `${slug}@example.com`, password: "load-test-password" },
  });
  if (signup.status !== 201) throw new Error(`signup ${signup.status}: ${JSON.stringify(signup.data)}`);
  const token = signup.data.accessToken;
  const plan = await call("/api/organization/plan", { token, body: { plan: "Business" }, method: "PUT" });
  if (plan.status !== 200) throw new Error(`plan ${plan.status}`);
  return { i, slug, token, marker: `[org ${i}]` };
}), concurrency);

// 2. Tickets from all organizations interleaved: half by the team, half through the public form.
const creates = [];
for (let n = 0; n < ticketsEach; n++) {
  for (const org of orgs) {
    creates.push(async () => {
      const subject = `${org.marker} request ${n}`;
      const result = n % 2 === 0
        ? await call("/api/tickets", { token: org.token, body: { subject, requesterName: "Customer", requesterEmail: "c@example.com", message: "Hello" } })
        : await call(`/api/public/${org.slug}/tickets`, { body: { name: "Customer", email: "c@example.com", subject, message: "Hello" } });
      if (result.status !== 201) fail(`create in ${org.slug}: ${result.status}`);
      return result.ms;
    });
  }
}
const startedAt = performance.now();
const createTimes = await pool(creates, concurrency);
const seconds = (performance.now() - startedAt) / 1000;

// 3. Every organization sees exactly its own tickets, numbered 1001 to 1000 + ticketsEach without gaps.
const listTimes = [];
await pool(orgs.map((org) => async () => {
  const seen = [];
  for (let page = 1; ; page++) {
    const list = await call(`/api/tickets?pageSize=100&page=${page}`, { token: org.token });
    listTimes.push(list.ms);
    if (list.status !== 200) { fail(`list in ${org.slug}: ${list.status}`); return; }
    seen.push(...list.data.items);
    if (seen.length >= list.data.total || list.data.items.length === 0) break;
  }
  if (seen.length !== ticketsEach) fail(`${org.slug} sees ${seen.length} tickets, expected ${ticketsEach}`);
  const foreign = seen.filter((t) => !t.subject.startsWith(org.marker));
  if (foreign.length) fail(`${org.slug} sees ${foreign.length} tickets of other organizations`);
  const numbers = seen.map((t) => t.number).sort((a, b) => a - b);
  const expected = Array.from({ length: ticketsEach }, (_, k) => 1001 + k);
  if (JSON.stringify(numbers) !== JSON.stringify(expected)) fail(`${org.slug} numbers are not 1001..${1000 + ticketsEach}`);
}), concurrency);

// 4. The same number in every organization is that organization's own ticket; another's search finds nothing.
const readTimes = [];
await pool(orgs.flatMap((org) => [1001, 1000 + ticketsEach].map((number) => async () => {
  const read = await call(`/api/tickets/${number}`, { token: org.token });
  readTimes.push(read.ms);
  if (read.status !== 200 || !read.data.subject.startsWith(org.marker)) fail(`${org.slug} #${number} is not its own`);
  const other = orgs[(org.i + 1) % orgs.length];
  const search = await call(`/api/tickets?q=${encodeURIComponent(other.marker)}`, { token: org.token });
  if (search.status !== 200 || search.data.total !== 0) fail(`${org.slug} finds tickets of ${other.slug}`);
})), concurrency);

const total = orgCount * ticketsEach;
console.log(`created ${total} tickets in ${seconds.toFixed(1)} s: ${(total / seconds).toFixed(0)} tickets/s`);
console.log(`create  ${summary(createTimes)}`);
console.log(`list    ${summary(listTimes)}`);
console.log(`read    ${summary(readTimes)}`);
if (problems.length) {
  console.log(`\n${problems.length} problem(s):\n  ${problems.slice(0, 20).join("\n  ")}`);
  process.exit(1);
}
console.log(`\nisolation held: ${orgCount} organizations, each saw only its own ${ticketsEach} tickets, numbered 1001 to ${1000 + ticketsEach}`);
