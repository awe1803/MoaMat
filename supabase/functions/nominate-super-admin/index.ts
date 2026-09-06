// =============================================================================
//  MoaMat — Edge Function de référence : nomination / transfert du Super-admin
// =============================================================================
//
//  Le rôle « super-admin » est VACANT à l'initialisation du système. Il n'est
//  jamais attribué par défaut ni codé en dur. Deux voies pour le pourvoir :
//
//    1. Amorçage : requête SQL documentée (db/roles.sql, bas de fichier),
//       exécutée une fois par la personne qui administre le projet Supabase.
//    2. Ensuite : CETTE fonction, pour nommer un super-admin supplémentaire ou
//       transférer le siège.
//
//  Règles d'autorisation appliquées ici :
//    * l'appelant doit être authentifié (JWT valide dans Authorization) ;
//    * si un super-admin existe déjà  -> seul un super-admin peut appeler ;
//    * si le siège est vacant         -> un admin peut réaliser la 1re
//      nomination (filet de sécurité si l'amorçage SQL n'a pas été fait).
//
//  La fonction utilise la clé service_role (injectée par la plateforme,
//  jamais commitée) pour écrire dans public.utilisateur_role et pour recopier
//  le rôle dans app_metadata de l'utilisateur cible. Chaque nomination est
//  journalisée dans public.audit_log via la RPC public.audit_write().
//
//  Entrée (POST, JSON) :  { "target_user_id": "<uuid>" }
//                    ou :  { "target_email": "membre@club.be" }
//                 option :  { "reason": "texte libre" }
//
//  Sortie (JSON) : { ok, target_user_id, previous_role, seat_was_vacant }
// =============================================================================

import { createClient } from "jsr:@supabase/supabase-js@2";
import { corsHeaders, jsonResponse } from "../_shared/cors.ts";

const SUPABASE_URL = Deno.env.get("SUPABASE_URL")!;
const SERVICE_ROLE_KEY = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!;
const ANON_KEY = Deno.env.get("SUPABASE_ANON_KEY")!;

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

  // Client « appelant » : sert uniquement à identifier qui appelle.
  const asCaller = createClient(SUPABASE_URL, ANON_KEY, {
    global: { headers: { Authorization: authHeader } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  // Client privilégié : écrit en contournant la RLS.
  const asAdmin = createClient(SUPABASE_URL, SERVICE_ROLE_KEY, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  // --- 1. Identifier l'appelant ---------------------------------------------
  const { data: callerData, error: callerErr } = await asCaller.auth.getUser();
  if (callerErr || !callerData?.user) {
    return jsonResponse({ ok: false, error: "Session invalide." }, 401);
  }
  const caller = callerData.user;

  const { data: callerRoleRow } = await asAdmin
    .from("utilisateur_role")
    .select("role")
    .eq("user_id", caller.id)
    .maybeSingle();
  const callerRole = callerRoleRow?.role ?? null;

  // --- 2. Le siège super-admin est-il vacant ? -----------------------------
  const { count: superAdminCount, error: countErr } = await asAdmin
    .from("utilisateur_role")
    .select("user_id", { count: "exact", head: true })
    .eq("role", "super-admin");
  if (countErr) {
    return jsonResponse({ ok: false, error: "Lecture des rôles impossible." }, 500);
  }
  const seatWasVacant = (superAdminCount ?? 0) === 0;

  const callerMayNominate = seatWasVacant
    ? callerRole === "admin" || callerRole === "super-admin"
    : callerRole === "super-admin";

  if (!callerMayNominate) {
    return jsonResponse({
      ok: false,
      error: seatWasVacant
        ? "Siège super-admin vacant : seule une personne « admin » ou « super-admin » peut réaliser la première nomination."
        : "Un super-admin existe déjà : seul un super-admin peut en nommer ou transférer le siège.",
    }, 403);
  }

  // --- 3. Résoudre l'utilisateur cible ------------------------------------
  let body: { target_user_id?: string; target_email?: string; reason?: string };
  try {
    body = await req.json();
  } catch {
    return jsonResponse({ ok: false, error: "Corps JSON invalide." }, 400);
  }

  let targetUserId = body.target_user_id?.trim();
  if (!targetUserId && body.target_email) {
    const { data: list, error: listErr } = await asAdmin.auth.admin.listUsers();
    if (listErr) {
      return jsonResponse({ ok: false, error: "Recherche par e-mail impossible." }, 500);
    }
    const match = list.users.find(
      (u) => u.email?.toLowerCase() === body.target_email!.trim().toLowerCase(),
    );
    targetUserId = match?.id;
  }
  if (!targetUserId) {
    return jsonResponse({ ok: false, error: "Utilisateur cible introuvable (target_user_id / target_email)." }, 404);
  }

  const { data: targetUser, error: targetErr } = await asAdmin.auth.admin.getUserById(targetUserId);
  if (targetErr || !targetUser?.user) {
    return jsonResponse({ ok: false, error: "Utilisateur cible inexistant." }, 404);
  }

  // --- 4. Rôle précédent de la cible -------------------------------------
  const { data: prevRow } = await asAdmin
    .from("utilisateur_role")
    .select("role")
    .eq("user_id", targetUserId)
    .maybeSingle();
  const previousRole = prevRow?.role ?? null;

  if (previousRole === "super-admin") {
    return jsonResponse({ ok: true, target_user_id: targetUserId, previous_role: previousRole, seat_was_vacant: seatWasVacant, note: "Déjà super-admin, aucune modification." });
  }

  // --- 5. Écrire le rôle (source de vérité : la table) -----------------
  const { error: upsertErr } = await asAdmin
    .from("utilisateur_role")
    .upsert(
      { user_id: targetUserId, role: "super-admin", assigned_by: caller.id, updated_at: new Date().toISOString() },
      { onConflict: "user_id" },
    );
  if (upsertErr) {
    return jsonResponse({ ok: false, error: `Écriture du rôle impossible : ${upsertErr.message}` }, 500);
  }

  // --- 6. Miroir dans le JWT (app_metadata.role) pour l'affichage client --
  await asAdmin.auth.admin.updateUserById(targetUserId, {
    app_metadata: { ...(targetUser.user.app_metadata ?? {}), role: "super-admin" },
  });

  // --- 7. Journal d'audit ---------------------------------------------
  await asAdmin.rpc("audit_write", {
    p_action: "superadmin.nominated",
    p_entity_table: "utilisateur_role",
    p_entity_id: targetUserId,
    p_before: previousRole ? { role: previousRole } : null,
    p_after: { role: "super-admin" },
    p_context: {
      via: "edge:nominate-super-admin",
      by: caller.id,
      by_email: caller.email ?? null,
      seat_was_vacant: seatWasVacant,
      reason: body.reason ?? null,
    },
  });

  return jsonResponse({
    ok: true,
    target_user_id: targetUserId,
    previous_role: previousRole,
    seat_was_vacant: seatWasVacant,
  });
});
