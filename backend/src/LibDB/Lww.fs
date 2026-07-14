/// The timestamp last-writer-wins staleness rule, in ONE place.
///
/// Distributed instances editing the same name must pick the SAME winner without coordinating. A candidate
/// is stale (loses) when its authoring stamp is older, or — on an exact tie — when its content hash is the
/// lower of the two (a portable, instance-independent tiebreak). The op-fold, divergence detection
/// (`Conflicts`), and the resolution overlay (`Resolutions`) all apply this one rule; keeping it here means
/// no copy can drift and make two instances diverge.
///
/// Stamps are `yyyy-MM-ddTHH:mm:ss.fffZ` strings, so lexical `<` is already chronological — no parsing.
module LibDB.Lww

/// True iff binding (newTs, newHash) loses to the live binding (curTs, curHash) under timestamp-LWW.
let isStale
  (newTs : string)
  (newHash : string)
  (curTs : string)
  (curHash : string)
  : bool =
  newTs < curTs || (newTs = curTs && newHash < curHash)
