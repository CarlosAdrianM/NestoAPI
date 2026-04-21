using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public static class ClienteHelper
    {
        // Issue NestoAPI#168 (TiendasNuevaVision#29): el check "¿tiene compras?" debe
        // mirar en TODAS las empresas. Un cliente con compras solo en empresa 2 sigue
        // siendo cliente del grupo y debe poder ver los vídeos de "Solo clientes" en
        // TiendasNuevaVision. Único caller: AuthController.CrearJWTAsync (login de
        // TiendasNuevaVision); Nesto/NestoApp no pasan por aquí.
        public static async Task<bool> ClienteConComprasRecientesAsync(string clienteId)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    return await ClienteConComprasRecientesAsync(db, clienteId);
                }
            }
            catch
            {
                return false;
            }
        }

        // Internal para tests (InternalsVisibleTo("NestoAPI.Tests")): recibe el DbContext
        // inyectado para poder mockear con DbSet de memoria.
        internal static Task<bool> ClienteConComprasRecientesAsync(NVEntities db, string clienteId)
        {
            DateTime fechaLimite = DateTime.Now.AddDays(-365);
            return db.ExtractosCliente
                .AnyAsync(p => p.TipoApunte == Constantes.ExtractosCliente.TiposApunte.FACTURA &&
                               p.Número == clienteId &&
                               p.Importe >= 0 &&
                               p.Fecha >= fechaLimite);
        }

        public static bool ClienteConComprasRecientes(string clienteId)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    DateTime fechaLimite = DateTime.Now.AddDays(-365);
                    return db.ExtractosCliente
                        .Any(p => p.TipoApunte == Constantes.ExtractosCliente.TiposApunte.FACTURA &&
                                  p.Número == clienteId &&
                                  p.Importe >= 0 &&
                                  p.Fecha >= fechaLimite);
                }
            }
            catch
            {
                return false;
            }
        }
    }
}