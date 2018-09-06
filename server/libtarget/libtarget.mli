(* Returns base64 encoding of the sha384 of the string *)
val digest384 : string -> string

(* Returns base64 encoding of the sha256 of the string *)
val digest256 : string -> string


val date_of_isostring : string -> Core_kernel.Time.t
val date_to_isostring : Core_kernel.Time.t -> string

val date_to_sqlstring : Core_kernel.Time.t -> string
val date_of_sqlstring : string -> Core_kernel.Time.t

val regexp_replace : pattern:string -> replacement:string -> string -> string

val string_split : sep:string -> string -> string list
