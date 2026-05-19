"use client";

import { useState } from "react";
import {
  CancelModal,
  CopyButton,
  CountdownTimer,
  DisputeForm,
  EmptyState,
  ErrorState,
  FilterBar,
  ItemCard,
  LanguageSelector,
  MaintenanceBanner,
  Pagination,
  Progress,
  Skeleton,
  Spinner,
  StatusBadge,
  ToastProvider,
  TransactionTimeline,
  UserCard,
  WalletAddressInput,
  useToast,
} from "@/components/common";
import { DisputeType, TimeoutFreezeReason, TransactionStatus } from "@/types/enums";

const ALL_STATUSES = [
  TransactionStatus.CREATED,
  TransactionStatus.ACCEPTED,
  TransactionStatus.TRADE_OFFER_SENT_TO_SELLER,
  TransactionStatus.ITEM_ESCROWED,
  TransactionStatus.PAYMENT_RECEIVED,
  TransactionStatus.TRADE_OFFER_SENT_TO_BUYER,
  TransactionStatus.ITEM_DELIVERED,
  TransactionStatus.COMPLETED,
  TransactionStatus.CANCELLED_TIMEOUT,
  TransactionStatus.CANCELLED_SELLER,
  TransactionStatus.CANCELLED_BUYER,
  TransactionStatus.CANCELLED_ADMIN,
  TransactionStatus.FLAGGED,
] as const;

function Section({
  id,
  title,
  children,
}: {
  id: string;
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section id={id} className="space-y-4 border-b border-gray-200 py-8">
      <h2 className="text-xl font-semibold text-gray-900">{title}</h2>
      <div className="space-y-4">{children}</div>
    </section>
  );
}

function ToastDemo() {
  const { push } = useToast();
  return (
    <div className="flex flex-wrap gap-2">
      <button
        type="button"
        className="rounded-md bg-blue-600 px-3 py-1.5 text-sm text-white"
        onClick={() => push({ variant: "info", title: "Info", message: "Bilgilendirme mesajı" })}
      >
        Info
      </button>
      <button
        type="button"
        className="rounded-md bg-green-600 px-3 py-1.5 text-sm text-white"
        onClick={() => push({ variant: "success", title: "Success", message: "İşlem başarılı" })}
      >
        Success
      </button>
      <button
        type="button"
        className="rounded-md bg-yellow-600 px-3 py-1.5 text-sm text-white"
        onClick={() => push({ variant: "warning", title: "Warning", message: "Dikkat" })}
      >
        Warning
      </button>
      <button
        type="button"
        className="rounded-md bg-red-600 px-3 py-1.5 text-sm text-white"
        onClick={() => push({ variant: "error", title: "Error", message: "Bir hata oluştu" })}
      >
        Error
      </button>
    </div>
  );
}

