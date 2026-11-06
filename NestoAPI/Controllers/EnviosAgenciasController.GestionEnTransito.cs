using System;
using System.Data.Entity.Validation;
using System.Globalization;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// NestoAPI#726: gestión en tránsito de un envío que ya tiene la agencia (hoy solo CTT): parar en vuelo, retorno
    /// (logística inversa), cambiar la fecha y la dirección de entrega. Núcleo común (cargar, comprobar, llamar a la
    /// agencia, historia, auditoría y correo a logística, NestoAPI#722) y la estrategia de cada agencia en
    /// <see cref="IGestionEnTransitoAgenciaRemota"/>. API primero, BD después, como Anular y Modificar.
    /// </summary>
    public partial class EnviosAgenciasController
    {
        private IServicioCorreoElectronico servicioCorreoGestiones;

        /// <summary>NestoAPI#726: el correo a logística de las gestiones (sustituible en los tests).</summary>
        internal IServicioCorreoElectronico ServicioCorreoGestiones
        {
            get => servicioCorreoGestiones ?? (servicioCorreoGestiones = new ServicioCorreoElectronico());
            set => servicioCorreoGestiones = value;
        }

        /// <summary>NestoAPI#726: hoy, para no admitir fechas de entrega pasadas (sustituible en los tests).</summary>
        internal Func<DateTime> HoyGestiones { get; set; } = () => DateTime.Today;

        /// <summary>NestoAPI#726: ¿no se reparte ese día? (festivo nacional o autonómico, sábado o domingo; sustituible en los tests).</summary>
        internal Func<DateTime, bool> EsDiaSinReparto { get; set; } = dia => GestorFestivos.EsFestivo(dia, Constantes.Almacenes.ALGETE);

        [HttpGet]
        [Route("api/EnviosAgencias/{id:int}/Gestiones")]
        [ResponseType(typeof(GestionesEnvioDTO))]
        public async Task<IHttpActionResult> GetGestionesEnvio(int id)
        {
            EnviosAgencia envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }
            return Ok(GestionEnTransitoEnvio.Calcular(envio, GestionEnTransitoDe(envio), await NombreAgenciaParaUsuario(envio)));
        }

        [HttpPost]
        [Route("api/EnviosAgencias/{id:int}/PararEnVuelo")]
        [ResponseType(typeof(ResultadoGestionEnvioDTO))]
        public async Task<IHttpActionResult> PararEnVuelo(int id)
        {
            GestionPreparada g = await PrepararGestion(id, gestiones => gestiones.PararEnVuelo);
            if (g.Error != null) return g.Error;

            string operacion = GestionEnTransitoEnvio.OPERACION_PARAR_EN_VUELO;
            IHttpActionResult fallo = await LlamarAgencia(g, operacion, () => g.Agencia.PararEnVueloAsync(g.Envio.CodigoBarras.Trim()));
            if (fallo != null) return fallo;

            string detalle = "El envío entra en incidencia y vuelve a origen (tiene coste: ida y vuelta).";
            await TerminarGestion(g, operacion, g.Envio.Estado.ToString(), detalle);
            return Ok(new ResultadoGestionEnvioDTO { Mensaje = $"{g.NombreAgencia ?? "La agencia"} ha parado el envío: vuelve a origen." });
        }

        [HttpPost]
        [Route("api/EnviosAgencias/{id:int}/Retorno")]
        [ResponseType(typeof(ResultadoRetornoEnvioDTO))]
        public async Task<IHttpActionResult> CrearRetornoEnvio(int id)
        {
            GestionPreparada g = await PrepararGestion(id, gestiones => gestiones.Retorno);
            if (g.Error != null) return g.Error;

            string operacion = GestionEnTransitoEnvio.OPERACION_RETORNO;
            string codigo = null;
            IHttpActionResult fallo = await LlamarAgencia(g, operacion, async () =>
            {
                codigo = await g.Agencia.CrearRetornoAsync(g.Envio.CodigoBarras.Trim());
                return null;
            });
            if (fallo != null) return fallo;

            // El código solo lo da la respuesta: se graba como un envío nuevo de recogida en origen (como los retornos de
            // siempre: sale en la pestaña Retornos y lo sigue el poll). Si la fila no se puede grabar, queda en la historia.
            int? envioRetornoId = await GrabarEnvioRetorno(g.Envio, codigo);
            string detalle = $"Envío de retorno {codigo}" + (envioRetornoId.HasValue ? $" (envío {envioRetornoId})" : " (no se pudo grabar como envío: apúntelo)") +
                ". Lo recoge el conductor en casa del cliente y saca él la etiqueta.";
            await TerminarGestion(g, operacion, g.Envio.Retorno.ToString(), detalle);
            return Ok(new ResultadoRetornoEnvioDTO
            {
                CodigoRetorno = codigo,
                EnvioRetornoId = envioRetornoId,
                Mensaje = $"Retorno pedido a {g.NombreAgencia ?? "la agencia"}: {codigo}. El repartidor pasará a recogerlo; no hace falta etiqueta."
            });
        }

        [HttpPost]
        [Route("api/EnviosAgencias/{id:int}/FechaEntrega")]
        [ResponseType(typeof(ResultadoGestionEnvioDTO))]
        public async Task<IHttpActionResult> CambiarFechaEntrega(int id, CambiarFechaEntregaEnvioDTO datos)
        {
            if (datos?.Fecha == null)
            {
                return BadRequest("Falta la fecha de entrega nueva.");
            }
            DateTime fecha = datos.Fecha.Value.Date;
            if (fecha < HoyGestiones().Date)
            {
                return BadRequest($"La fecha de entrega {fecha:dd/MM/yyyy} ya ha pasado.");
            }
            if (EsDiaSinReparto(fecha))
            {
                return BadRequest($"El {fecha:dd/MM/yyyy} no se reparte (festivo o fin de semana): elija otro día.");
            }

            GestionPreparada g = await PrepararGestion(id, gestiones => gestiones.CambiarFecha);
            if (g.Error != null) return g.Error;

            string operacion = GestionEnTransitoEnvio.OPERACION_FECHA;
            IHttpActionResult fallo = await LlamarAgencia(g, operacion, () => g.Agencia.CambiarFechaEntregaAsync(g.Envio.CodigoBarras.Trim(), fecha));
            if (fallo != null) return fallo;

            string anterior = g.Envio.FechaEntrega?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) ?? string.Empty;
            // La agencia ya tiene la fecha nueva: se graba en el envío para que Nesto la vea (como la dirección).
            g.Envio.FechaEntrega = fecha;
            await TerminarGestion(g, operacion, anterior, $"Nueva fecha de entrega: {fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.");
            return Ok(new ResultadoGestionEnvioDTO { Mensaje = $"{g.NombreAgencia ?? "La agencia"} entregará el envío el {fecha:dd/MM/yyyy}." });
        }

        [HttpPost]
        [Route("api/EnviosAgencias/{id:int}/DireccionEntrega")]
        [ResponseType(typeof(ResultadoGestionEnvioDTO))]
        public async Task<IHttpActionResult> CambiarDireccionEntrega(int id, CambiarDireccionEntregaEnvioDTO datos)
        {
            if (datos == null)
            {
                return BadRequest("Faltan los datos de la dirección nueva.");
            }

            GestionPreparada g = await PrepararGestion(id, gestiones => gestiones.CambiarDireccion);
            if (g.Error != null) return g.Error;

            var direccion = new DireccionEntregaRemota
            {
                Nombre = datos.Nombre?.Trim(),
                Direccion = datos.Direccion?.Trim(),
                CodigoPostal = datos.CodigoPostal?.Trim(),
                Poblacion = datos.Poblacion?.Trim(),
                Pais = string.IsNullOrWhiteSpace(datos.Pais) ? "ES" : datos.Pais.Trim().ToUpperInvariant(),
                Telefono = datos.Telefono?.Trim(),
                Correo = datos.Correo?.Trim()
            };

            string operacion = GestionEnTransitoEnvio.OPERACION_DIRECCION;
            IHttpActionResult fallo = await LlamarAgencia(g, operacion, () => g.Agencia.CambiarDireccionEntregaAsync(g.Envio.CodigoBarras.Trim(), direccion));
            if (fallo != null) return fallo;

            EnviosAgencia envio = g.Envio;
            string anterior = $"{envio.Nombre?.Trim()}, {envio.Direccion?.Trim()}, {envio.CodPostal?.Trim()} {envio.Poblacion?.Trim()} (tel. {envio.Telefono?.Trim()}, {envio.Email?.Trim()})";
            // La agencia ya tiene la dirección nueva: se graba en el envío para que Nesto la vea (como hace Modificar).
            envio.Nombre = Limitar(direccion.Nombre, 80);
            envio.Direccion = Limitar(direccion.Direccion, 80);
            envio.CodPostal = Limitar(CodigoPostalEnvio(direccion.CodigoPostal, direccion.Pais), 20);
            envio.Poblacion = Limitar(direccion.Poblacion, 80);
            envio.Telefono = Limitar(PrimerTelefono(direccion.Telefono), 15);
            envio.Email = Limitar(direccion.Correo, 254);
            string nueva = $"{envio.Nombre}, {envio.Direccion}, {envio.CodPostal} {envio.Poblacion} (tel. {direccion.Telefono}, {envio.Email})";
            await TerminarGestion(g, operacion, Limitar(anterior, 50), $"Antes: {anterior}. Ahora: {nueva}.");
            return Ok(new ResultadoGestionEnvioDTO { Mensaje = $"{g.NombreAgencia ?? "La agencia"} entregará el envío en la dirección nueva." });
        }

        // ---------- núcleo común ----------

        internal class GestionPreparada
        {
            public EnviosAgencia Envio;
            public IGestionEnTransitoAgenciaRemota Agencia;
            public string NombreAgencia;
            public IHttpActionResult Error;
        }

        private IGestionEnTransitoAgenciaRemota GestionEnTransitoDe(EnviosAgencia envio)
            => fabricaAgenciasRemotas.Crear(envio.Agencia) as IGestionEnTransitoAgenciaRemota;

        /// <summary>Carga el envío (404 si no existe) y comprueba que admite la operación (400 con el motivo si no).</summary>
        private async Task<GestionPreparada> PrepararGestion(int id, Func<GestionesEnvioDTO, bool> admite)
        {
            var g = new GestionPreparada { Envio = await db.EnviosAgencias.FindAsync(id) };
            if (g.Envio == null)
            {
                g.Error = NotFound();
                return g;
            }
            g.Agencia = GestionEnTransitoDe(g.Envio);
            g.NombreAgencia = await NombreAgenciaParaUsuario(g.Envio);
            GestionesEnvioDTO gestiones = GestionEnTransitoEnvio.Calcular(g.Envio, g.Agencia, g.NombreAgencia);
            if (!admite(gestiones))
            {
                g.Error = BadRequest(gestiones.Motivo ?? "El envío no admite esta gestión en la agencia.");
            }
            return g;
        }

        /// <summary>
        /// Llama a la agencia. null = hecho (y auditado); si no, la respuesta de error: 400 con el motivo de la agencia o
        /// con los datos que faltan, 502 si la agencia no responde. El rechazo de la agencia queda en la historia.
        /// </summary>
        private async Task<IHttpActionResult> LlamarAgencia(GestionPreparada g, string operacion, Func<Task<ResultadoOperacionRemota>> llamada)
        {
            string error;
            AgenciaRemotaException excepcion = null;
            try
            {
                ResultadoOperacionRemota resultado = await llamada();
                if (resultado == null || resultado.Exito)
                {
                    await AuditarGestion(g, true, null, operacion);
                    return null;
                }
                error = resultado.Error ?? "La agencia ha rechazado la operación.";
            }
            catch (ArgumentException ex)
            {
                // Datos que la agencia exige y no vienen: no se ha llamado.
                return BadRequest(ex.Message);
            }
            catch (AgenciaRemotaException ex)
            {
                excepcion = ex;
                error = ex.Message;
            }

            string usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            db.EnviosHistorias.Add(FilaHistoria(g.Envio, operacion, string.Empty, GestionEnTransitoEnvio.TextoHistoria(operacion, usuario, false, error), usuario));
            await AuditarGestion(g, false, error, operacion);
            if (excepcion != null && FallosAgenciaRemota.NoResponde(excepcion))
            {
                return RespuestaAgenciaNoResponde(g.NombreAgencia, excepcion, g.Envio, operacion);
            }
            return BadRequest(error);
        }

        // AgenciasLlamadasWeb (como Anular/Modificar) y, de paso, guarda la fila de historia pendiente.
        private async Task AuditarGestion(GestionPreparada g, bool exito, string error, string operacion)
        {
            if (g.Agencia is IAnulacionAgenciaRemota conIntercambios)
            {
                await AuditarOperacion(g.Envio, conIntercambios, exito, error, operacion);
                return;
            }
            await GuardarSinRomper(g.Envio, operacion);
        }

        /// <summary>Hecho en la agencia: la historia, lo que haya cambiado en el envío y el correo a logística (NestoAPI#722).</summary>
        private async Task TerminarGestion(GestionPreparada g, string operacion, string valorAnterior, string detalle)
        {
            string usuario = UsuarioAuditoriaHelper.Resolver(User, "NestoAPI");
            g.Envio.Usuario = Limitar(usuario, 50);
            g.Envio.FechaModificacion = DateTime.Now;
            db.EnviosHistorias.Add(FilaHistoria(g.Envio, operacion, valorAnterior, GestionEnTransitoEnvio.TextoHistoria(operacion, usuario, true, detalle), usuario));
            await GuardarSinRomper(g.Envio, operacion);
            AvisarLogistica(g, operacion, detalle, usuario);
        }

        /// <summary>La agencia ya lo ha hecho: un fallo de nuestra BD no se devuelve como error de la operación (ELMAH).</summary>
        private async Task GuardarSinRomper(EnviosAgencia envio, string operacion)
        {
            try
            {
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"{operacion} del envío {envio.Numero} (albarán {envio.CodigoBarras?.Trim()}): no se pudo guardar la historia o el envío: {DescribirErroresValidacion(ex)}", ex));
            }
        }

        private void AvisarLogistica(GestionPreparada g, string operacion, string detalle, string usuario)
        {
            try
            {
                MailMessage correo = GestionEnTransitoEnvio.ComponerCorreoLogistica(g.Envio, g.NombreAgencia, operacion, detalle, usuario);
                if (!ServicioCorreoGestiones.EnviarCorreoSMTP(correo))
                {
                    ElmahHelper.Log(new Exception($"No se pudo enviar a logística el correo de «{operacion}» del envío {g.Envio.Numero}."));
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"No se pudo enviar a logística el correo de «{operacion}» del envío {g.Envio.Numero}: {ex.Message}", ex));
            }
        }

        private static EnvioHistoria FilaHistoria(EnviosAgencia envio, string campo, string valorAnterior, string observaciones, string usuario) => new EnvioHistoria
        {
            NumeroEnvio = envio.Numero,
            Campo = Limitar(campo, 50),
            ValorAnterior = Limitar(valorAnterior ?? string.Empty, 50) ?? string.Empty,
            Observaciones = observaciones,
            Usuario = Limitar(usuario, 50),
            FechaModificacion = DateTime.Now
        };

        /// <summary>
        /// El envío de retorno como fila nueva de EnviosAgencia: los datos del original, recogida en origen (Retorno 2),
        /// ya tramitado en la agencia y sin reembolso. Devuelve su número, o null si no se pudo grabar (ELMAH).
        /// </summary>
        private async Task<int?> GrabarEnvioRetorno(EnviosAgencia o, string codigo)
        {
            EnviosAgencia retorno = EnvioRetornoDe(o, codigo, HoyGestiones(), UsuarioAuditoriaHelper.Resolver(User, "NestoAPI"));
            try
            {
                db.EnviosAgencias.Add(retorno);
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception(
                    $"La agencia creó el retorno {codigo} del envío {o.Numero} pero no se pudo grabar como envío: {DescribirErroresValidacion(ex)}", ex));
                try
                {
                    db.Entry(retorno).State = System.Data.Entity.EntityState.Detached;
                }
                catch
                {
                    // Sin contexto real (tests): nada que soltar.
                }
                return null;
            }
            return retorno.Numero;
        }

        internal static EnviosAgencia EnvioRetornoDe(EnviosAgencia o, string codigo, DateTime hoy, string usuario) => new EnviosAgencia
        {
            Empresa = o.Empresa,
            Agencia = o.Agencia,
            Cliente = o.Cliente,
            Contacto = o.Contacto,
            Pedido = o.Pedido,
            Estado = Constantes.Agencias.ESTADO_TRAMITADO,
            Fecha = hoy.Date,
            Servicio = o.Servicio,
            Horario = o.Horario,
            Bultos = o.Bultos,
            Retorno = AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN,
            Nombre = o.Nombre,
            Direccion = o.Direccion,
            CodPostal = o.CodPostal,
            Poblacion = o.Poblacion,
            Provincia = o.Provincia,
            Pais = o.Pais,
            Telefono = o.Telefono,
            Movil = o.Movil,
            Email = o.Email,
            Observaciones = Limitar($"Retorno del envío {o.Numero} ({o.CodigoBarras?.Trim()})", 80),
            Atencion = o.Atencion,
            Reembolso = 0m,
            Vendedor = o.Vendedor,
            ImporteGasto = 0m,
            ImporteAsegurado = 0m,
            Peso = o.Peso,
            Nemonico = o.Nemonico,
            NombrePlaza = o.NombrePlaza,
            TelefonoPlaza = o.TelefonoPlaza,
            EmailPlaza = o.EmailPlaza,
            CodigoBarras = codigo,
            Usuario = Limitar(string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : usuario, 50),
            FechaModificacion = DateTime.Now
        };

        private static string PrimerTelefono(string telefonos)
            => (telefonos ?? string.Empty).Split(new[] { '/', ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim()).FirstOrDefault(t => t.Length > 0) ?? string.Empty;
    }
}
