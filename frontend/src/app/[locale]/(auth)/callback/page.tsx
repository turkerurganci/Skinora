"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

export default function SteamCallbackPage() {
  const router = useRouter();

  useEffect(() => {
    // Steam OpenID callback handling will be implemented in T29
    router.push("/");
  }, [router]);

  return (
    <div className="flex min-h-screen items-center justify-center">
      <p>Authenticating...</p>
    </div>
  );
}
