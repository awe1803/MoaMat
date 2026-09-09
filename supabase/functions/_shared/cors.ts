// Shared CORS headers for the MoaMat Edge Functions.
//
// The front end (Blazor WASM) calls the functions from a different origin
// (GitHub Pages, or localhost during development), so the OPTIONS preflight has
// to answer 2xx.
//
// A wildcard origin is acceptable here because these functions authenticate the
// caller with a bearer token, never with a cookie: a third-party page can issue
// the request but has no way to obtain a valid token for someone else. Do not
// add `Access-Control-Allow-Credentials` without narrowing the origin first.

export const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
} as const;

/** Builds a JSON response carrying the shared CORS headers. */
export function jsonResponse(
  body: unknown,
  status = 200,
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}
