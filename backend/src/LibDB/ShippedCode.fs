/// What this build ships, so the store's copy of it is checked before it runs.
///
/// The store holds the CLI's own Dark code (`Darklang.Cli.*`, the stdlib) beside the code a person
/// or an agent writes, and the CLI runs its own code with every permission. Anything that can write
/// the store, an agent inside `dark agent run` included, could rewrite one of those rows, and the
/// next `dark` outside would run it as the person. So a release build carries a digest of every
/// item its seed ships (`shipped-digests.bin`, made by `Seed.export`), and every load of one of
/// those items from the store is checked against it first: a copy that differs is refused.
///
/// What is digested is what runs: a function's instructions, a type's and a value's runtime form,
/// an impl's definition (which names the functions its methods run). The fold that builds those
/// rows is deterministic, so a store grown from the seed holds exactly the bytes the build
/// digested. Items the build does not ship (anything authored or pulled here) are not checked.
///
/// A debug build embeds no digests, and checks nothing.
module LibDB.ShippedCode

open Prelude

open Microsoft.Data.Sqlite

/// The four kinds of row checked, each the byte its digest records start with.
type Kind =
  | Fn
  | Type
  | Value
  | Impl

let private tag (kind : Kind) : byte =
  match kind with
  | Fn -> byte 'f'
  | Type -> byte 't'
  | Value -> byte 'v'
  | Impl -> byte 'i'

let private describe (kind : Kind) : string =
  match kind with
  | Fn -> "function"
  | Type -> "type"
  | Value -> "value"
  | Impl -> "impl"

/// A record: the kind's tag, the first 16 bytes of the item's hash, and the first 16 bytes of
/// the SHA-256 of its row. 128 bits each way: plenty to find the item and to tell its bytes apart.
let private keySize = 17
let private recordSize = 33

/// The digests a build ships: records sorted by key.
type Digests = private Digests of byte[]

/// The record key for an item, or None for a hash that is not 64 hex digits (no item a build
/// ships has one).
let private keyOf (kind : Kind) (hash : string) : Option<byte[]> =
  if hash.Length <> 64 then
    None
  else
    try
      let key = Array.zeroCreate<byte> keySize
      key[0] <- tag kind
      System.Convert.FromHexString(hash.Substring(0, 32)).CopyTo(key, 1)
      Some key
    with :? System.FormatException ->
      None

let private digestOf (bytes : byte[]) : byte[] =
  Array.sub (System.Security.Cryptography.SHA256.HashData bytes) 0 16

/// The digests of <param rows> (kind, item hash, row bytes), sorted, as a build embeds them.
let ofRows (rows : seq<Kind * string * byte[]>) : byte[] =
  let records =
    rows
    |> Seq.choose (fun (kind, hash, bytes) ->
      keyOf kind hash |> Option.map (fun key -> Array.append key (digestOf bytes)))
    |> Seq.toArray
  // Byte order, which is what the search below compares in.
  let sorted =
    records
    |> Array.sortWith (fun (a : byte[]) b ->
      System.MemoryExtensions.SequenceCompareTo(
        System.ReadOnlySpan(a, 0, keySize),
        System.ReadOnlySpan(b, 0, keySize)
      ))
  Array.concat sorted

/// The digests of what the store at <param dbPath> ships: every item a committed name on main
/// has held. Read before `Seed.export` empties the projections, from the store the seed is made
/// of. Bindings in a draft or on a branch are not shipped, and neither is their content.
let ofStore (dbPath : string) : byte[] =
  use conn = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly")
  conn.Open()
  use cmd = conn.CreateCommand()
  cmd.CommandText <-
    "WITH shipped AS
       (SELECT DISTINCT item_hash FROM locations
        WHERE branch_id = $main AND draft = 0 AND item_hash <> '')
     SELECT 'f', hash, rt_instrs FROM package_functions WHERE hash IN shipped
     UNION ALL SELECT 't', hash, rt_def FROM package_types WHERE hash IN shipped
     UNION ALL SELECT 'v', hash, rt_dval FROM package_values
       WHERE hash IN shipped AND rt_dval IS NOT NULL
     UNION ALL SELECT 'i', hash, pt_def FROM package_trait_impls WHERE hash IN shipped"
  cmd.Parameters.AddWithValue(
    "$main",
    string LibExecution.ProgramTypes.BranchId.Main
  )
  |> ignore<SqliteParameter>
  use reader = cmd.ExecuteReader()
  let rows =
    [ while reader.Read() do
        let kind =
          match reader.GetString 0 with
          | "f" -> Fn
          | "t" -> Type
          | "v" -> Value
          | _ -> Impl
        yield (kind, reader.GetString 1, reader.GetFieldValue<byte[]> 2) ]
  ofRows rows

