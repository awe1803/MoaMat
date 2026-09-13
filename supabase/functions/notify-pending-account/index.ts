// =============================================================================
//  MoaMat - Edge Function: push notification "new pending account"
// =============================================================================
//
//  Called by the database trigger notifier_compte_en_attente (db/notifications.sql)
//  through pg_net whenever a sign-up creates an "en_attente" account. It sends a
//  Web Push notification to every browser subscribed by an account that is
//  allowed to approve sign-ups (permission "role.assign").
//
//  Authentication: the caller is the database, not a user, so there is no JWT
//  (verify_jwt = false in supabase/config.toml). The request must carry the
//  shared secret PUSH_WEBHOOK_SECRET in the x-moamat-webhook-secret header; the
//  same value is stored in Supabase Vault on the database side.
//
//  Nothing in the body is trusted beyond the user id: the account is re-read
//  with the service_role key, and nothing is sent unless it is still pending.
//  Recipients come from public.destinataires_push_compte_en_attente(), which
//  re-checks the permission at send time.
//
//  Expired subscriptions (push service answers 404 / 410) are deleted.
//
//  Secrets (supabase secrets set ...):
//    PUSH_WEBHOOK_SECRET   shared with the database trigger (Vault)
//    VAPID_PUBLIC_KEY      base64url, also shipped to the client (Push:VapidPublicKey)
//    VAPID_PRIVATE_KEY     base64url, NEVER shipped to the client
//    VAPID_SUBJECT         contact for the push services, e.g. mailto:materiel@royalmoana.be
//
//  Input  (POST, JSON): { "user_id": "<uuid>" }
//  Output (JSON):       { ok, sent, expired, failed } or { ok, skipped }
// =============================================================================

import { createClient } from "jsr:@supabase/supabase-js@2";
import webpush from "npm:web-push@3.6.7";

