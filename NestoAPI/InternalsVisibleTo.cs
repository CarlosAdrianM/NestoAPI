using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("NestoAPI.Tests")]

// NestoAPI#188: Castle.DynamicProxy (usado por FakeItEasy) necesita ver tipos
// internal para generar proxies en tests. Sin esto, A.Fake<IRefreshTokenStore>()
// falla porque la interfaz es internal.
[assembly: InternalsVisibleTo("DynamicProxyGenAssembly2")]
