// In development, always fetch from the network and do not enable offline support.
// This is because caching would make development more difficult (changes would not
// be reflected on the first load after each change).
//
// No 'fetch' handler is registered on purpose: an empty (no-op) handler still adds
// overhead to every navigation and browsers now warn about it. The published build
// (service-worker.published.js) is the one that wires up real offline caching.
