// Loaded only by the packaged-engine smoke test to make its upstream deterministic.
const fetchNetwork = globalThis.fetch;
globalThis.fetch = async (input, options) => {
  const url = new URL(input instanceof Request ? input.url : input);
  if (url.origin !== "https://dnd5e.lamagra.link")
    return fetchNetwork(input, options);
  switch (url.pathname) {
    case "/search/index.json":
      return Response.json({
        m: { s: { MM: 1 } },
        x: [{ c: 1, n: "Remote goblin", s: 1 }],
      });
    case "/data/bestiary/index.json":
      return Response.json({ MM: "bestiary-mm.json" });
    case "/data/bestiary/bestiary-mm.json":
      return Response.json({
        monster: [
          {
            name: "Remote goblin",
            source: "MM",
            hasToken: true,
            ac: [15],
            hp: { average: 7 },
          },
        ],
      });
    default:
      throw new Error(`Unexpected catalogue request: ${url.pathname}`);
  }
};
