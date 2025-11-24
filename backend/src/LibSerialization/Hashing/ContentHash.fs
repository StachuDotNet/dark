/// Content-addressed hashing utilities for Darklang
///
/// Provides deterministic hash generation by hashing serialized content.
/// Used for stable package item IDs based on content.
module LibSerialization.Hashing.ContentHash

open System
open System.IO
open System.Security.Cryptography

open Prelude

/// Compute SHA256 hash of bytes
let hashBytes (bytes : byte[]) : Hash =
  let hashBytes = SHA256.HashData(ReadOnlySpan bytes)
  Hash.ofBytes hashBytes


/// Serialize a value using a binary writer function, then hash it
///
/// This is the core pattern: whatever can be written to binary can be hashed.
/// The binary format is the source of truth for what makes something "the same".
let hashWithWriter<'T> (writer : BinaryWriter -> 'T -> unit) (value : 'T) : Hash =
  use ms = new MemoryStream()
  use bw = new BinaryWriter(ms)

  writer bw value

  let bytes = ms.ToArray()
  hashBytes bytes


/// Hash PackageLocations (deprecated - prefer content-based hashing)
module PackageLocation =
  open LibSerialization.Binary

  /// Compute a hash for a PackageLocation
  ///
  /// Note: Location-based hashing means IDs change when items move.
  /// This is kept for backwards compatibility but content-based hashing is preferred.
  let hash (loc : LibExecution.ProgramTypes.PackageLocation) : Hash =
    hashWithWriter Serializers.PT.Common.PackageLocation.write loc


/// Hash PackageOps (for deduplication)
module PackageOp =
  open LibSerialization.Binary

  /// Compute a content-addressed hash for a PackageOp
  ///
  /// This is used for deduplication - the same op content gets the same hash.
  /// Originally from PT/SQL/OpPlayback.fs, centralized here.
  let hash (op : LibExecution.ProgramTypes.PackageOp) : Hash =
    hashWithWriter Serializers.PT.PackageOp.write op


/// Hash PackageType definitions to stable content-based hashes
module PackageType =
  open LibSerialization.Binary

  /// Compute a content-addressed hash for a PackageType
  ///
  /// The hash is based on the complete type definition (name, fields, etc).
  /// The existing ID field is normalized to Hash.empty before hashing.
  let hash (typ : LibExecution.ProgramTypes.PackageType.PackageType) : Hash =
    let normalized = { typ with id = Hash.empty }
    hashWithWriter Serializers.PT.PackageType.write normalized


/// Hash PackageFn definitions to stable content-based hashes
module PackageFn =
  open LibSerialization.Binary

  /// Compute a content-addressed hash for a PackageFn
  ///
  /// The hash is based on the complete function definition (signature, body, etc).
  /// The existing ID field is normalized to Hash.empty before hashing.
  let hash (fn : LibExecution.ProgramTypes.PackageFn.PackageFn) : Hash =
    let normalized = { fn with id = Hash.empty }
    hashWithWriter Serializers.PT.PackageFn.write normalized


/// Hash PackageValue definitions to stable content-based hashes
module PackageValue =
  open LibSerialization.Binary

  /// Compute a content-addressed hash for a PackageValue
  ///
  /// The hash is based on the complete value definition (type, body, etc).
  /// The existing ID field is normalized to Hash.empty before hashing.
  let hash (value : LibExecution.ProgramTypes.PackageValue.PackageValue) : Hash =
    let normalized = { value with id = Hash.empty }
    hashWithWriter Serializers.PT.PackageValue.write normalized
