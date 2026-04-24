using NestoAPI.Infraestructure;
using System;
using System.Data.Entity;
using System.Threading.Tasks;

namespace NestoAPI.Providers
{
    // NestoAPI#188: capa mínima de persistencia para SimpleRefreshTokenProvider.
    // Aísla EF del provider para poder testearlo con FakeItEasy sin tocar BD.
    internal interface IRefreshTokenStore : IDisposable
    {
        Task AddAsync(RefreshToken token);
        Task<RefreshToken> FindAsync(string id);
        Task SaveChangesAsync();
    }

    internal sealed class EfRefreshTokenStore : IRefreshTokenStore
    {
        private readonly ApplicationDbContext _db = new ApplicationDbContext();

        public Task AddAsync(RefreshToken token)
        {
            _db.RefreshTokens.Add(token);
            return _db.SaveChangesAsync();
        }

        public Task<RefreshToken> FindAsync(string id)
        {
            return _db.RefreshTokens.FindAsync(id);
        }

        public Task SaveChangesAsync()
        {
            return _db.SaveChangesAsync();
        }

        public void Dispose()
        {
            _db.Dispose();
        }
    }
}
