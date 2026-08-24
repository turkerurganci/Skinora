import { describe, it, expect, afterEach } from "vitest";
import { cleanup, render, screen, within } from "@testing-library/react";
import { NextIntlClientProvider } from "next-intl";
import messages from "@/i18n/messages/en.json";
import type { AdminUserListItem } from "@/lib/api/admin";
import { AdminUserTable } from "./AdminUserTable";

/**
 * F5 — `UITour-AdminUsersPageIsStub`.
 *
 * The assertion that matters here is the LINK, not the pixels: the S20 detail
 * page has existed since T39 and worked the whole time; the only thing missing
 * for four phases was a way to reach it without typing a Steam ID into the
 * address bar. A row that renders a name but forgets the href would look
 * completely fine in a screenshot and leave the original defect in place, so
 * every row test reads `href` rather than text.
 *
 * The real `en.json` is used on purpose: a key the component addresses but no
 * locale carries renders as its own dotted path, and asserting on real copy
 * catches that.
 */

const USERS: AdminUserListItem[] = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    steamId: "76561199053273410",
    displayName: "moderator one",
    avatarUrl: "https://avatars.example/one.jpg",
    role: { id: "role-1", name: "Moderator" },
  },
  {
    id: "22222222-2222-2222-2222-222222222222",
    steamId: "76561198000000002",
    displayName: "plain user",
    avatarUrl: null,
    role: null,
  },
];

function renderTable(users: AdminUserListItem[] = USERS) {
  return render(
    <NextIntlClientProvider locale="en" messages={messages}>
      <AdminUserTable users={users} />
    </NextIntlClientProvider>,
  );
}

/** The desktop <table> and the mobile card list both render every row, so a
 *  bare `getAllByRole("link")` sees each user twice. Scope to the table. */
function desktopTable() {
  return screen.getByRole("table", { name: messages.adminUsers.tableAriaLabel });
}

afterEach(cleanup);

describe("AdminUserTable", () => {
  it("her satırın adını S20 detay sayfasına bağlar", () => {
    renderTable();
    const table = desktopTable();

    expect(within(table).getByRole("link", { name: "moderator one" })).toHaveAttribute(
      "href",
      "/en/admin/users/76561199053273410",
    );
    expect(within(table).getByRole("link", { name: "plain user" })).toHaveAttribute(
      "href",
      "/en/admin/users/76561198000000002",
    );
  });

  it("ada ek olarak ayrı bir detay bağlantısı sunar ve o da aynı yere gider", () => {
    renderTable();
    const detailLinks = within(desktopTable()).getAllByRole("link", {
      name: messages.adminUsers.actions.detail,
    });

    expect(detailLinks).toHaveLength(2);
    expect(detailLinks[0]).toHaveAttribute("href", "/en/admin/users/76561199053273410");
  });

  it("Steam ID'yi ham hâliyle gösterir — detay sayfasına giden anahtar bu", () => {
    renderTable();
    expect(within(desktopTable()).getByText("76561199053273410")).toBeInTheDocument();
  });

  it("rolü olan kullanıcıda rol adını, olmayanda 'No role' gösterir", () => {
    renderTable();
    const table = desktopTable();

    expect(within(table).getByText("Moderator")).toBeInTheDocument();
    expect(within(table).getByText(messages.adminUsers.noRole)).toBeInTheDocument();
  });

  it("avatar yoksa satır düşmez, baş harflere döner", () => {
    renderTable([USERS[1]]);
    const table = desktopTable();

    expect(within(table).queryByRole("img")).not.toBeInTheDocument();
    expect(within(table).getByText("pl")).toBeInTheDocument();
    expect(within(table).getByRole("link", { name: "plain user" })).toBeInTheDocument();
  });

  it("avatar varsa dekoratif olarak işaretlenir — ad zaten bağlantı metni", () => {
    renderTable([USERS[0]]);
    const avatar = within(desktopTable()).getByRole("presentation", { hidden: true });
    expect(avatar).toHaveAttribute("src", "https://avatars.example/one.jpg");
  });
});
