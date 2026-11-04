using NestoAPI.Infraestructure;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#813: un cliente activo sin vendedor en la ficha.</summary>
    public class ClienteSinVendedorDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Nombre { get; set; }
        public short? Estado { get; set; }
        /// <summary>Fecha de su último pedido (null si no tiene ninguno), para empezar por los que compran.</summary>
        public DateTime? UltimoPedido { get; set; }
    }

    public partial class ClientesController
    {
        public const string MENSAJE_SIN_VENDEDOR_SIN_PERMISO = "Solo Dirección, Administración o Informática pueden ver los clientes sin vendedor.";

        /// <summary>NestoAPI#813: meses → los clientes sin vendedor (sustituible en los tests). null = la consulta de verdad.</summary>
        internal Func<int?, Task<List<ClienteSinVendedorDTO>>> LeerClientesSinVendedor { get; set; }

        internal static bool PuedeVerClientesSinVendedor(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.INFORMATICA));

        // GET api/Clientes/SinVendedor            → todos los activos (empresas 1 y 3) sin vendedor
        // GET api/Clientes/SinVendedor?meses=12   → solo los que han hecho algún pedido en los últimos 12 meses
        /// <summary>
        /// NestoAPI#813: clientes activos (estado ≥ 0) de las empresas 1 y 3 con el vendedor de la ficha vacío, para que Administración
        /// se lo ponga: sin vendedor en la ficha, el pedido puede grabarse sin vendedor y luego no se puede albaranear ni facturar.
        /// Ordenados por su último pedido (los que compran, primero).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/SinVendedor")]
        [ResponseType(typeof(List<ClienteSinVendedorDTO>))]
        public async Task<IHttpActionResult> GetClientesSinVendedor(int? meses = null)
        {
            if (!PuedeVerClientesSinVendedor(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_SIN_VENDEDOR_SIN_PERMISO);
            }
            if (meses.HasValue && meses.Value <= 0)
            {
                return BadRequest("Los meses tienen que ser más de cero");
            }
            List<ClienteSinVendedorDTO> clientes = await (LeerClientesSinVendedor ?? LeerClientesSinVendedorDeLaBD)(meses).ConfigureAwait(false);
            return Ok(clientes);
        }

        internal const string SQL_CLIENTES_SIN_VENDEDOR = @"
SELECT RTRIM(c.Empresa) AS Empresa, RTRIM(c.[Nº Cliente]) AS Cliente, RTRIM(c.Contacto) AS Contacto, RTRIM(c.Nombre) AS Nombre,
       c.Estado, u.UltimoPedido
FROM Clientes c
OUTER APPLY (SELECT MAX(p.Fecha) AS UltimoPedido FROM CabPedidoVta p
             WHERE p.Empresa = c.Empresa AND p.[Nº Cliente] = c.[Nº Cliente] AND p.Contacto = c.Contacto) u
WHERE c.Empresa IN ('1', '3') AND c.Estado >= 0 AND (c.Vendedor IS NULL OR LTRIM(RTRIM(c.Vendedor)) = '')
  AND (@Meses IS NULL OR u.UltimoPedido >= DATEADD(MONTH, -@Meses, GETDATE()))
ORDER BY CASE WHEN u.UltimoPedido IS NULL THEN 1 ELSE 0 END, u.UltimoPedido DESC, c.Empresa, c.[Nº Cliente], c.Contacto";

        private async Task<List<ClienteSinVendedorDTO>> LeerClientesSinVendedorDeLaBD(int? meses)
        {
            return await db.Database.SqlQuery<ClienteSinVendedorDTO>(SQL_CLIENTES_SIN_VENDEDOR,
                new SqlParameter("@Meses", System.Data.SqlDbType.Int) { Value = (object)meses ?? DBNull.Value })
                .ToListAsync().ConfigureAwait(false);
        }
    }
}
