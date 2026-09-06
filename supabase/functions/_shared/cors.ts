// En-têtes CORS communs aux Edge Functions MoaMat.
// Le front (Blazor WASM) appelle les fonctions depuis une autre origine
// (GitHub Pages / localhost) : le préflight OPTIONS doit répondre 2xx.

export const corsHeaders = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers":
    "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
} as const;

export function jsonResponse(
  body: unknown,
  status = 200,
): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { ...corsHeaders, "Content-Type": "application/json" },
  });
}
