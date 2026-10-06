/** Placeholder shaped like the series page (hero, filters), shown while the series loads. */
export default function SeriesSkeleton() {
  return (
    <div role="status" aria-busy="true">
      <span className="sr-only">Loading series…</span>
      <section className="hero" aria-hidden="true">
        <div className="hero-inner">
          <div className="poster hero-poster skeleton" />
          <div className="hero-body hero-skeleton">
            <div className="skeleton skeleton-line" style={{ width: "42%", height: 28 }} />
            <div className="skeleton skeleton-line" style={{ width: "34%" }} />
            <div className="row">
              <div className="skeleton skeleton-pill" style={{ width: 96, height: 40, borderRadius: 8 }} />
              <div className="skeleton skeleton-pill" style={{ width: 110 }} />
              <div className="skeleton skeleton-button" />
            </div>
            <div className="row">
              <div className="skeleton skeleton-pill" />
              <div className="skeleton skeleton-pill" />
              <div className="skeleton skeleton-pill" />
            </div>
            <div className="skeleton skeleton-line" style={{ width: "95%" }} />
            <div className="skeleton skeleton-line" style={{ width: "90%" }} />
            <div className="skeleton skeleton-line" style={{ width: "55%" }} />
            <div className="facts">
              {[60, 45, 30].map((w) => (
                <div key={w} style={{ display: "contents" }}>
                  <div className="skeleton skeleton-line" style={{ width: 70 }} />
                  <div className="skeleton skeleton-line" style={{ width: `${w}%` }} />
                </div>
              ))}
            </div>
          </div>
        </div>
      </section>

      <div className="skeleton skeleton-line" style={{ width: 220, height: 20, margin: "16px 0 10px" }} aria-hidden="true" />
      {[0, 1].map((i) => (
        <div key={i} className="card" aria-hidden="true">
          <div className="row">
            <div className="skeleton skeleton-line" style={{ width: 180 }} />
            <span className="spacer" />
            <div className="skeleton skeleton-button" style={{ width: 70 }} />
            <div className="skeleton skeleton-button" style={{ width: 70 }} />
          </div>
          <div className="skeleton skeleton-line" style={{ width: "50%", margin: "10px 0 6px" }} />
        </div>
      ))}
    </div>
  );
}
