import QuickFilterManager from "../components/QuickFilterManager";

/** Reusable filter templates. They never run on their own; pick one on a series page to start a new filter from it. */
export default function QuickFiltersPage() {
  return (
    <>
      <p className="hint">
        Quick Filters are reusable filter definitions. They are not applied to anything themselves: on a series page,
        choose one from the “New filter” dropdown to start a new filter with its name and conditions filled in.
      </p>
      <QuickFilterManager />
    </>
  );
}
