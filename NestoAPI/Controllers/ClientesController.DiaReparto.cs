using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infrastructure;
using NestoAPI.Models;
using System;
using System.Net;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace NestoAPI.Controllers
{
    /// <summary>NestoAPI#799: la excepción del día de reparto de un contacto (GET).</summary>
    public class DiaRepartoClienteDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        /// <summary>'01' (lunes) … '05' (viernes).</summary>
        public string Ruta { get; set; }
        /// <summary>«miércoles».</summary>
        public string Dia { get; set; }
        public string Motivo { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }
    }

    /// <summary>NestoAPI#799: cuerpo del PUT. Ruta null o vacía = quitar la excepción (vuelve a ir por su código postal).</summary>
    public class PeticionDiaRepartoClienteDTO
    {
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Ruta { get; set; }
        public string Motivo { get; set; }
    }

    /// <summary>NestoAPI#799: respuesta del PUT.</summary>
    public class RespuestaDiaRepartoClienteDTO
    {
        /// <summary>La ruta que queda ('01'..'05'), o null si se ha quitado la excepción.</summary>
        public string Ruta { get; set; }
        /// <summary>«miércoles», o null si se ha quitado.</summary>
        public string Dia { get; set; }
        /// <summary>Cuántos pedidos pendientes del contacto han cambiado de fecha de entrega.</summary>
        public int PedidosRecalculados { get; set; }
    }

    public partial class ClientesController
    {
        public const string MENSAJE_DIA_REPARTO_SIN_PERMISO = "Solo Dirección, Almacén o Administración pueden cambiar el día de reparto de un cliente.";
        public const string MENSAJE_DIA_REPARTO_DIA_MALO = "El día tiene que ser de lunes a viernes";
        public const int LONGITUD_MOTIVO_DIA_REPARTO = 200;

        /// <summary>NestoAPI#799: la tabla ClientesDiaReparto y los pedidos pendientes (sustituible en los tests).</summary>
        internal IRepositorioDiasRepartoClientes RepositorioDiasReparto { get; set; } = new RepositorioDiasRepartoClientes();
        /// <summary>NestoAPI#799: dónde se traza un recálculo de fechas fallido (ELMAH; sustituible en los tests).</summary>
        internal Action<Exception> RegistrarErrorDiaReparto { get; set; } = ex => ElmahHelper.Log(ex);

        internal static bool PuedeCambiarDiaReparto(IPrincipal usuario) =>
            usuario != null && (usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.DIRECCION)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ALMACEN)
                || usuario.IsInRoleSinDominio(Constantes.GruposSeguridad.ADMINISTRACION));

        private static string EmpresaODefecto(string empresa) =>
            string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        // GET api/Clientes/DiaReparto?empresa=1&cliente=2077&contacto=3
        /// <summary>
        /// NestoAPI#799: la excepción del día de reparto por nuestra ruta de ese contacto (200), o 204 si no tiene (va por el día
        /// de su código postal).
        /// </summary>
        [HttpGet]
        [Authorize]
        [Route("api/Clientes/DiaReparto")]
        [ResponseType(typeof(DiaRepartoClienteDTO))]
        public async Task<IHttpActionResult> GetDiaReparto(string empresa, string cliente, string contacto)
        {
            if (string.IsNullOrWhiteSpace(cliente) || string.IsNullOrWhiteSpace(contacto))
            {
                return BadRequest("Falta el cliente o el contacto.");
            }
            string empresaLimpia = EmpresaODefecto(empresa);
            ExcepcionDiaRepartoCliente excepcion = await RepositorioDiasReparto.Leer(empresaLimpia, cliente.Trim(), contacto.Trim()).ConfigureAwait(false);
            DayOfWeek? dia = CalendarioRutasDia.DiaDeLaSemana(excepcion?.Ruta);
            if (excepcion == null || !dia.HasValue)
            {
                return StatusCode(HttpStatusCode.NoContent);
            }
            return Ok(new DiaRepartoClienteDTO
            {
                Empresa = excepcion.Empresa?.Trim() ?? empresaLimpia,
                Cliente = excepcion.Cliente?.Trim(),
                Contacto = excepcion.Contacto?.Trim(),
                Ruta = excepcion.Ruta.Trim(),
                Dia = CalendarioRutasDia.Nombre(dia.Value),
                Motivo = excepcion.Motivo,
                Usuario = excepcion.Usuario?.Trim(),
                FechaModificacion = excepcion.FechaModificacion
            });
        }

        // PUT api/Clientes/DiaReparto   { "Empresa": "1", "Cliente": "2077", "Contacto": "3", "Ruta": "03", "Motivo": "..." }
        /// <summary>
        /// NestoAPI#799: pone (Ruta '01'..'05') o quita (Ruta null o vacía) la excepción del día de reparto del contacto, y
        /// recalcula la fecha de entrega de sus pedidos pendientes por ruta propia. Dirección, Almacén o Administración.
        /// </summary>
        [HttpPut]
        [Authorize]
        [Route("api/Clientes/DiaReparto")]
        [ResponseType(typeof(RespuestaDiaRepartoClienteDTO))]
        public async Task<IHttpActionResult> PutDiaReparto([FromBody] PeticionDiaRepartoClienteDTO peticion)
        {
            if (!PuedeCambiarDiaReparto(User))
            {
                return Content(HttpStatusCode.Forbidden, MENSAJE_DIA_REPARTO_SIN_PERMISO);
            }
            if (peticion == null || string.IsNullOrWhiteSpace(peticion.Cliente) || string.IsNullOrWhiteSpace(peticion.Contacto))
            {
                return BadRequest("Falta el cliente o el contacto.");
            }
            string ruta = string.IsNullOrWhiteSpace(peticion.Ruta) ? null : peticion.Ruta.Trim();
            DayOfWeek? dia = CalendarioRutasDia.DiaDeLaSemana(ruta);
            if (ruta != null && !dia.HasValue)
            {
                return BadRequest(MENSAJE_DIA_REPARTO_DIA_MALO);
            }
            string empresa = EmpresaODefecto(peticion.Empresa);
            string cliente = peticion.Cliente.Trim();
            string contacto = peticion.Contacto.Trim();
            if (!await RepositorioDiasReparto.ContactoExiste(empresa, cliente, contacto).ConfigureAwait(false))
            {
                return BadRequest($"No existe el contacto {contacto} del cliente {cliente}.");
            }
            string motivo = string.IsNullOrWhiteSpace(peticion.Motivo) || ruta == null ? null : peticion.Motivo.Trim();
            if (motivo != null && motivo.Length > LONGITUD_MOTIVO_DIA_REPARTO)
            {
                motivo = motivo.Substring(0, LONGITUD_MOTIVO_DIA_REPARTO);
            }
            string usuario = UsuarioAuditoriaHelper.Resolver(User, UsuarioAuditoriaHelper.DESCONOCIDO);

            await RepositorioDiasReparto.Guardar(new ExcepcionDiaRepartoCliente
            {
                Empresa = empresa,
                Cliente = cliente,
                Contacto = contacto,
                Ruta = ruta,
                Motivo = motivo
            }, usuario).ConfigureAwait(false);
            DiasRepartoClientes.Invalidar();

            // La excepción queda guardada aunque no se puedan mover las fechas: se traza y el PUT no falla.
            int recalculados = 0;
            try
            {
                recalculados = await RepositorioDiasReparto.RecalcularFechasPendientes(empresa, cliente, contacto, usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                RegistrarErrorDiaReparto?.Invoke(new Exception($"NestoAPI#799: guardado el día de reparto de {cliente}/{contacto}, pero no se han podido recalcular las fechas de sus pedidos pendientes: {ex.Message}", ex));
            }

            return Ok(new RespuestaDiaRepartoClienteDTO
            {
                Ruta = ruta,
                Dia = dia.HasValue ? CalendarioRutasDia.Nombre(dia.Value) : null,
                PedidosRecalculados = recalculados
            });
        }
    }
}
