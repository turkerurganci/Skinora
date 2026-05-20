import Link from "next/link";
import { LanguageSelector } from "@/components/common";

export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-screen flex-col bg-gray-50">
      <header className="flex items-center justify-between border-b border-gray-200 bg-white px-4 py-3">
        <Link href="/" className="text-lg font-semibold text-gray-900" aria-label="Skinora">
          Skinora
        </Link>
        <LanguageSelector />
      </header>
      <main className="flex flex-1 items-center justify-center px-4 py-10">{children}</main>
    </div>
  );
}
