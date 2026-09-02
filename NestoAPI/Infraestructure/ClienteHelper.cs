using NestoAPI.Models;
using NestoAPI.Infraestructure.Clientes;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public static class ClienteHelper
    {
        /// <summary>
        /// NestoAPI#446: el nivel de precios que le toca a la persona de contacto con ese email
        /// en ese cliente, por sus cargos ("Pedidos sin ver precios" = 30, "sin ver descuentos" =
        /// 31). Con varios cargos para el mismo correo manda el más restrictivo. Único caller:
        /// AuthController.CrearJWTAsync (login y refresco de TiendasNuevaVision). Ante cualquier
        /// duda (error, sin email) es Completo: a nadie se le esconden los precios por accidente.
        /// </summary>
        public static async Task<PoliticaPreciosOcultos.NivelPrecios> NivelPreciosAsync(string clienteId, string email)
        {
            if (string.IsNullOrWhiteSpace(clienteId) || string.IsNullOrWhiteSpace(email))
            {
                return PoliticaPreciosOcultos.NivelPrecios.Completo;
            }
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    return await NivelPreciosAsync(db, clienteId, email);
                }
            }
            catch (Exception)
            {
                return PoliticaPreciosOcultos.NivelPrecios.Completo;
            }
        }

        public static async Task<PoliticaPreciosOcultos.NivelPrecios> NivelPreciosAsync(NVEntities db, string clienteId, string email)
        {
            string cliente = clienteId.Trim();
            string correo = email.Trim().ToLower();
            System.Collections.Generic.List<short?> cargos = await db.PersonasContactoClientes
                .Where(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO
                    && p.NºCliente == cliente
                    && p.CorreoElectrónico != null
                    && p.CorreoElectrónico.Trim().ToLower() == correo)
                .Select(p => (short?)p.Cargo)
                .ToListAsync();
            return PoliticaPreciosOcultos.NivelMasRestrictivo(cargos);
        }

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