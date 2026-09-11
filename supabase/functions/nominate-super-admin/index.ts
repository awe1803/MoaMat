// =============================================================================
//  MoaMat - reference Edge Function: nominate / transfer the super-admin seat
// =============================================================================
//
//  The "super-admin" role is VACANT at initialisation. It is never granted by
//  default nor hard-coded. There are two ways to fill the seat:
//
//    1. Bootstrap: the SQL statement documented at the bottom of db/roles.sql,
//       run once by whoever administers the Supabase project.
//    2. Afterwards: THIS function, to nominate an additional super-admin or to
//       transfer the seat.
//
//  Authorisation rules enforced here:
//    * the caller must be authenticated (valid JWT in Authorization);
//    * if a super-admin already exists -> only a super-admin may call;
//    * if the seat is vacant           -> an admin may perform the first
//      nomination (safety net when the SQL bootstrap was not run).
//
//  The function uses the service_role key (injected by the platform, never
//  committed) to write into public.utilisateur_role and to mirror the role into
//  the target user's app_metadata. Every nomination is recorded in
//  public.audit_log through the public.audit_write() RPC.
//
//  Idempotence: nominating someone who is already super-admin is a no-op that
//  still answers 200. The three writes below (role, claim mirror, audit) are not
//  a transaction, so each one is checked and the response reports exactly which
//  of them succeeded - a partial state must be visible, never silent.
//
//  Input  (POST, JSON):  { "target_user_id": "<uuid>" }
//                    or: { "target_email": "member@club.be" }
//               option:  { "reason": "free text" }
//
//  Output (JSON): { ok, target_user_id, previous_role, seat_was_vacant,
//                   claim_mirrored, audited }
// =============================================================================

import { createClient } from "jsr:@supabase/supabase-js@2";
import { corsHeaders, jsonResponse } from "../_shared/cors.ts";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;

const SUPER_ADMIN = "super-admin";

/** Page size and page cap used when looking a user up by e-mail. */
const USER_PAGE_SIZE = 1000;
const MAX_USER_PAGES = 50;

const UUID_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type AdminClient = ReturnType<typeof createClient>;

/**
 * Finds a user by e-mail address.
 *
 * listUsers() is paginated and returns only the first page by default, so a
 * single call silently fails to find anyone past the first page. This walks the
 * pages until a match is found or the list is exhausted.
 */
async function findUserIdByEmail(
  asAdmin: AdminClient,
  email: string,
): Promise<{ id?: string; failed?: boolean }> {
  const needle = email.trim().toLowerCase();

  for (let page = 1; page <= MAX_USER_PAGES; page++) {
    const { data, error } = await asAdmin.auth.admin.listUsers({
      page,
      perPage: USER_PAGE_SIZE,
    });

    if (error) {
      return { failed: true };
    }

    const match = data.users.find((u) => u.email?.toLowerCase() === needle);
    if (match) {
      return { id: match.id };
    }

    if (data.users.length < USER_PAGE_SIZE) {
      return {};
    }
  }

  return {};
}

