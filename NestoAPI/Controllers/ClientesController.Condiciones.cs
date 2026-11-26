using NestoAPI.Infraestructure.Clientes;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Carlos 26/11/26: el IVA de la ficha y los descuentos pactados de un cliente por la API (hasta hoy, scripts de sa). LAS REGLAS:
    /// <see cref="ReglasCondicionesCliente"/>; el servicio, <see cref="ServicioCondicionesCliente"/>.
    /// </summary>
    public partial class ClientesController
    {
        /// <summary>Sustituible en los tests.</summary>
        internal IRepositorioCondicionesCliente RepositorioCondicionesCliente
        {
            get => repositorioCondicionesCliente ?? (repositorioCondicionesCliente = new RepositorioCondicionesClienteSql(db.Database.Connection.ConnectionString));
            set => repositorioCondicionesCliente = value;
        }
        private IRepositorioCondicionesCliente repositorioCondicionesCliente;

        /// <summary>(empresa, cliente) → publica cada contacto a Odoo. Sustituible en los tests.</summary>
        internal System.Func<string, string, Task> PublicarCliente { get; set; }

        /// <summary>
        /// El IVA de TODOS los contactos del cliente (solo uno de ParametrosIVA de la empresa), con rastro en Modificaciones (Tabla ClienteIva)
        /// y publicando a Odoo como cualquier cambio de ficha. Administración, Dirección e Informática.
        /// </summary>
        [HttpPut]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/Iva")]
        [ResponseType(typeof(ResultadoIvaClienteDTO))]
        public async Task<IHttpActionResult> PutIvaCliente(string empresa, string cliente, [FromBody] CambioIvaClienteDTO peticion)
        {
            ResultadoCondicionesCliente<ResultadoIvaClienteDTO> r = await new ServicioCondicionesCliente(RepositorioCondicionesCliente)
                .CambiarIva(empresa, cliente, peticion, User, PublicarCliente ?? PublicarClienteTrasCopia).ConfigureAwait(true);
            return TraducirCondiciones(r);
        }

        /// <summary>Los descuentos pactados (filas de DescuentosProducto) del cliente. Administración, Dirección e Informática.</summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/DescuentosPactados")]
        [ResponseType(typeof(List<DescuentoPactadoDTO>))]
        public async Task<IHttpActionResult> GetDescuentosPactados(string empresa, string cliente)
        {
            return TraducirCondiciones(await new ServicioCondicionesCliente(RepositorioCondicionesCliente).Leer(empresa, cliente, User).ConfigureAwait(true));
        }

        /// <summary>
        /// UNA fila de DescuentosProducto del cliente si no hay una igual (misma clave), con rastro (Tabla DescuentoPactado). Con
        /// <paramref name="ensayo"/> (por defecto) no escribe: dice a cuántos productos vivos aplica y lo que saldría en uno de ejemplo.
        /// Dirección e Informática.
        /// </summary>
        [HttpPost]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/DescuentosPactados")]
        [ResponseType(typeof(ResultadoDescuentoPactadoDTO))]
        public async Task<IHttpActionResult> PostDescuentoPactado(string empresa, string cliente, [FromBody] DescuentoPactadoPeticionDTO peticion, bool ensayo = true)
        {
            return TraducirCondiciones(await new ServicioCondicionesCliente(RepositorioCondicionesCliente)
                .Crear(empresa, cliente, peticion, ensayo, User).ConfigureAwait(true));
        }

        /// <summary>Borra una fila de DescuentosProducto, solo si es de ESE cliente, con rastro. Dirección e Informática.</summary>
        [HttpDelete]
        [Authorize]
        [Route("api/Clientes/{empresa}/{cliente}/DescuentosPactados/{numeroOrden:int}")]
        [ResponseType(typeof(DescuentoPactadoDTO))]
        public async Task<IHttpActionResult> DeleteDescuentoPactado(string empresa, string cliente, int numeroOrden)
        {
            return TraducirCondiciones(await new ServicioCondicionesCliente(RepositorioCondicionesCliente)
                .Borrar(empresa, cliente, numeroOrden, User).ConfigureAwait(true));
        }

        private IHttpActionResult TraducirCondiciones<T>(ResultadoCondicionesCliente<T> r)
        {
            switch (r.Estado)
            {
                case 200: return Ok(r.Datos);
                case 403: return Content(HttpStatusCode.Forbidden, new { Message = r.Motivo });
                case 404: return Content(HttpStatusCode.NotFound, new { Message = r.Motivo });
                case 409: return Content(HttpStatusCode.Conflict, new { Message = r.Motivo, r.Codigo });
                default: return Content(HttpStatusCode.BadRequest, new { Message = r.Motivo });
            }
        }
    }
}
