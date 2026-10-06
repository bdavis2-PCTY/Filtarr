import { useEffect, useState } from "react";
import FiltersPage from "./pages/FiltersPage";
import HistoryPage from "./pages/HistoryPage";
import ListPage from "./pages/ListPage";
import QuickFiltersPage from "./pages/QuickFiltersPage";
import SearchPage from "./pages/SearchPage";
import SeriesPage from "./pages/SeriesPage";
import SettingsPage from "./pages/SettingsPage";
import { parseRoute, recordPage } from "./routes";

const tabs = [
  { page: "search", label: "Search" },
  { page: "filters", label: "Global filters" },
  { page: "quick-filters", label: "Quick Filters" },
  { page: "list", label: "List & Sync" },
  { page: "history", label: "History" },
  { page: "settings", label: "Settings" },
] as const;

export default function App() {
  const [route, setRoute] = useState(() => parseRoute(location.hash));
  useEffect(() => recordPage(route.page), [route.page]);
  useEffect(() => {
    const onChange = () => setRoute(parseRoute(location.hash));
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);

  // A series page belongs to the Search tab.
  const activeTab = route.page === "series" ? "search" : route.page;

  return (
    <div className="app">
      <header>
        <h1>Filtarr</h1>
        <nav>
          {tabs.map((t) => (
            <a
              key={t.page}
              href={`#/${t.page}`}
              className={t.page === activeTab ? "active" : ""}
            >
              {t.label}
            </a>
          ))}
        </nav>
      </header>
      <main>
        {route.page === "search" && <SearchPage />}
        {route.page === "series" && (
          <SeriesPage key={route.imdbId} imdbId={route.imdbId} />
        )}
        {route.page === "filters" && <FiltersPage />}
        {route.page === "quick-filters" && <QuickFiltersPage />}
        {route.page === "list" && <ListPage />}
        {route.page === "history" && <HistoryPage />}
        {route.page === "settings" && <SettingsPage />}
      </main>
    </div>
  );
}
