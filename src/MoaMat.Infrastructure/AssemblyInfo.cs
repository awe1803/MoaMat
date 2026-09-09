using System.Runtime.CompilerServices;

// The unit test project verifies the internal translation helpers directly:
// they carry real decisions (date conversion, failure classification) that
// deserve coverage without widening the public surface of the adapter layer.
[assembly: InternalsVisibleTo("MoaMat.UnitTests")]