Deno.serve(async (req) => {
  if (req.method === "OPTIONS") {
    return new Response("ok", { headers: corsHeaders });
  }
  if (req.method !== "POST") {
    return jsonResponse({ ok: false, error: "Méthode non autorisée." }, 405);
  }

  const authHeader = req.headers.get("Authorization") ?? "";
  if (!authHeader.toLowerCase().startsWith("bearer ")) {
    return jsonResponse({ ok: false, error: "Jeton d'authentification manquant." }, 401);
  }

  // "Caller" client: used only to identify who is calling.
  const asCaller = createClient(SUPABASE_URL, ANON_KEY, {
    global: { headers: { Authorization: authHeader } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  // Privileged client: writes bypassing RLS.
  const asAdmin = createClient(SUPABASE_URL, SERVICE_ROLE_KEY, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  // --- 1. Identify the caller ----------------------------------------------
  const { data: callerData, error: callerErr } = await asCaller.auth.getUser();
  if (callerErr || !callerData?.user) {
    return jsonResponse({ ok: false, error: "Session invalide." }, 401);
  }
  const caller = callerData.user;

  const { data: callerRoleRow, error: callerRoleErr } = await asAdmin
    .from("utilisateur_role")
    .select("role")
    .eq("user_id", caller.id)
    .maybeSingle();
  if (callerRoleErr) {
    return jsonResponse({ ok: false, error: "Lecture du rôle appelant impossible." }, 500);
  }
  const callerRole = callerRoleRow?.role ?? null;

  // --- 2. Is the super-admin seat vacant? ----------------------------------
  const { count: superAdminCount, error: countErr } = await asAdmin
    .from("utilisateur_role")
    .select("user_id", { count: "exact", head: true })
    .eq("role", SUPER_ADMIN);
  if (countErr) {
    return jsonResponse({ ok: false, error: "Lecture des rôles impossible." }, 500);
  }
  const seatWasVacant = (superAdminCount ?? 0) === 0;

  const callerMayNominate = seatWasVacant
    ? callerRole === "admin" || callerRole === SUPER_ADMIN
    : callerRole === SUPER_ADMIN;

  if (!callerMayNominate) {
    return jsonResponse({
      ok: false,
      error: seatWasVacant
        ? "Siège super-admin vacant : seule une personne « admin » ou « super-admin » peut réaliser la première nomination."
        : "Un super-admin existe déjà : seul un super-admin peut en nommer ou transférer le siège.",
    }, 403);
  }

  // --- 3. Resolve the target user ------------------------------------------
  let body: { target_user_id?: string; target_email?: string; reason?: string };
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ ok: false, error: "Corps JSON invalide." }, 400);
  }

  let targetUserId = body.target_user_id?.trim();

  if (targetUserId && !UUID_PATTERN.test(targetUserId)) {
    return jsonResponse({ ok: false, error: "target_user_id n'est pas un UUID valide." }, 400);
  }

  if (!targetUserId && body.target_email) {
    const lookup = await findUserIdByEmail(asAdmin, body.target_email);
    if (lookup.failed) {
      return jsonResponse({ ok: false, error: "Recherche par e-mail impossible." }, 500);
    }
    targetUserId = lookup.id;
  }

  if (!targetUserId) {
    return jsonResponse({
      ok: false,
      error: "Utilisateur cible introuvable (target_user_id / target_email).",
    }, 404);
  }

  const { data: targetUser, error: targetErr } = await asAdmin.auth.admin.getUserById(targetUserId);
  if (targetErr || !targetUser?.user) {
    return jsonResponse({ ok: false, error: "Utilisateur cible inexistant." }, 404);
  }

  // --- 4. Previous role of the target --------------------------------------
  const { data: prevRow, error: prevErr } = await asAdmin
    .from("utilisateur_role")
    .select("role")
    .eq("user_id", targetUserId)
    .maybeSingle();
  if (prevErr) {
    return jsonResponse({ ok: false, error: "Lecture du rôle cible impossible." }, 500);
  }
  const previousRole = prevRow?.role ?? null;

  // Idempotent replay: nothing to change, and nothing new to audit.
  if (previousRole === SUPER_ADMIN) {
    return jsonResponse({
      ok: true,
      target_user_id: targetUserId,
      previous_role: previousRole,
      seat_was_vacant: seatWasVacant,
      claim_mirrored: true,
      audited: true,
      note: "Déjà super-admin, aucune modification.",
    });
  }

  // --- 5. Write the role (the table is the source of truth) ----------------
  const { error: upsertErr } = await asAdmin
    .from("utilisateur_role")
    .upsert(
      {
        user_id: targetUserId,
        role: SUPER_ADMIN,
        assigned_by: caller.id,
        updated_at: new Date().toISOString(),
      },
      { onConflict: "user_id" },
    );
  if (upsertErr) {
    return jsonResponse({
      ok: false,
      error: `Écriture du rôle impossible : ${upsertErr.message}`,
    }, 500);
  }

  // --- 6. Mirror into the JWT (app_metadata.role) for the client -----------
  // The role is already granted at this point: the table is the authority the
  // RLS policies read. A failure here only means the claim lags until the next
  // token refresh, so it is reported rather than turned into a 500 that would
  // wrongly suggest the nomination did not happen.
  const { error: claimErr } = await asAdmin.auth.admin.updateUserById(targetUserId, {
    app_metadata: { ...(targetUser.user.app_metadata ?? {}), role: SUPER_ADMIN },
  });
  const claimMirrored = !claimErr;

  // --- 7. Audit trail ------------------------------------------------------
  // A privileged grant that goes unrecorded is exactly what an audit trail
  // exists to prevent, so the outcome is surfaced to the caller.
  const { error: auditErr } = await asAdmin.rpc("audit_write", {
    p_action: "superadmin.nominated",
    p_entity_table: "utilisateur_role",
    p_entity_id: targetUserId,
    p_before: previousRole ? { role: previousRole } : null,
    p_after: { role: SUPER_ADMIN },
    p_context: {
      via: "edge:nominate-super-admin",
      by: caller.id,
      by_email: caller.email ?? null,
      seat_was_vacant: seatWasVacant,
      reason: body.reason ?? null,
    },
  });
  const audited = !auditErr;

  if (!claimMirrored || !audited) {
    console.error("nominate-super-admin partial completion", {
      targetUserId,
      claimError: claimErr?.message ?? null,
      auditError: auditErr?.message ?? null,
    });
  }

  return jsonResponse({
    ok: true,
    target_user_id: targetUserId,
    previous_role: previousRole,
    seat_was_vacant: seatWasVacant,
    claim_mirrored: claimMirrored,
    audited,
  });
});
