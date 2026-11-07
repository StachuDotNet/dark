-- A test's compiled body, beside its source, as `package_functions.rt_instrs` is for a function.
--
-- Written by the fold from the same `AddTest` op as `pt_def`, so `dark test run` executes these
-- instructions rather than compiling every test's body again on every run, and a worker reads
-- this column instead of the source.
--
-- NULL where the body cannot be compiled (`self` outside a function), and on rows folded before
-- this column existed. Running such a test compiles its source, as every run used to. A
-- projection like the rest of the table: rebuilt with it from `package_ops`.
ALTER TABLE package_tests ADD COLUMN rt_instrs BLOB;
