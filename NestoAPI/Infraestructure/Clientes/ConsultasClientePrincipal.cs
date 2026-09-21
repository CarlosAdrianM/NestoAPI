using NestoAPI.Models;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#500: único punto para buscar la ficha principal de un cliente.
    ///
    /// La regla de negocio es que solo puede haber una ficha con ClientePrincipal = 1 por número
    /// de cliente, y el #263 puso la guarda al crear... pero la base de datos no lo impide y el
    /// Nesto viejo escribe directo en ella. El 19/09/2026 había 79 clientes con DOS principales
    /// (corregidos con Scripts/Issue500_ClientePrincipalDuplicado.sql) y media aplicación se caía
    /// con un 500 "La secuencia contiene más de un elemento", porque tanto Single como
    /// SingleOrDefault lanzan si aparece más de una fila: plantilla de venta, razón social de la
    /// factura, formas de pago, plazos de pago, extracto de cliente y pedidos del cliente.
    ///
    /// Aquí se ordena por Contacto y se coge el primero, que es justo la fila que devolvería la
    /// base de datos por orden de índice si solo hubiera una. Un duplicado futuro degrada (se
    /// atiende al contacto más bajo) en vez de tumbar la pantalla entera.
    /// </summary>
    public static class ConsultasClientePrincipal
    {
        public static Cliente BuscarPrincipal(this IQueryable<Cliente> clientes, string empresa, string cliente)
        {
            return FiltrarPrincipales(clientes, empresa, cliente).FirstOrDefault();
        }

        public static Task<Cliente> BuscarPrincipalAsync(this IQueryable<Cliente> clientes, string empresa, string cliente)
        {
            return FiltrarPrincipales(clientes, empresa, cliente).FirstOrDefaultAsync();
        }

        private static IQueryable<Cliente> FiltrarPrincipales(IQueryable<Cliente> clientes, string empresa, string cliente)
        {
            return clientes
                .Where(c => c.Empresa == empresa && c.Nº_Cliente == cliente && c.ClientePrincipal)
                .OrderBy(c => c.Contacto);
        }
    }
}
