/** Minimal hash routing: "#/search", "#/series/tt0903747", ... */
export const seriesPath = (imdbId: string) => `#/series/${imdbId}`;

export type Route =
  | { page: "search" | "filters" | "quick-filters" | "list" | "history" | "settings" }
  | { page: "series"; imdbId: string };

export function parseRoute(hash: string): Route {
  const path = hash.replace(/^#\/?/, "");
  const series = /^series\/(tt\d+)$/.exec(path);
  if (series) return { page: "series", imdbId: series[1] };
  switch (path) {
    case "filters":
    case "quick-filters":
    case "list":
    case "history":
    case "settings":
      return { page: path };
    default:
      return { page: "search" };
  }
}

let previousPage: Route["page"] | null = null;
let currentPage: Route["page"] | null = null;

/** Called on every route change, so a page can tell where the user came from. */
export function recordPage(page: Route["page"]) {
  if (page === currentPage) return;
  previousPage = currentPage;
  currentPage = page;
}

/**
 * Click handler for "Back to search". When the user came from the search page it goes back in browser history, exactly like the
 * browser's back button: the search page returns as it was, and history doesn't pile up. Otherwise the link just navigates.
 */
export function backToSearch(e: { preventDefault(): void }) {
  if (previousPage === "search" && window.history.length > 1) {
    e.preventDefault();
    window.history.back();
  }
}
