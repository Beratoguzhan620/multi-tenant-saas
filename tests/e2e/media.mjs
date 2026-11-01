// Screenshots and walkthrough frames for the README, from the real stack with realistic data.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 PUBLIC_REQUESTS_PER_MINUTE=1000 docker compose up --build -d && node media.mjs <output-dir>
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = (process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname).replace(/\/?$/, "/");
mkdirSync(`${OUT}frames`, { recursive: true });
const password = "media-password-1";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function call(path, token, body, method = body ? "POST" : "GET", headers = {}) {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

// The organization and its team.
const slug = "kargo-hizli";
const owner = await call("/api/auth/signup", null, {
  organizationName: "Kargo Hızlı", slug, displayName: "Ayşe Yılmaz", email: "ayse@kargohizli.example", password,
});
const ayse = owner.accessToken;
await call("/api/organization/plan", ayse, { plan: "Team" }, "PUT");
const team = {};
for (const [key, name, role] of [["can", "Can Öztürk", "Agent"], ["elif", "Elif Şahin", "Admin"], ["mert", "Mert Aydın", "Agent"]]) {
  const invite = await call("/api/invitations", ayse, { email: `${key}@kargohizli.example`, role });
  const session = await call("/api/auth/join", null, { token: invite.token, displayName: name, password });
  team[key] = { id: session.user.id, token: session.accessToken };
}
await call("/api/invitations", ayse, { email: "selin@kargohizli.example", role: "Agent" });

// Requests from customers, oldest first, through the public form; then the team works on them.
const customers = [
  ["Fatma Çelik", "fatma.celik@example.com", "Teslimat adresimi değiştirmek istiyorum", "Siparişim yola çıkmadan adresi Kadıköy'deki iş yerime almak mümkün mü?"],
  ["Hakan Koç", "hakan.koc@example.com", "Hasarlı paket teslim edildi", "Kutu ezik geldi, içindeki seramik vazo kırık. Fotoğraflarını ekledim."],
  ["Zeynep Demir", "zeynep.demir@example.com", "Fatura iki kez kesilmiş", "Ekim ayı faturası hesabıma iki kez yansımış. İadesini nasıl alabilirim?"],
  ["Burak Şen", "burak.sen@example.com", "Kurye kapıyı çalmadan gitmiş", "Evdeydim ama 'adreste bulunamadı' notu bırakılmış."],
  ["Selin Aksoy", "selin.aksoy@example.com", "Kurumsal hesap açmak istiyoruz", "Ayda yaklaşık 300 gönderimiz var, toplu fiyat alabilir miyiz?"],
  ["Emre Yıldız", "emre.yildiz@example.com", "Takip numarası çalışmıyor", "KH-48213 numarasıyla sorgulayınca 'kayıt yok' diyor."],
  ["Mehmet Kaya", "mehmet.kaya@example.com", "Kargom üç gündür aktarma merkezinde", "Sipariş 48213 hâlâ dağıtıma çıkmadı. Ne zaman gelir?"],
];
const tickets = [];
for (const [name, email, subject, message] of customers) {
  tickets.push(await call(`/api/public/${slug}/tickets`, null, { name, email, subject, message }));
  await sleep(150);
}
const [adres, hasar, fatura, kurye, kurumsal, takip, aktarma] = tickets;
const reply = (who, t, body, internal = false) => call(`/api/tickets/${t.number}/comments`, who, { body, internal });
const patch = (t, change) => call(`/api/tickets/${t.number}`, ayse, change, "PATCH");
const customerReply = (t, message) => call(`/api/public/${slug}/tickets/${t.number}/replies`, null, { message }, "POST", { "X-Ticket-Key": t.accessKey });

await patch(adres, { assigneeId: team.can.id });
await reply(team.can.token, adres, "Merhaba Fatma Hanım, adres değişikliğini yaptım. Paketiniz yarın iş yerinize teslim edilecek.");
await patch(adres, { status: "Solved" });
await patch(hasar, { priority: "High", assigneeId: team.elif.id });
await reply(team.elif.token, hasar, "Hasar tutanağı için şubeyle görüşüyorum, ilk fotoğraf yeterli.", true);
await reply(team.elif.token, hasar, "Çok üzgünüz Hakan Bey. Hasar kaydını açtık; ürün bedeli 3 iş günü içinde iade edilecek.");
await patch(fatura, { priority: "High", assigneeId: owner.user.id });
await patch(kurye, { assigneeId: team.mert.id });
await reply(team.mert.token, kurye, "Kurye arkadaşla konuştum, yarın 10:00–12:00 arasında tekrar uğrayacak.");
await customerReply(kurye, "Teşekkürler, o saatte evdeyim.");
await patch(kurumsal, { priority: "Low" });
await patch(takip, { status: "Closed", assigneeId: team.can.id });
await patch(aktarma, { priority: "Urgent", assigneeId: owner.user.id });
await reply(ayse, aktarma, "Kargo firması aktarma merkezinde yoğunluk olduğunu söyledi; öncelik verdiler.", true);
await reply(ayse, aktarma, "Merhaba Mehmet Bey, paketiniz bugün dağıtıma çıkıyor. Gecikme için özür dileriz.");
// A second organization of Ayşe's, for the switcher; it shares nothing with the first.
await call("/api/organizations", ayse, { name: "Kargo Hızlı Yurtdışı", slug: "kargo-hizli-yurtdisi" });

// A file on the damaged parcel ticket.
{
  const form = new FormData();
  form.append("file", new Blob(["Hasar tutanağı\nGönderi: KH-51877\nDurum: kutu ezik, ürün kırık\n"], { type: "text/plain" }), "hasar-tutanagi.txt");
  const response = await fetch(`${BASE}/api/tickets/${hasar.number}/attachments`, { method: "POST", headers: { Authorization: `Bearer ${team.elif.token}` }, body: form });
  if (!response.ok) throw new Error(`upload: ${response.status}`);
}

const browser = await chromium.launch();
async function signedIn(email, viewport = { width: 1280, height: 800 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB", deviceScaleFactor: 1 });
  const page = await context.newPage();
  await page.goto(`${BASE}/signin`);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.getByRole("heading", { name: "Tickets" }).waitFor();
  await page.locator("tbody tr.clickable").first().waitFor();
  return page;
}
const shot = async (page, name) => {
  await sleep(300);
  await page.screenshot({ path: `${OUT}${name}.png` });
  console.log(`saved ${name}.png`);
};

{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/signup`);
  await page.getByRole("heading", { name: "Start your help desk" }).waitFor();
  await shot(page, "sign-up");
  await page.goto(`${BASE}/support/${slug}`);
  await page.getByRole("heading", { name: "How can we help?" }).waitFor();
  await page.getByLabel("Your name").fill("Deniz Arslan");
  await page.getByLabel("Email").fill("deniz@example.com");
  await page.getByLabel("Subject").fill("İade kargosu ücretli mi?");
  await page.getByLabel("Message").fill("Aldığım ürünü iade etmek istiyorum; kargo ücretini kim ödüyor?");
  await shot(page, "support-form");
  await page.goto(`${BASE}/support/${slug}/tickets/${aktarma.number}#${aktarma.accessKey}`);
  await page.getByText("paketiniz bugün dağıtıma çıkıyor").waitFor();
  await shot(page, "customer-view");
  await context.close();
}

const page = await signedIn("ayse@kargohizli.example");
await shot(page, "tickets");
await page.getByRole("row").filter({ hasText: "aktarma merkezinde" }).click();
await page.locator(".message.note").waitFor();
await shot(page, "ticket");
await page.goto(`${BASE}/tickets/${hasar.number}`);
await page.getByRole("button", { name: "hasar-tutanagi.txt" }).first().waitFor();
await page.getByRole("link", { name: "Team" }).click();
await page.getByText("selin@kargohizli.example").waitFor();
await shot(page, "team");
await page.getByRole("link", { name: "Plan & usage" }).click();
await page.getByRole("progressbar").first().waitFor();
await shot(page, "plan");
await page.getByRole("link", { name: "Audit log" }).click();
await page.locator(".audit-row").first().waitFor();
await shot(page, "audit");
await page.getByRole("button", { name: "Organization: Kargo Hızlı" }).click();
await shot(page, "organizations");
await page.keyboard.press("Escape");

{
  const phone = await signedIn("can@kargohizli.example", { width: 390, height: 844 });
  await shot(phone, "phone");
  await phone.context().close();
}
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const api = await context.newPage();
  await api.goto(`${BASE}/scalar`);
  await api.waitForLoadState("networkidle");
  await sleep(1500);
  await shot(api, "api-reference");
  await context.close();
}

// Walkthrough frames: the customer on the left writes in, the agent on the right answers.
const customer = await (await browser.newContext({ viewport: { width: 640, height: 720 }, locale: "en-GB" })).newPage();
const agent = await signedIn("can@kargohizli.example", { width: 640, height: 720 });
let frame = 0;
async function frames(count = 1) {
  for (let i = 0; i < count; i++) {
    const n = String(frame++).padStart(3, "0");
    await Promise.all([customer.screenshot({ path: `${OUT}frames/${n}-a.png` }), agent.screenshot({ path: `${OUT}frames/${n}-b.png` })]);
  }
}
await customer.goto(`${BASE}/support/${slug}`);
await customer.getByRole("heading", { name: "How can we help?" }).waitFor();
await frames(3);
await customer.getByLabel("Your name").pressSequentially("Deniz Arslan", { delay: 25 });
await customer.getByLabel("Email").pressSequentially("deniz@example.com", { delay: 20 });
await frames(1);
await customer.getByLabel("Subject").pressSequentially("İade kargosu ücretli mi?", { delay: 30 });
await frames(1);
await customer.getByLabel("Message").pressSequentially("Ürünü iade etmek istiyorum, kargo ücretini kim ödüyor?", { delay: 20 });
await frames(2);
await customer.getByRole("button", { name: "Send request" }).click();
await customer.getByRole("heading", { name: /received/ }).waitFor();
await frames(3);
await customer.getByRole("link", { name: "Open my request" }).click();
await customer.getByRole("heading", { name: "İade kargosu ücretli mi?" }).waitFor();
await agent.reload();
await agent.getByRole("row").filter({ hasText: "İade kargosu" }).waitFor();
await frames(4);
await agent.getByRole("row").filter({ hasText: "İade kargosu" }).click();
await agent.getByRole("heading", { name: "İade kargosu ücretli mi?" }).waitFor();
await frames(3);
await agent.getByLabel("Reply").pressSequentially("Merhaba Deniz Bey, 14 gün içindeki iadelerde kargo bizden.", { delay: 22 });
await frames(2);
await agent.getByRole("button", { name: "Send reply" }).click();
await agent.locator(".page-head .badge", { hasText: "Pending" }).waitFor();
await frames(3);
await customer.reload();
await customer.getByText("kargo bizden").waitFor();
await sleep(300);
await frames(8);
console.log(`saved ${frame} frame pairs`);

await browser.close();