// Self-contained on purpose (no ../_shared import): the function can be pasted
// as a single file in the Supabase Dashboard editor. It is only ever called by
// the database, never by a browser, so it needs no CORS headers.
function jsonResponse(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const WEBHOOK_SECRET = Deno.env.get("PUSH_WEBHOOK_SECRET") ?? "";
const VAPID_PUBLIC_KEY = Deno.env.get("VAPID_PUBLIC_KEY") ?? "";
const VAPID_PRIVATE_KEY = Deno.env.get("VAPID_PRIVATE_KEY") ?? "";
const VAPID_SUBJECT = Deno.env.get("VAPID_SUBJECT") ?? "";

const PENDING = "en_attente";

/** Screen opened by a click on the notification, relative to the app scope. */
const TARGET_URL = "comptes?role=en_attente";

/** How long a push service keeps an undelivered notification (seconds). */
const TIME_TO_LIVE = 24 * 60 * 60;

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type Recipient = { endpoint: string; p256dh: string; auth: string };

/**
 * Compares two secrets in constant time. Both sides are hashed first so the
 * comparison length never depends on the attacker-supplied value.
 */
async function secretsMatch(expected: string, received: string): Promise<boolean> {
  const encoder = new TextEncoder();
  const [a, b] = await Promise.all([
    crypto.subtle.digest("SHA-256", encoder.encode(expected)),
    crypto.subtle.digest("SHA-256", encoder.encode(received)),
  ]);
  const left = new Uint8Array(a);
  const right = new Uint8Array(b);
  let difference = 0;
  for (let i = 0; i < left.length; i++) {
    difference |= left[i] ^ right[i];
  }
  return difference === 0;
}

Deno.serve(async (req) => {
  if (req.method !== "POST") {
    return jsonResponse({ ok: false, error: "Méthode non autorisée." }, 405);
  }

  if (!WEBHOOK_SECRET || !VAPID_PUBLIC_KEY || !VAPID_PRIVATE_KEY || !VAPID_SUBJECT) {
    console.error("notify-pending-account: missing PUSH_WEBHOOK_SECRET or VAPID_* secret");
    return jsonResponse({ ok: false, error: "Fonction non configurée." }, 500);
  }

  // --- 1. Authenticate the database trigger --------------------------------
  const received = req.headers.get("x-moamat-webhook-secret") ?? "";
  if (!(await secretsMatch(WEBHOOK_SECRET, received))) {
    return jsonResponse({ ok: false, error: "Appelant non autorisé." }, 401);
  }

  let body: { user_id?: string };
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ ok: false, error: "Corps JSON invalide." }, 400);
  }

  const userId = body.user_id?.trim();
  if (!userId || !UUID_PATTERN.test(userId)) {
    return jsonResponse({ ok: false, error: "user_id n'est pas un UUID valide." }, 400);
  }

  const asAdmin = createClient(SUPABASE_URL, SERVICE_ROLE_KEY, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  // --- 2. Re-read the account: still pending? ------------------------------
  const { data: roleRow, error: roleErr } = await asAdmin
    .from("utilisateur_role")
    .select("role")
    .eq("user_id", userId)
    .maybeSingle();
  if (roleErr) {
    return jsonResponse({ ok: false, error: "Lecture du rôle impossible." }, 500);
  }
  if (roleRow?.role !== PENDING) {
    // Already approved (or deleted) before the notification went out.
    return jsonResponse({ ok: true, skipped: "Compte plus en attente." });
  }

  const { data: userData, error: userErr } = await asAdmin.auth.admin.getUserById(userId);
  if (userErr || !userData?.user) {
    return jsonResponse({ ok: true, skipped: "Compte introuvable." });
  }
  const email = userData.user.email ?? "Un nouveau membre";

  // --- 3. Recipients: accounts that may approve sign-ups -------------------
  const { data: recipients, error: recipientsErr } = await asAdmin
    .rpc("destinataires_push_compte_en_attente");
  if (recipientsErr) {
    return jsonResponse({ ok: false, error: "Lecture des destinataires impossible." }, 500);
  }

  const list = (recipients ?? []) as Recipient[];
  if (list.length === 0) {
    return jsonResponse({ ok: true, sent: 0, expired: 0, failed: 0 });
  }

  // --- 4. Send -------------------------------------------------------------
  webpush.setVapidDetails(VAPID_SUBJECT, VAPID_PUBLIC_KEY, VAPID_PRIVATE_KEY);

  // The payload is encrypted end to end for each browser: the push service
  // only relays ciphertext, so carrying the e-mail address is acceptable.
  const payload = JSON.stringify({
    title: "Nouveau compte en attente",
    body: `${email} demande un accès à MoaMat.`,
    url: TARGET_URL,
    tag: `compte-en-attente-${userId}`,
  });

  const results = await Promise.allSettled(
    list.map((recipient) =>
      webpush.sendNotification(
        { endpoint: recipient.endpoint, keys: { p256dh: recipient.p256dh, auth: recipient.auth } },
        payload,
        { TTL: TIME_TO_LIVE, urgency: "normal" },
      )
    ),
  );

  const expiredEndpoints: string[] = [];
  let sent = 0;
  let failed = 0;

  results.forEach((result, index) => {
    if (result.status === "fulfilled") {
      sent++;
      return;
    }
    const statusCode = (result.reason as { statusCode?: number })?.statusCode;
    if (statusCode === 404 || statusCode === 410) {
      expiredEndpoints.push(list[index].endpoint);
    } else {
      failed++;
      console.error("notify-pending-account: push failed", {
        statusCode: statusCode ?? null,
        message: (result.reason as Error)?.message ?? String(result.reason),
      });
    }
  });

  // --- 5. Forget subscriptions the push service no longer knows -----------
  if (expiredEndpoints.length > 0) {
    const { error: purgeErr } = await asAdmin
      .from("abonnement_push")
      .delete()
      .in("endpoint", expiredEndpoints);
    if (purgeErr) {
      console.error("notify-pending-account: expired subscription purge failed", purgeErr.message);
    }
  }

  return jsonResponse({ ok: true, sent, expired: expiredEndpoints.length, failed });
});