/// <param bytes> as digests, or None when they are not a whole number of records.
let parse (bytes : byte[]) : Option<Digests> =
  if bytes.Length % recordSize = 0 then Some(Digests bytes) else None

type Check =
  /// The build ships this item and the store's copy is the one it shipped.
  | Matches
  /// The build ships this item and the store's copy is not the one it shipped.
  | Differs
  /// The build does not ship this item.
  | NotShipped

/// Whether <param bytes>, the store's row for the <param kind> at <param hash>, is the copy the
/// build shipped. A binary search over the records; no allocation beyond the key and the digest.
let check (Digests records) (kind : Kind) (hash : string) (bytes : byte[]) : Check =
  match keyOf kind hash with
  | None -> NotShipped
  | Some key ->
    let rec search (lo : int) (hi : int) : Option<int> =
      if lo > hi then
        None
      else
        let mid = lo + (hi - lo) / 2
        let order =
          System.MemoryExtensions.SequenceCompareTo(
            System.ReadOnlySpan(records, mid * recordSize, keySize),
            System.ReadOnlySpan key
          )
        if order = 0 then Some mid
        elif order < 0 then search (mid + 1) hi
        else search lo (mid - 1)
    match search 0 (records.Length / recordSize - 1) with
    | None -> NotShipped
    | Some i ->
      if
        System.MemoryExtensions.SequenceEqual(
          System.ReadOnlySpan(
            records,
            i * recordSize + keySize,
            recordSize - keySize
          ),
          System.ReadOnlySpan(digestOf bytes)
        )
      then
        Matches
      else
        Differs

/// The digests this binary embeds, or None in a build that embeds none.
let shipped : Lazy<Option<Digests>> =
  lazy
    (try
      match System.Reflection.Assembly.GetEntryAssembly() with
      | null -> None
      | entry ->
        use stream = entry.GetManifestResourceStream "shipped-digests.bin"
        if isNull stream then
          None
        else
          use buffer = new System.IO.MemoryStream()
          stream.CopyTo buffer
          parse (buffer.ToArray())
     with _ ->
       None)

/// The name the store gives <param hash>, for the refusal. The store is what is in doubt, so this
/// is a label and nothing more.
let private nameOf (hash : string) : string =
  try
    use conn =
      new SqliteConnection($"Data Source={Sqlite.currentDbPath};Mode=ReadOnly")
    conn.Open()
    use cmd = conn.CreateCommand()
    cmd.CommandText <-
      "SELECT owner || '.' || modules || '.' || name FROM locations
       WHERE item_hash = $hash AND live = 1 LIMIT 1"
    cmd.Parameters.AddWithValue("$hash", hash) |> ignore<SqliteParameter>
    match cmd.ExecuteScalar() with
    | :? string as name -> name
    | _ -> hash
  with _ ->
    hash

/// The sentence a refused item stops the command with.
let refusal (kind : Kind) (name : string) : string =
  let dbPath = Sqlite.currentDbPath
  $"dark will not run the {describe kind} {name}: the store's copy is not the one this dark "
  + "ships, so something changed the store outside dark.\n"
  + $"  To go back: stop every dark, then copy a backup from {StoreBackups.dirFor dbPath} over "
  + $"{dbPath}\n"
  + $"  Or start a fresh store: move {dbPath} aside and run dark again."

/// Whether a fold may write <param bytes> over an existing row for the <param kind> at <param hash>:
/// not when this build ships that item and these are not the bytes it shipped.
let mayStore (kind : Kind) (hash : string) (bytes : byte[]) : bool =
  match shipped.Force() with
  | None -> true
  | Some digests -> check digests kind hash bytes <> Differs

/// Stop if <param bytes> is not the copy this build shipped of the <param kind> at <param hash>.
/// Every load of a package item's runtime form goes through here.
let verify (kind : Kind) (hash : string) (bytes : byte[]) : unit =
  match shipped.Force() with
  | None -> ()
  | Some digests ->
    match check digests kind hash bytes with
    | Matches
    | NotShipped -> ()
    | Differs ->
      Exception.raiseStoreCondition
        (refusal kind (nameOf hash))
        [ "kind", describe kind; "hash", hash ]
