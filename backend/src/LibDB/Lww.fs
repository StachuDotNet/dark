/// The timestamp last-writer-wins staleness rule, in ONE place.
///
/// A candidate binding LOSES (is "stale") when its authoring stamp is older than the current binding's — or,
/// on an exact stamp tie, when its content hash is the lower of the two (a portable, instance-independent
/// tiebreak). The op-fold (`applySetName`), divergence detection (`Conflicts.detectDivergences`), and the
/// resolution overlay (`Resolutions.applyToLocations`) must ALL apply this identical rule; if any copy drifts,
/// two instances can pick different winners for the same name and silently diverge. Keeping it here makes that
/// impossible by construction.
///
/// Stamps are the portable `yyyy-MM-ddTHH:mm:ss.fffZ` strings; lexical `<` is chronological for that fixed
/// format, so no parsing is needed.
module LibDB.Lww

/// True iff binding (newTs, newHash) loses to the live binding (curTs, curHash) under timestamp-LWW.
let isStale (newTs : string) (newHash : string) (curTs : string) (curHash : string) : bool =
  newTs < curTs || (newTs = curTs && newHash < curHash)
