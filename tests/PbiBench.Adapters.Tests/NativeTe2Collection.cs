#if NETFRAMEWORK
using Xunit;

namespace PbiBench.Adapters.Tests;

// The inherited TE2 runtime has process-global model/editor state.
[CollectionDefinition("Native TE2", DisableParallelization = true)]
public sealed class NativeTe2Collection { }
#endif
