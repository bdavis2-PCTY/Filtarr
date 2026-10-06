import { type SeriesDetails, type SeriesInfo } from "../api";
import SonarrAction from "./SonarrAction";

const compact = new Intl.NumberFormat("en", { notation: "compact", maximumFractionDigits: 1 });

const runtime = (m: number) => (m >= 60 ? `${Math.floor(m / 60)}h${m % 60 ? ` ${m % 60}m` : ""}` : `${m}m`);
const capitalize = (s: string) => s.charAt(0).toUpperCase() + s.slice(1);

/** Dot-separated facts that are present, e.g. "TV-MA · 49m · Continuing · Apple TV". */
function metaLine(d: SeriesDetails): string[] {
  return [
    d.certification,
    d.runtimeMinutes ? runtime(d.runtimeMinutes) : null,
    d.status ? capitalize(d.status) : null,
    d.network,
    d.firstAired ? `First aired ${d.firstAired}` : null,
  ].filter((x): x is string => !!x);
}

function seasonsLine(d: SeriesDetails): string | null {
  if (!d.seasons && !d.episodes) return null;
  const parts = [d.seasons ? `${d.seasons} season${d.seasons === 1 ? "" : "s"}` : null, d.episodes ? `${d.episodes} episodes` : null];
  const downloaded = d.inSonarr && d.episodes ? ` (${d.episodesDownloaded ?? 0} downloaded)` : "";
  return parts.filter(Boolean).join(" · ") + downloaded;
}

function Skeletons() {
  return (
    <div className="hero-skeleton" aria-busy="true">
      <span className="sr-only">Loading details…</span>
      <div className="skeleton skeleton-line" style={{ width: "40%" }} />
      <div className="skeleton skeleton-line" style={{ width: "95%" }} />
      <div className="skeleton skeleton-line" style={{ width: "90%" }} />
      <div className="skeleton skeleton-line" style={{ width: "60%" }} />
    </div>
  );
}

/**
 * The top of a series page, laid out like a title page on IMDb: backdrop, poster, title and years, a line of facts, the rating,
 * genres, the plot and cast, then a list of details. Every part is optional because the sources (IMDb, OMDb, Sonarr) differ in
 * what they know.
 */
export default function SeriesHero({
  info,
  details,
  loadingDetails,
  onAdded,
}: {
  info: SeriesInfo;
  details: SeriesDetails | null;
  loadingDetails: boolean;
  onAdded: (updated: SeriesInfo) => void;
}) {
  const d = details;
  const poster = d?.posterUrl ?? info.imageUrl;
  const years = d?.yearRange ?? info.yearRange ?? (info.year ? String(info.year) : null);
  const cast = d?.cast.length ? d.cast : info.cast ? info.cast.split(",").map((c) => c.trim()) : [];
  const facts = d ? metaLine(d) : [];
  const seasons = d ? seasonsLine(d) : null;
  const backdrop = d?.backdropUrl
    ? { backgroundImage: `linear-gradient(90deg, rgba(20,23,28,0.97) 28%, rgba(20,23,28,0.7)), url(${d.backdropUrl})` }
    : undefined;

  return (
    <section className="hero" style={backdrop}>
      <div className="hero-inner">
        {poster ? <img className="poster hero-poster" src={poster} alt="" /> : <div className="poster hero-poster placeholder" />}

        <div className="hero-body">
          <h1 className="hero-title">
            {info.title}
            {years && <span className="hero-years"> ({years})</span>}
          </h1>
          {facts.length > 0 && <div className="hero-meta">{facts.join(" · ")}</div>}

          <div className="hero-actions">
            {d?.rating != null && (
              <div className="rating-box" title={d.ratingSource ? `Rating from ${d.ratingSource}` : undefined}>
                <span className="rating-star">★</span>
                <span>
                  <strong>{d.rating.toFixed(1)}</strong>
                  <span className="rating-of">/10</span>
                </span>
                {d.votes != null && (
                  <span className="rating-votes">
                    {compact.format(d.votes)} · {d.ratingSource}
                  </span>
                )}
              </div>
            )}
            <a href={`https://www.imdb.com/title/${info.imdbId}/`} target="_blank" rel="noreferrer">
              View on IMDb ↗
            </a>
            <span className={`badge ${info.inSonarr ? "Include" : ""}`}>{info.inSonarr ? "in Sonarr" : "not in Sonarr"}</span>
            <SonarrAction info={info} onAdded={onAdded} />
          </div>

          {d && d.genres.length > 0 && (
            <div className="chips">
              {d.genres.map((g) => (
                <span key={g} className="chip">
                  {g}
                </span>
              ))}
            </div>
          )}

          {loadingDetails && !d ? <Skeletons /> : d?.plot && <p className="hero-plot">{d.plot}</p>}

          <dl className="facts">
            {cast.length > 0 && (
              <>
                <dt>Stars</dt>
                <dd>{cast.join(" · ")}</dd>
              </>
            )}
            {seasons && (
              <>
                <dt>Seasons</dt>
                <dd>{seasons}</dd>
              </>
            )}
            {d?.network && (
              <>
                <dt>Network</dt>
                <dd>{d.network}</dd>
              </>
            )}
            {d?.language && (
              <>
                <dt>Language</dt>
                <dd>{d.language}</dd>
              </>
            )}
            {d?.country && (
              <>
                <dt>Country</dt>
                <dd>{d.country}</dd>
              </>
            )}
            {d?.awards && (
              <>
                <dt>Awards</dt>
                <dd>{d.awards}</dd>
              </>
            )}
            {d && d.tags.length > 0 && (
              <>
                <dt>Sonarr tags</dt>
                <dd>
                  <span className="chips">
                    {d.tags.map((t) => (
                      <span key={t} className="chip tag">
                        {t}
                      </span>
                    ))}
                  </span>
                </dd>
              </>
            )}
          </dl>

          {d && !d.hasFullDetails && !loadingDetails && (
            <p className="hint">
              Showing what Sonarr and IMDb provide. Add an OMDb API key in Settings for the full plot, cast and the IMDb rating.
            </p>
          )}
        </div>
      </div>
    </section>
  );
}
