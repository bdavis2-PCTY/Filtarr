import FilterManager from "../components/FilterManager";

/** Global filters, which apply to every series. Per-series filters are managed on each series' page. */
export default function FiltersPage() {
  return (
    <>
      <p className="hint">
        Global filters apply to every series in Sonarr. To add filters for one
        series, search for it on the Search tab and open its page.
      </p>
      <FilterManager imdbId={null} />
    </>
  );
}
