# SEO content and measurement plan

## Existing articles

Three subjects are published in Persian and English. Keep each translation on its own canonical URL; both versions share a slug. The editorial changes refine titles and summaries and add relevant links between these subjects without changing URLs or exposing internal project names.

| Subject | Candidate queries (hypotheses, not measured volume) | Reader intent | Internal connections |
| --- | --- | --- | --- |
| CQRS | CQRS چیست، جداسازی command query، CQRS consistency; CQRS shared vs separate databases | Decide how much read/write separation is useful | Data coupling for service boundaries; orchestration for workflows |
| Orchestration and Saga | Saga چیست، الگوی outbox، مدیریت خطای API؛ saga orchestration outbox MassTransit | Recover a sequence of dependent actions | CQRS for read-model lag; coupling for distributed boundaries |
| Microservice data coupling | معایب میکروسرویس، وابستگی داده؛ microservices data coupling trade-offs | Decide whether a service split is worth its cost | CQRS for read projections; Saga for cross-service actions |

## Next articles

Publish an article when it adds a useful example, failure scenario or decision aid. Translate it with the same technical meaning rather than duplicating keywords. These topics are candidates, not a publishing commitment.

| Priority | Proposed article | What it should answer | Link from |
| --- | --- | --- | --- |
| 1 | Idempotency after API timeouts | How do we distinguish failure from an unknown outcome and avoid duplicate effects? | Saga |
| 2 | Transactional Outbox in .NET | What does one database transaction guarantee, and what can still be delivered twice? | Saga |
| 3 | Modular monolith before microservices | Which boundaries are useful before independent deployment is needed? | Data coupling |
| 4 | CQRS read-model lag | How should a UI behave immediately after a successful write? | CQRS |
| 5 | Retry, backoff and circuit breakers | Which failures are retryable and when does retry amplify an outage? | Saga |
| 6 | Querying across service boundaries | When should we use API composition, a projection or a different boundary? | Data coupling |
| 7 | Compensation that can fail | What happens when the recovery operation itself times out? | Saga |
| 8 | Ordering RabbitMQ messages | What ordering can consumers rely on with concurrency and redelivery? | Saga |
| 9 | Evolving integration-event contracts | How can old and new consumers coexist during deployment? | Saga, data coupling |
| 10 | Rebuilding a read projection | How do replay, checkpoints and live writes interact? | CQRS |

Use generic examples (orders or insurance claims), original diagrams when they explain a decision, and links to primary technical documentation. Never include employer names, internal endpoints, customer data or proprietary implementation details. Avoid asserting personal production results without evidence.

## Search Console: registration already complete

After the new release is live:

1. Open the existing domain property, or the URL-prefix property matching `https://www.amirrezaghahremani.ir/`. Under **Sitemaps**, submit `https://www.amirrezaghahremani.ir/sitemap.xml` if it is not already submitted. Check for success and the discovered URL count (currently four main pages and six articles).
2. Inspect one Persian article and its English translation with **URL inspection**. Check live fetch, indexability and Google's selected canonical. Request indexing for the new or substantially updated pages when appropriate; submission is not an indexing guarantee.
3. Record a baseline in **Performance → Search results**: date range, clicks, impressions, CTR and average position. Export Queries and Pages; compare `/fa/blog/` with `/blog/`, branded with non-branded searches, and mobile with desktop. Keep country and search type filters consistent.
4. In **Page indexing**, investigate exclusions for intended public canonical URLs. Drafts, missing articles and admin pages should not be indexed. Check **Core Web Vitals** when enough field data exists; a new or low-traffic site may have no report.
5. Review after Google has recrawled the changes, then compare equal periods (for example 28 days). Keep a dated release note. Avoid attributing a ranking change to a single edit when content, demand and crawling changed together.

Keep Search Console exports and site analytics private. Do not fill metrics with estimates. Account reports can be exported and analyzed without sharing a password. Use the actual report date range, which may end before the export date.

## Verification and measurement limits

Run the isolated checks documented in the README before releasing. They cover sitemap pagination, publication status, language pairing, structured data, related links, canonical pagination, upstream errors, compression and the existing admin lifecycle.

Mobile browser measurements use a 390×844 viewport, CPU throttling and an emulated network. They are local lab samples, not PageSpeed scores, production TTFB, INP measurements or Search Console field data. Compare HTML bytes and external requests directly; treat single-run LCP differences cautiously. Recheck the actual production pages after deployment.

## Primary references

- [Google: localized versions and reciprocal hreflang](https://developers.google.com/search/docs/specialty/international/localized-versions)
- [Google: building and submitting a sitemap](https://developers.google.com/search/docs/crawling-indexing/sitemaps/build-sitemap)
- [Google: Article structured data](https://developers.google.com/search/docs/appearance/structured-data/article)
- [Search Console performance report](https://support.google.com/webmasters/answer/7576553)
- [URL inspection](https://support.google.com/webmasters/answer/9012289)