function ContentInner() {
  const [cancelOpen, setCancelOpen] = useState(false);
  const [page, setPage] = useState(3);
  const [selectedItem, setSelectedItem] = useState<string | null>(null);

  const [{ deadlineFar, deadlineWarn, deadlineCritical }] = useState(() => {
    const now = Date.now();
    return {
      deadlineFar: new Date(now + 2 * 24 * 3600 * 1000),
      deadlineWarn: new Date(now + 5 * 60 * 1000),
      deadlineCritical: new Date(now + 30 * 1000),
    };
  });

  return (
    <div className="mx-auto max-w-5xl space-y-2 px-4 py-8">
      <header className="space-y-2 border-b border-gray-200 pb-6">
        <h1 className="text-2xl font-bold text-gray-900">
          T84 — Ortak UI Bileşenleri (Dev Showcase)
        </h1>
        <p className="text-sm text-gray-600">
          04 §5 Ortak Bileşen Kütüphanesi (C01–C17) görsel doğrulaması.
        </p>
      </header>

      <Section id="c01" title="C01 — Status Badge (14 durum)">
        <div className="flex flex-wrap gap-2">
          {ALL_STATUSES.map((s) => (
            <StatusBadge key={s} status={s} />
          ))}
          <StatusBadge status="EMERGENCY_HOLD" />
        </div>
      </Section>

      <Section id="c02" title="C02 — Countdown Timer">
        <div className="space-y-3">
          <div>
            <p className="text-sm text-gray-600">Far (green)</p>
            <CountdownTimer deadline={deadlineFar} warningThresholdSeconds={60 * 60} />
          </div>
          <div>
            <p className="text-sm text-gray-600">Warning (yellow)</p>
            <CountdownTimer deadline={deadlineWarn} warningThresholdSeconds={60 * 60} />
          </div>
          <div>
            <p className="text-sm text-gray-600">Critical (red, pulsing)</p>
            <CountdownTimer deadline={deadlineCritical} warningThresholdSeconds={60} />
          </div>
          <div>
            <p className="text-sm text-gray-600">Frozen (MAINTENANCE)</p>
            <CountdownTimer
              deadline={deadlineFar}
              warningThresholdSeconds={60 * 60}
              frozen
              frozenReason={TimeoutFreezeReason.MAINTENANCE}
            />
          </div>
          <div>
            <p className="text-sm text-gray-600">Clock format</p>
            <CountdownTimer
              deadline={deadlineFar}
              warningThresholdSeconds={60 * 60}
              format="clock"
            />
          </div>
        </div>
      </Section>

      <Section id="c03" title="C03 — Item Card (3 varyant)">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
          <ItemCard
            variant="compact"
            item={{
              steamItemId: "1",
              name: "AK-47 | Redline (Field-Tested)",
              tradeable: true,
            }}
          />
          <ItemCard
            variant="detailed"
            item={{
              steamItemId: "2",
              name: "AWP | Asiimov (Battle-Scarred)",
              type: "Sniper Rifle",
              wear: "Battle-Scarred",
              tradeable: true,
            }}
          />
          <ItemCard
            variant="selectable"
            selected={selectedItem === "3"}
            onSelect={(it) => setSelectedItem(it.steamItemId)}
            item={{
              steamItemId: "3",
              name: "Karambit | Doppler",
              wear: "Factory New",
              tradeable: false,
            }}
          />
        </div>
      </Section>

      <Section id="c04" title="C04 — User Card (2 varyant)">
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          <UserCard
            variant="compact"
            user={{
              steamId: "76561198000000001",
              username: "TraderAce",
              reputationScore: 4.5,
              completedTransactions: 27,
              accountAgeText: "2 yıl",
            }}
          />
          <UserCard
            variant="detailed"
            user={{
              steamId: "76561198000000002",
              username: "SkinHunter",
              reputationScore: 3,
              completedTransactions: 5,
              accountAgeText: "6 ay",
            }}
          />
        </div>
      </Section>

      <Section id="c05" title="C05 — Transaction Timeline (8 adım)">
        <div className="space-y-6">
          <div>
            <p className="text-sm text-gray-600">Active (PAYMENT_RECEIVED)</p>
            <TransactionTimeline status={TransactionStatus.PAYMENT_RECEIVED} />
          </div>
          <div>
            <p className="text-sm text-gray-600">Completed</p>
            <TransactionTimeline status={TransactionStatus.COMPLETED} />
          </div>
          <div>
            <p className="text-sm text-gray-600">Cancelled</p>
            <TransactionTimeline status={TransactionStatus.CANCELLED_BUYER} cancelled />
          </div>
          <div>
            <p className="text-sm text-gray-600">Flagged</p>
            <TransactionTimeline status={TransactionStatus.FLAGGED} flagged />
          </div>
        </div>
      </Section>

      <Section id="c06" title="C06 — Cancel Modal">
        <button
          type="button"
          onClick={() => setCancelOpen(true)}
          className="rounded-md bg-red-600 px-3 py-2 text-sm font-medium text-white"
        >
          Modal&apos;ı aç
        </button>
        <CancelModal
          open={cancelOpen}
          refundDescription="Item satıcıya iade edilecektir."
          onClose={() => setCancelOpen(false)}
          onConfirm={(reason) => {
            console.log("cancel reason", reason);
            setCancelOpen(false);
          }}
        />
      </Section>

      <Section id="c07" title="C07 — Dispute Form (3 adım)">
        <DisputeForm
          onAutoCheck={async (type) => {
            await new Promise((r) => setTimeout(r, 1000));
            return type === DisputeType.PAYMENT ? "resolved" : "unresolved";
          }}
          onEscalate={async () => {
            await new Promise((r) => setTimeout(r, 500));
          }}
        />
      </Section>

      <Section id="c08" title="C08 — Maintenance Banner (4 varyant)">
        <div className="space-y-2">
          <MaintenanceBanner variant="plannedMaintenance" />
          <MaintenanceBanner variant="activeMaintenance" />
          <MaintenanceBanner variant="steamOutage" />
          <MaintenanceBanner variant="blockchainDegradation" />
        </div>
      </Section>

      <Section id="c09" title="C09 — Toast Notification (4 varyant)">
        <ToastDemo />
      </Section>

      <Section id="c10" title="C10 — Language Selector (4 dil)">
        <LanguageSelector />
      </Section>

      <Section id="c11" title="C11 — Wallet Address Input">
        <WalletAddressInput
          onValidate={async (addr) => {
            await new Promise((r) => setTimeout(r, 600));
            return addr.endsWith("X")
              ? { status: "sanctioned" as const }
              : { status: "ok" as const };
          }}
          onConfirm={(addr) => console.log("confirmed", addr)}
        />
      </Section>

      <Section id="c12" title="C12 — Copy Button">
        <div className="flex items-center gap-2">
          <code className="rounded bg-gray-100 px-2 py-1 text-sm">
            TR7ABC123def456ghi789jkl012MNO345PQ
          </code>
          <CopyButton value="TR7ABC123def456ghi789jkl012MNO345PQ" />
        </div>
      </Section>

      <Section id="c13" title="C13 — Empty State">
        <EmptyState
          title="Henüz işleminiz yok"
          description="İlk işleminizi başlatın."
          action={
            <button
              type="button"
              className="rounded-md bg-blue-600 px-3 py-2 text-sm font-medium text-white"
            >
              İlk işlemini başlat
            </button>
          }
        />
      </Section>

      <Section id="c14" title="C14 — Loading State (Skeleton / Spinner / Progress)">
        <div className="space-y-3">
          <div>
            <p className="text-sm text-gray-600">Skeleton</p>
            <div className="space-y-2">
              <Skeleton className="h-4 w-3/4" />
              <Skeleton className="h-4 w-1/2" />
              <Skeleton className="h-20 w-full" />
            </div>
          </div>
          <div>
            <p className="text-sm text-gray-600">Spinner (sm/md/lg)</p>
            <div className="flex items-center gap-3">
              <Spinner size="sm" />
              <Spinner size="md" />
              <Spinner size="lg" />
            </div>
          </div>
          <div>
            <p className="text-sm text-gray-600">Progress</p>
            <Progress value={42} />
          </div>
        </div>
      </Section>

      <Section id="c15" title="C15 — Error State">
        <ErrorState message="Sunucuya bağlanılamadı." onRetry={() => console.log("retry")} />
      </Section>

      <Section id="c16" title="C16 — Pagination">
        <Pagination currentPage={page} totalPages={10} onPageChange={setPage} />
        <p className="text-xs text-gray-500">Current: {page}</p>
      </Section>

      <Section id="c17" title="C17 — Filter Bar">
        <FilterBar
          fields={[
            {
              key: "status",
              label: "Durum",
              kind: "select",
              options: [
                { value: "ACTIVE", label: "Aktif" },
                { value: "COMPLETED", label: "Tamamlanan" },
                { value: "CANCELLED", label: "İptal" },
              ],
            },
            { key: "query", label: "Arama", kind: "text", placeholder: "Item adı" },
            { key: "from", label: "Başlangıç", kind: "date" },
          ]}
          onApply={(values) => console.log("filter applied", values)}
        />
      </Section>
    </div>
  );
}

export default function ComponentShowcasePage() {
  return (
    <ToastProvider>
      <ContentInner />
    </ToastProvider>
  );
}
