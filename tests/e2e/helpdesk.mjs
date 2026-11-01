// A support team, its customer and a second organization, in separate browsers, against the Compose stack.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d && cd tests/e2e && npm install && npx playwright install chromium && node helpdesk.mjs
import { chromium } from "playwright";
import { mkdirSync, writeFileSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const password = "e2e-test-password";
const owner = { org: `Kargo Hızlı ${run}`, slug: `kargo-${run}`, name: "Ayşe Yılmaz", email: `ayse.${run}@example.com` };
const other = { org: `Market ${run}`, slug: `market-${run}`, name: "Deniz Arslan", email: `deniz.${run}@example.com` };
const teammate = { name: "Can Öztürk", email: `can.${run}@example.com` };

const browser = await chromium.launch();
const errors = [];
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n")[0]}`);
  }
}

async function open(viewport = { width: 1280, height: 800 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB", permissions: ["clipboard-read", "clipboard-write"] });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    // A refused request (404, 402) is logged by the browser; that is the app working, not an error in it.
    if (message.type() === "error" && !/Failed to load resource/.test(message.text())) errors.push(message.text());
  });
  return page;
}

async function signUp(page, person) {
  await page.goto(`${BASE}/signup`);
  await page.getByLabel("Organization name").fill(person.org);
  await page.getByLabel("Address").fill(person.slug);
  await page.getByLabel("Your name").fill(person.name);
  await page.getByLabel("Email").fill(person.email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Create organization" }).click();
  await page.getByRole("heading", { name: "Tickets" }).waitFor();
}

const ayse = await open();
const customer = await open({ width: 900, height: 900 });
const deniz = await open();
let customerLink = "";

await step("signed-out visitors land on sign in, and a wrong password is refused", async () => {
  await ayse.goto(BASE);
  await ayse.getByRole("heading", { name: "Sign in" }).waitFor();
  await ayse.getByLabel("Email").fill("nobody@example.com");
  await ayse.getByLabel("Password").fill("wrong-password-1");
  await ayse.getByRole("button", { name: "Sign in" }).click();
  await ayse.getByRole("alert").filter({ hasText: "incorrect" }).waitFor();
});

await step("Ayşe signs up her organization and sees an empty inbox", async () => {
  await signUp(ayse, owner);
  await ayse.getByText("No tickets yet").waitFor();
  await ayse.getByRole("button", { name: `Organization: ${owner.org}` }).waitFor();
});

await step("a customer opens a request on the public form and gets a private link", async () => {
  await customer.goto(`${BASE}/support/${owner.slug}`);
  await customer.getByRole("heading", { name: "How can we help?" }).waitFor();
  await customer.getByLabel("Your name").fill("Mehmet Kaya");
  await customer.getByLabel("Email").fill("mehmet@example.com");
  await customer.getByLabel("Subject").fill("Kargom üç gündür yolda");
  await customer.getByLabel("Message").fill("Sipariş 48213 hâlâ dağıtıma çıkmadı. Ne zaman gelir?");
  await customer.getByRole("button", { name: "Send request" }).click();
  await customer.getByRole("heading", { name: "Request #1001 received" }).waitFor();
  customerLink = await customer.locator(".key-box code").innerText();
  if (!customerLink.includes("#")) throw new Error("the link carries no key");
  await customer.getByRole("link", { name: "Open my request" }).click();
  await customer.getByRole("heading", { name: "Kargom üç gündür yolda" }).waitFor();
});

await step("the ticket reaches Ayşe's inbox and she answers with a note and a reply", async () => {
  await ayse.reload();
  await ayse.getByRole("row").filter({ hasText: "Kargom üç gündür yolda" }).click();
  await ayse.getByRole("heading", { name: "Kargom üç gündür yolda" }).waitFor();
  await ayse.getByRole("tab", { name: "Internal note" }).click();
  await ayse.getByLabel("Internal note").fill("Kargo firması aktarma merkezinde bekliyor dedi.");
  await ayse.getByRole("button", { name: "Add note" }).click();
  await ayse.locator(".message.note").waitFor();
  await ayse.getByRole("tab", { name: "Reply to customer" }).click();
  await ayse.getByLabel("Reply").fill("Merhaba Mehmet Bey, paketiniz yarın dağıtıma çıkıyor.");
  await ayse.getByRole("button", { name: "Send reply" }).click();
  await ayse.locator(".page-head .badge", { hasText: "Pending" }).waitFor();
});

await step("the customer sees the reply but never the internal note, and answers", async () => {
  await customer.reload();
  await customer.getByText("paketiniz yarın dağıtıma çıkıyor").waitFor();
  if (await customer.getByText("aktarma merkezinde").count()) throw new Error("the internal note leaked to the customer");
  await customer.getByLabel("Reply").fill("Teşekkürler, bekliyorum.");
  await customer.getByRole("button", { name: "Send reply" }).click();
  await customer.getByText("Teşekkürler, bekliyorum.").waitFor();
  await customer.locator(".badge", { hasText: "Open" }).waitFor();
});

await step("Ayşe attaches a file to the ticket", async () => {
  await ayse.reload();
  await ayse.getByLabel("Choose a file").setInputFiles({ name: "kargo-takip.txt", mimeType: "text/plain", buffer: Buffer.from("Takip no: 48213") });
  await ayse.getByRole("button", { name: "kargo-takip.txt" }).first().waitFor();
});

let inviteLink = "";
await step("Ayşe invites Can, who joins with a new account and sees the same ticket", async () => {
  await ayse.getByRole("link", { name: "Team" }).click();
  await ayse.getByRole("button", { name: "Invite" }).click();
  await ayse.locator("dialog").getByLabel("Email").fill(teammate.email);
  await ayse.getByRole("button", { name: "Create invitation" }).click();
  inviteLink = await ayse.locator("dialog .key-box code").innerText();
  await ayse.getByRole("button", { name: "Done" }).click();
  await ayse.getByText(teammate.email).waitFor();

  const can = await open();
  await can.goto(inviteLink);
  await can.getByRole("heading", { name: `Join ${owner.org}` }).waitFor();
  await can.getByLabel("Your name").fill(teammate.name);
  await can.getByLabel("Password").fill(password);
  await can.getByRole("button", { name: "Create account and join" }).click();
  await can.getByRole("row").filter({ hasText: "Kargom üç gündür yolda" }).waitFor();
  if (await can.getByRole("link", { name: "Audit log" }).count()) throw new Error("an agent sees the audit log");
  await can.context().close();
});

await step("another organization sees none of it, even by ticket number", async () => {
  await signUp(deniz, other);
  await deniz.getByText("No tickets yet").waitFor();
  await deniz.goto(`${BASE}/tickets/1001`);
  await deniz.getByText("There is no ticket #1001.").waitFor();
  await deniz.goto(`${BASE}/team`);
  await deniz.getByRole("cell", { name: other.name, exact: false }).first().waitFor();
  if (await deniz.getByText(owner.name).count()) throw new Error("another organization's member is visible");
});

await step("Ayşe upgrades to Team and the plan shows it", async () => {
  await ayse.getByRole("link", { name: "Plan & usage" }).click();
  await ayse.getByText("2 / 3").waitFor();
  await ayse.getByRole("button", { name: "Upgrade to Team" }).click();
  await ayse.getByText("You are now on the Team plan.").waitFor();
  await ayse.getByRole("button", { name: `Organization: ${owner.org}` }).filter({ hasText: "Team plan" }).waitFor();
});

await step("the audit log tells who did what", async () => {
  await ayse.getByRole("link", { name: "Audit log" }).click();
  await ayse.getByText("Free → Team").waitFor();
  await ayse.locator(".audit-row", { hasText: "joined" }).filter({ hasText: teammate.email }).waitFor();
  await ayse.locator(".audit-row", { hasText: "invited" }).filter({ hasText: teammate.email }).waitFor();
});

await step("a second organization starts empty, and switching back shows the first one's tickets", async () => {
  await ayse.getByRole("button", { name: `Organization: ${owner.org}` }).click();
  await ayse.getByRole("menuitem", { name: "New organization" }).click();
  await ayse.locator("dialog").getByLabel("Organization name").fill(`Kargo Yurtdışı ${run}`);
  await ayse.locator("dialog").getByRole("button", { name: "Create" }).click();
  await ayse.getByText("No tickets yet").waitFor();
  await ayse.getByRole("button", { name: `Organization: Kargo Yurtdışı ${run}` }).click();
  await ayse.getByRole("menuitem", { name: owner.org }).click();
  await ayse.getByRole("row").filter({ hasText: "Kargom üç gündür yolda" }).waitFor();
});

await step("the panel works on a phone-sized screen", async () => {
  await ayse.setViewportSize({ width: 390, height: 844 });
  await ayse.getByRole("row").filter({ hasText: "Kargom üç gündür yolda" }).click();
  await ayse.getByRole("heading", { name: "Kargom üç gündür yolda" }).waitFor();
  const overflow = await ayse.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
  if (overflow > 1) throw new Error(`the page scrolls sideways by ${overflow}px`);
  await ayse.setViewportSize({ width: 1280, height: 800 });
});

await step("no errors in the browser console", async () => {
  if (errors.length) throw new Error(errors.join(" | "));
});

await ayse.screenshot({ path: `${OUT}last.png` });
writeFileSync(`${OUT}summary.txt`, `${failures} failed\n`);
await browser.close();
if (failures) {
  console.log(`\n${failures} step(s) failed`);
  process.exit(1);
}
console.log("\nall steps passed");
