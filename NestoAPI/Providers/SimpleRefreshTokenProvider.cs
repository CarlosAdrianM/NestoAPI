using Microsoft.Owin.Security.Infrastructure;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Providers
{
    // NestoAPI#188: emisión y validación de refresh_tokens OAuth2 para /oauth/token.
    // Scope: SOLO el flow de NestoApp (grant password). Nesto desktop y TiendasNuevaVision
    // usan AuthController con su propia lógica, y no pasan por aquí.
    //
    // Patrón (Taiseer Joudeh):
    //   - CreateAsync: el cliente recibe un GUID en claro; nosotros persistimos solo
    //     su hash SHA-256. Si la BD se compromete, los tokens no son reutilizables.
    //   - ReceiveAsync: hasheamos el token entrante, buscamos por PK, validamos que
    //     no esté expirado ni revocado, deserializamos el ticket original y marcamos
    //     el token como revocado (rotación). El siguiente CreateAsync emitirá uno nuevo.
    internal sealed class SimpleRefreshTokenProvider : IAuthenticationTokenProvider
    {
        private readonly Func<IRefreshTokenStore> _storeFactory;
        private readonly TimeSpan _tokenLifetime;

        public SimpleRefreshTokenProvider(TimeSpan tokenLifetime)
            : this(() => new EfRefreshTokenStore(), tokenLifetime)
        {
        }

        // Constructor para tests
        internal SimpleRefreshTokenProvider(Func<IRefreshTokenStore> storeFactory, TimeSpan tokenLifetime)
        {
            _storeFactory = storeFactory ?? throw new ArgumentNullException(nameof(storeFactory));
            _tokenLifetime = tokenLifetime;
        }

        public async Task CreateAsync(AuthenticationTokenCreateContext context)
        {
            string clientId = ObtenerClientId(context);
            string secret = Guid.NewGuid().ToString("N");
            string hash = HashToken(secret);

            DateTimeOffset issuedUtc = DateTimeOffset.UtcNow;
            DateTimeOffset expiresUtc = issuedUtc.Add(_tokenLifetime);

            context.Ticket.Properties.IssuedUtc = issuedUtc;
            context.Ticket.Properties.ExpiresUtc = expiresUtc;

            var entidad = new Infraestructure.RefreshToken
            {
                Id = hash,
                UserName = context.Ticket.Identity.Name,
                ClientId = clientId,
                IssuedUtc = issuedUtc.UtcDateTime,
                ExpiresUtc = expiresUtc.UtcDateTime,
                ProtectedTicket = context.SerializeTicket()
            };

            using (IRefreshTokenStore store = _storeFactory())
            {
                await store.AddAsync(entidad).ConfigureAwait(false);
            }

            context.SetToken(secret);
        }

        public async Task ReceiveAsync(AuthenticationTokenReceiveContext context)
        {
            string hash = HashToken(context.Token);

            using (IRefreshTokenStore store = _storeFactory())
            {
                Infraestructure.RefreshToken entidad = await store.FindAsync(hash).ConfigureAwait(false);
                if (entidad == null || entidad.RevokedUtc.HasValue || entidad.ExpiresUtc <= DateTime.UtcNow)
                {
                    // context.SetTicket no se llama → OWIN devuelve invalid_grant al cliente
                    return;
                }

                // Rotación: revocamos este token antes de entregar el ticket. Si el Grant
                // posterior falla, el usuario pierde este refresh_token y tendrá que
                // re-autenticarse; es el trade-off aceptado por seguridad.
                entidad.RevokedUtc = DateTime.UtcNow;
                await store.SaveChangesAsync().ConfigureAwait(false);

                context.DeserializeTicket(entidad.ProtectedTicket);
            }
        }

        public void Create(AuthenticationTokenCreateContext context)
        {
            // OWIN prefiere los métodos async; los sync quedan como shims.
            CreateAsync(context).GetAwaiter().GetResult();
        }

        public void Receive(AuthenticationTokenReceiveContext context)
        {
            ReceiveAsync(context).GetAwaiter().GetResult();
        }

        internal static string HashToken(string token)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(token));
                var sb = new StringBuilder(bytes.Length * 2);
                for (int i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private static string ObtenerClientId(AuthenticationTokenCreateContext context)
        {
            System.Collections.Generic.IDictionary<string, string> dict = context.Ticket.Properties.Dictionary;
            return dict != null && dict.TryGetValue("as:client_id", out string valor) && !string.IsNullOrEmpty(valor)
                ? valor
                : "NestoApp";
        }
    }
}
