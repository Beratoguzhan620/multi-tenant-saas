import { describe, expect, it } from "vitest";
import { bytes, percent, relative, slugify } from "./format";
import { parseRoute } from "./router";
import { ticketQueryString } from "./api";

describe("format", () => {
  it("formats sizes", () => {
    expect(bytes(512)).toBe("512 B");
    expect(bytes(1536)).toBe("1.5 KB");
    expect(bytes(100 * 1024 * 1024)).toBe("100 MB");
    expect(bytes(5 * 1024 ** 3)).toBe("5 GB");
  });

  it("formats times relative to now", () => {
    const now = new Date("2026-11-02T12:00:00Z");
    expect(relative("2026-11-02T11:59:50Z", now)).toBe("just now");
    expect(relative("2026-11-02T11:55:00Z", now)).toBe("5 min ago");
    expect(relative("2026-11-02T09:00:00Z", now)).toBe("3 h ago");
    expect(relative("2026-11-01T10:00:00Z", now)).toBe("yesterday");
  });

  it("suggests slugs from Turkish names", () => {
    expect(slugify("Kargo Hızlı A.Ş.")).toBe("kargo-hizli-a-s");
    expect(slugify("  Çiçek Sepeti  ")).toBe("cicek-sepeti");
    expect(slugify("Öğrenci İşleri")).toBe("ogrenci-isleri");
  });

  it("caps usage at 100 percent and treats no limit as zero", () => {
    expect(percent(50, 100)).toBe(50);
    expect(percent(150, 100)).toBe(100);
    expect(percent(10, null)).toBe(0);
  });
});

describe("routes", () => {
  it("parses the team's pages", () => {
    expect(parseRoute("/", "")).toEqual({ page: "tickets" });
    expect(parseRoute("/tickets/1001", "")).toEqual({ page: "ticket", number: 1001 });
    expect(parseRoute("/team", "")).toEqual({ page: "team" });
    expect(parseRoute("/tickets/abc", "")).toEqual({ page: "not-found" });
  });

  it("keeps secrets in the fragment", () => {
    expect(parseRoute("/join", "#tok_en")).toEqual({ page: "join", token: "tok_en" });
    expect(parseRoute("/join", "")).toEqual({ page: "not-found" });
    expect(parseRoute("/support/kargo/tickets/1001", "#key")).toEqual({ page: "support-ticket", slug: "kargo", number: 1001, key: "key" });
    expect(parseRoute("/support/kargo", "")).toEqual({ page: "support", slug: "kargo" });
  });
});

describe("ticket queries", () => {
  it("leaves out defaults", () => {
    expect(ticketQueryString({})).toBe("");
    expect(ticketQueryString({ status: "Open", q: "  iade ", page: 1 })).toBe("?status=Open&q=iade");
    expect(ticketQueryString({ assignee: "me", page: 3, pageSize: 25 })).toBe("?assignee=me&page=3&pageSize=25");
  });
});
