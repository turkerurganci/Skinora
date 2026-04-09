import { defineRouting } from "next-intl/routing";

export const routing = defineRouting({
  locales: ["en", "zh", "es", "tr"],
  defaultLocale: "en",
});
