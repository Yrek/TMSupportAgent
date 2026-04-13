import { useEffect } from "react";

const APP_NAME = "Threat Modeling Agent";

/**
 * Sets document.title to `{title} — {APP_NAME}`.
 * Restores the previous title on unmount so nested calls compose correctly.
 */
export function usePageTitle(title: string) {
  useEffect(() => {
    const prev = document.title;
    document.title = title ? `${title} — ${APP_NAME}` : APP_NAME;
    return () => {
      document.title = prev;
    };
  }, [title]);
}
