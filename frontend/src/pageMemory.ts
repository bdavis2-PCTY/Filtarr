import { useLayoutEffect } from "react";
import type { Preview, Run, SeriesInfo } from "./api";

/**
 * What a page needs to come back exactly as the user left it (browser back, the "Back to search" link, or a tab click):
 * its inputs, its results and its scroll position. Pages unmount when you leave them, so this lives at module level.
 *
 * Anything that changes data (every non-GET API call) bumps `dataVersion`. A remembered page is "fresh" if nothing changed since
 * it loaded, in which case it is shown instantly with no request at all; if something did change, it is still shown instantly and
 * (for search) quietly refreshed behind the scenes.
 */
let dataVersion = 0;

export const invalidatePages = () => {
  dataVersion++;
};
export const currentDataVersion = () => dataVersion;

export type SearchMemory = {
  query: string;
  results: SeriesInfo[] | null;
  withFilters: SeriesInfo[] | null;
  /** `dataVersion` when the shown list was loaded. */
  version: number;
};
let search: SearchMemory | null = null;
export const searchMemory = { get: () => search, set: (m: SearchMemory) => void (search = m) };

export type ListMemory = {
  seriesId: number | undefined;
  preview: Preview | null;
  run: Run | null;
  version: number;
};
let list: ListMemory | null = null;
export const listMemory = { get: () => list, set: (m: ListMemory) => void (list = m) };

const scrollPositions = new Map<string, number>();

/**
 * Remembers the window's scroll position for `key` while the page is mounted and puts it back when the page mounts again.
 * A page with nothing remembered starts at the top (the window would otherwise keep the previous page's scroll position).
 */
export function useScrollMemory(key: string) {
  // A layout effect, so the restore happens before paint and the page never visibly jumps.
  useLayoutEffect(() => {
    window.scrollTo(0, scrollPositions.get(key) ?? 0);
    // Saved on the way out, not tracked with a scroll listener: a layout-effect cleanup runs synchronously during the page swap,
    // while the page being left is still in the DOM, so scrollY is still its own. (A listener would record the next page's
    // reset to 0 over it.)
    return () => {
      scrollPositions.set(key, window.scrollY);
    };
  }, [key]);
}
