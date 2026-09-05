<#
.SYNOPSIS
    Regenere db/initial_load.sql a partir des CSV de Access_Data/csv/.

.DESCRIPTION
    Reprise des donnees de l'ancienne base Access vers le schema defini dans
    db/schema.sql. Le .sql produit est autonome (INSERT ... VALUES) et
    re-executable : il commence par un TRUNCATE ... RESTART IDENTITY CASCADE et
    se termine par le recalage des sequences d'identite.

    Nettoyage applique a l'ecriture (le .sql ne contient que des litteraux
    propres) :
      * chaine vide               -> NULL
      * colonnes date             -> parse MM/dd/yy ; sentinelles Access
                                     (12/30/99, 30/12/1899, annee <= 1900) -> NULL
      * colonnes booleennes       -> 0/1 -> false/true ; autre -> NULL (+ warning)
      * colonnes numeriques       -> virgule -> point ; non numerique -> NULL
      * texte                     -> echappement des apostrophes

    Les 8 tables zz_ et la colonne utilisateur.mot_de_passe_a_ne_pas_migrer
    ne sont volontairement pas reprises.

.NOTES
    PowerShell 5.1+. Executer depuis n'importe quel dossier :
        pwsh ./tools/Generate-InitialLoad.ps1
#>

[CmdletBinding()]
param(
    [string] $CsvDir  = (Join-Path $PSScriptRoot '..\Access_Data\csv'),
    [string] $OutFile = (Join-Path $PSScriptRoot '..\db\initial_load.sql'),
    [int]    $BatchSize = 200
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --- Ordre de chargement : referentiels -> parents -> enfants -> reste --------
$Tables = @(
    'ref_action_compresseur','ref_gaz','ref_local_didactique','ref_regle_requalification',
    'ref_site','ref_site_web','ref_tarif_requalification',
    'fournisseur','personne','utilisateur',
    'bouteille','bouteille_sortie_inventaire','bouteille_requalification','bouteille_membre',
    'detendeur','detendeur_sortie_inventaire','detendeur_intervention',
    'gilet','gilet_sortie_inventaire','petit_materiel','materiel_didactique','piece_detachee',
    'compresseur_releve',
    'achat_facture','achat_reception','achat_2022_non_integre','devis_ligne',
    'pret'
)

# Cle primaire par table (defaut : "id")
$PrimaryKey = @{ 'achat_reception' = 'id_achats'; 'ref_site_web' = 'num' }

# Colonnes exclues de la reprise
$ExcludedColumns = @{ 'utilisateur' = @('mot_de_passe_a_ne_pas_migrer') }

# --- Typage des colonnes (par nom, toutes tables confondues) -----------------
$DateCols = @(
    'date_mise_en_service','date_echeance','date_declassement','date_rendez_vous',
    'date_derniere_epreuve','date_operation','date_echeance_suivante','date_dernier_controle',
    'date_achat','date_intervention','date_releve','date_facture','date_pret',
    'date_retour_prevue','date_retour_reelle','date_reepreuve_initiale','date_creation'
)
$BoolCols = @(
    'controle_effectue','double_sortie','sortie_autorisee','a_controler','a_requalifier',
    'est_declassee','est_declasse','est_manquant','a_premier_etage','a_second_etage',
    'a_octopus','a_inflateur','a_manometre','reserve_enfants','flag_aujourdhui','est_cloture'
)
$NumericCols = @(
    'cout_eur','montant_eur','montant_tvac_eur','prix_unitaire_eur','prix_tvac_eur',
    'prix_individuel','prix_unitaire','tarif_eur','indexation_pct','stock','quantite'
)
$IntCols     = @('qu_achetee','qu_recue','qu_a_recevoir','annee')
$BigIntCols  = @(
    'bouteille_id','detendeur_id','gaz_id','type_derniere_epreuve_id','type_echeance_suivante_id',
    'tarif_id','action_id','local_id','site_id','piece_jointe_id','montant_htva_eur_corrompu'
)

$script:Warnings = New-Object System.Collections.Generic.List[string]

# Calendrier : "72" -> 1972, "28" -> 2028, "45" -> 2045
$Ci = [System.Globalization.CultureInfo]::InvariantCulture.Clone()
$Ci.Calendar.TwoDigitYearMax = 2049
[string[]] $DateFormats = @('M/d/yy H:mm:ss','M/d/yy HH:mm:ss','M/d/yyyy H:mm:ss','M/d/yyyy HH:mm:ss','M/d/yy','M/d/yyyy')

function Get-ColumnType([string] $name) {
    if ($DateCols    -contains $name) { return 'date' }
    if ($BoolCols    -contains $name) { return 'bool' }
    if ($NumericCols -contains $name) { return 'numeric' }
    if ($IntCols     -contains $name) { return 'int' }
    if ($BigIntCols  -contains $name) { return 'int' }
    return 'text'
}

function Format-SqlText([string] $v) {
    if ([string]::IsNullOrEmpty($v)) { return 'NULL' }
    return "'" + $v.Replace("'", "''") + "'"
}

function Format-SqlBool([string] $v, [string] $ctx) {
    $t = $v.Trim()
    if ($t -eq '')  { return 'NULL' }
    if ($t -eq '0') { return 'false' }
    if ($t -eq '1') { return 'true' }
    $script:Warnings.Add("bool inattendu -> NULL : $ctx = '$v'")
    return 'NULL'
}

function Format-SqlNumeric([string] $v, [string] $ctx) {
    $t = $v.Trim()
    if ($t -eq '') { return 'NULL' }
    $t = $t.Replace(' ', '').Replace(',', '.')
    if ($t -match '^-?\d+(\.\d+)?$') { return $t }
    $script:Warnings.Add("numerique invalide -> NULL : $ctx = '$v'")
    return 'NULL'
}

function Format-SqlInt([string] $v, [string] $ctx) {
    $t = $v.Trim()
    if ($t -eq '') { return 'NULL' }
    if ($t -match '^-?\d+$')       { return $t }
    if ($t -match '^(-?\d+)\.0+$') { return $Matches[1] }
    $script:Warnings.Add("entier invalide -> NULL : $ctx = '$v'")
    return 'NULL'
}

function Format-SqlDate([string] $v, [string] $ctx) {
    $t = $v.Trim()
    if ($t -eq '') { return 'NULL' }
    if ($t.StartsWith('12/30/99') -or $t.StartsWith('30/12/1899') -or $t.StartsWith('1/0/')) { return 'NULL' }
    $dt = [datetime]::MinValue
    if ([datetime]::TryParseExact($t, $DateFormats, $Ci,
            [System.Globalization.DateTimeStyles]::None, [ref] $dt)) {
        if ($dt.Year -le 1900) { return 'NULL' }
        return "'" + $dt.ToString('yyyy-MM-dd') + "'"
    }
    $script:Warnings.Add("date non reconnue -> NULL : $ctx = '$v'")
    return 'NULL'
}

function Format-Value([string] $col, [string] $val, [string] $table) {
    $ctx = "$table.$col"
    switch (Get-ColumnType $col) {
        'date'    { return (Format-SqlDate    $val $ctx) }
        'bool'    { return (Format-SqlBool    $val $ctx) }
        'numeric' { return (Format-SqlNumeric $val $ctx) }
        'int'     { return (Format-SqlInt     $val $ctx) }
        default   { return (Format-SqlText    $val) }
    }
}

# --- Generation ------------------------------------------------------------------
$nl = "`n"
$sb = New-Object System.Text.StringBuilder
[void] $sb.Append('-- =============================================================================' + $nl)
[void] $sb.Append('--  MoaMat - Reprise initiale des donnees (Access -> Supabase / PostgreSQL)' + $nl)
[void] $sb.Append('--  FICHIER GENERE par tools/Generate-InitialLoad.ps1 - NE PAS EDITER A LA MAIN.' + $nl)
[void] $sb.Append("--  Genere le $((Get-Date).ToString('yyyy-MM-dd HH:mm')) - a executer APRES db/schema.sql." + $nl)
[void] $sb.Append('-- =============================================================================' + $nl + $nl)
[void] $sb.Append('begin;' + $nl + $nl)
[void] $sb.Append('truncate table' + $nl)
[void] $sb.Append('    ' + ($Tables -join (',' + $nl + '    ')) + $nl)
[void] $sb.Append('    restart identity cascade;' + $nl + $nl)

$summary = @()

foreach ($table in $Tables) {
    $csvPath = Join-Path $CsvDir "$table.csv"
    if (-not (Test-Path $csvPath)) { throw "CSV introuvable : $csvPath" }

    $rows = @(Import-Csv -Path $csvPath -Encoding UTF8)
    $summary += [pscustomobject]@{ Table = $table; Lignes = $rows.Count }

    [void] $sb.Append("-- $table ($($rows.Count) lignes)" + $nl)
    if ($rows.Count -eq 0) { [void] $sb.Append($nl); continue }

    $allCols = @($rows[0].psobject.Properties.Name)
    $excluded = @()
    if ($ExcludedColumns.ContainsKey($table)) { $excluded = $ExcludedColumns[$table] }
    $cols = @($allCols | Where-Object { $excluded -notcontains $_ })
    $colList = $cols -join ', '

    for ($i = 0; $i -lt $rows.Count; $i += $BatchSize) {
        $chunk = $rows[$i..([math]::Min($i + $BatchSize - 1, $rows.Count - 1))]
        [void] $sb.Append("insert into $table ($colList) values" + $nl)
        $lines = foreach ($row in $chunk) {
            $vals = foreach ($c in $cols) { Format-Value $c ([string] $row.$c) $table }
            '    (' + ($vals -join ', ') + ')'
        }
        [void] $sb.Append(($lines -join (',' + $nl)) + ';' + $nl)
    }
    [void] $sb.Append($nl)
}

# --- Recalage des sequences d'identite ---------------------------------------
[void] $sb.Append('-- Recalage des sequences d''identite sur le max(id) reinjecte' + $nl)
foreach ($table in $Tables) {
    $pk = 'id'
    if ($PrimaryKey.ContainsKey($table)) { $pk = $PrimaryKey[$table] }
    [void] $sb.Append("select setval(pg_get_serial_sequence('$table', '$pk'), (select coalesce(max($pk), 1) from $table), true);" + $nl)
}
[void] $sb.Append($nl + 'commit;' + $nl)

# --- Ecriture (UTF-8 sans BOM) ------------------------------------------------
$dir = Split-Path -Parent $OutFile
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($false)))

Write-Host ""
Write-Host "initial_load.sql genere : $OutFile"
$summary | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("Total lignes : {0}" -f (($summary | Measure-Object -Property Lignes -Sum).Sum))

if ($script:Warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "$($script:Warnings.Count) avertissement(s) de conversion :" -ForegroundColor Yellow
    $script:Warnings | Group-Object | Sort-Object Count -Descending |
        ForEach-Object { Write-Host ("  x{0,-4} {1}" -f $_.Count, $_.Name) -ForegroundColor Yellow }
}
