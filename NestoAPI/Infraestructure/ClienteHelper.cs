using NestoAPI.Models;
using System;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure
{
    public static class ClienteHelper
    {
        public static async Task<bool> ClienteConComprasRecientesAsync(string clienteId)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    DateTime fechaLimite = DateTime.Now.AddDays(-365);
                    return await db.ExtractosCliente
                        .AnyAsync(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                       p.TipoApunte == Constantes.ExtractosCliente.TiposApunte.FACTURA &&
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

        public static bool ClienteConComprasRecientes(string clienteId)
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    DateTime fechaLimite = DateTime.Now.AddDays(-365);
                    return db.ExtractosCliente
                        .Any(p => p.Empresa == Constantes.Empresas.EMPRESA_POR_DEFECTO &&
                                  p.TipoApunte == Constantes.ExtractosCliente.TiposApunte.FACTURA &&
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