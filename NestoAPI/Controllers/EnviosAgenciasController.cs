using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Data.Entity.Validation;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;
using NestoAPI.Models;
using System.Net.Mail;
using System.Text;
using System.Configuration;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Infraestructure.Agencias.Tarifas;
using System.Web.Http.Cors;
using System.Security.Claims;
using NestoAPI.Infraestructure.Seguridad;

namespace NestoAPI.Controllers
{
    /// <summary>
    /// Resultado de tramitar un envío con una agencia remota: el albarán que asignó la agencia, los
    /// bultos y la etiqueta (ZPL en base64) para mandar a la Zebra. <see cref="Reimpresion"/> indica
    /// que el envío ya estaba tramitado y solo se reimprimió (no se volvió a registrar).
    /// </summary>
    public class TramitarEnvioResultadoDTO
    {
        public int Numero { get; set; }
        public string Albaran { get; set; }
        public int Bultos { get; set; }
        public bool Reimpresion { get; set; }
        public string EtiquetaTipo { get; set; }
        public string EtiquetaCodificacion { get; set; }
        public string EtiquetaContenido { get; set; }
    }

    /// <summary>
    /// Datos corregidos para modificar un envío YA registrado en la agencia (#317). Solo se pisan
    /// los campos informados (null/vacío = conservar el valor actual del envío). La provincia solo
    /// se persiste en BD (las agencias la derivan del CP).
    /// </summary>
    public class ModificarEnvioAgenciaDTO
    {
        public string Nombre { get; set; }
        public string Direccion { get; set; }
        public string CodigoPostal { get; set; }
        public string Poblacion { get; set; }
        public string Provincia { get; set; }
        public string Telefono { get; set; }
        public string Movil { get; set; }
        public string Observaciones { get; set; }
    }

    // Issue #189: [Authorize] de clase. Llamantes auditados 13/07/26: Nesto (JWT vía
    // IClienteApiFactory desde 1.10.9.0), NestoApp (interceptor Bearer + refresh desde v2.17.2)
    // y TiendasNuevaVision (MyHttpClient con AuthHeaderHandler). Lo anónimo, explícito abajo.
    [Authorize]
    public class EnviosAgenciasController : ApiController
    {
        public EnviosAgenciasController() : this(new NVEntities())
        {
        }

        public EnviosAgenciasController(NVEntities db)
        {
            this.db = db;
            db.Configuration.LazyLoadingEnabled = false;
            fabricaAgenciasRemotas = new FabricaAgenciasRemotas(db);
        }

        public EnviosAgenciasController(NVEntities db, IFabricaAgenciasRemotas fabricaAgenciasRemotas) : this(db)
        {
            this.fabricaAgenciasRemotas = fabricaAgenciasRemotas;
        }

        private NVEntities db;
        private IFabricaAgenciasRemotas fabricaAgenciasRemotas;

        /*
        // GET: api/EnviosAgencias
        public IQueryable<EnviosAgencia> GetEnviosAgencias()
        {
            return db.EnviosAgencias;
        }
        */
        // GET: api/EnviosAgencias/5
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> GetEnviosAgencia(int id, string cliente)
        {
            //EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            EnviosAgencia enviosAgencia = await (from c in db.EnviosAgencias where c.Numero==id && c.Cliente==cliente select c).FirstOrDefaultAsync();
            //EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }
            
            return Ok(enviosAgencia);
        }

        // GET: api/EnviosAgencias/5
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> GetEnviosAgencia(int id)
        {
            EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }

            return Ok(enviosAgencia);
        }

        // GET: api/EnviosAgencias/5
        [ResponseType(typeof(List<EnvioAgenciaDTO>))]
        public async Task<IHttpActionResult> GetEnviosAgencia(string empresa, int pedido)
        {
            List<EnvioAgenciaDTO> enviosAgencia = await db.EnviosAgencias.Where(e => e.Empresa == empresa && e.Pedido == pedido).Include("AgenciasTransportes").Select(e => new EnvioAgenciaDTO {
                Numero = e.Numero,
                AgenciaId = e.Agencia,
                AgenciaIdentificador = e.AgenciasTransporte.Identificador,
                AgenciaNombre = e.AgenciasTransporte.Nombre,
                Estado = e.Estado,
                Retorno = e.Retorno,
                Fecha = e.Fecha,
                CodigoBarras = e.CodigoBarras,
                CodigoPostal = e.CodPostal,
                Cliente = e.Cliente,
                Pedido = (int)e.Pedido
            }).ToListAsync();
            return Ok(enviosAgencia);
        }

        // PUT: api/EnviosAgencias/5
        [ResponseType(typeof(void))]
        public async Task<IHttpActionResult> PutEnviosAgencia(int id, EnviosAgencia enviosAgencia)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            if (id != enviosAgencia.Numero)
            {
                return BadRequest();
            }

            // Issue #135: Si la etiqueta pasa a estado >= en_curso, convertir el sentinel
            // de reembolso a 0 para evitar enviar valores negativos a la agencia
            if (enviosAgencia.Estado >= Constantes.Agencias.ESTADO_EN_CURSO && enviosAgencia.Reembolso < 0)
            {
                enviosAgencia.Reembolso = 0;
            }

            enviosAgencia.Usuario = User?.Identity?.Name ?? "NestoAPI";
            db.Entry(enviosAgencia).State = EntityState.Modified;

            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EnviosAgenciaExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }


            return StatusCode(HttpStatusCode.NoContent);
        }

        // POST: api/EnviosAgencias/5/Tramitar
        // Tramita un envío con la agencia (server-side): lo inserta en su API, guarda el albarán y los
        // bultos y devuelve la etiqueta ZPL para la Zebra. Solo agencias con gestión remota (hoy
        // Innovatrans); el resto devuelve BadRequest. Idempotente: si el envío ya tiene albarán, NO
        // reinserta (evita envío fantasma + cobro doble), solo reimprime la etiqueta. Crea envío REAL.
        [HttpPost]
        [Authorize]
        [Route("api/EnviosAgencias/{id:int}/Tramitar")]
        [ResponseType(typeof(TramitarEnvioResultadoDTO))]
        public async Task<IHttpActionResult> TramitarEnvio(int id)
        {
            EnviosAgencia envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }

            IAgenciaRemota agencia = fabricaAgenciasRemotas.Crear(envio.Agencia);
            if (agencia == null)
            {
                return BadRequest($"La agencia {envio.Agencia} no tiene gestión remota en el servidor.");
            }

            // Idempotencia: si ya hay albarán, "tramitar" = reimprimir (no reinsertar).
            if (!string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                EtiquetaDataTrans reimpresion = await ReimprimirSeguro(agencia, envio.CodigoBarras.Trim());
                bool reimpresionOk = reimpresion != null && reimpresion.Exito && reimpresion.EsZpl;
                await AuditarTramitacion(envio, agencia, reimpresionOk, reimpresionOk ? null : "No se pudo reimprimir la etiqueta.");
                if (!reimpresionOk)
                {
                    // Nesto#412: si el código de barras no es un albarán real de esta agencia (p. ej.
                    // un envío que heredó el código local de OTRA agencia), la reimpresión falla
                    // SIEMPRE y el mensaje genérico no daba pista de cómo salir del bucle.
                    return Content(HttpStatusCode.BadGateway,
                        $"No se pudo reimprimir la etiqueta del albarán {envio.CodigoBarras.Trim()}. " +
                        "Si ese código no es un albarán de esta agencia (p. ej. viene de una etiqueta de otra agencia), " +
                        "borra el código de barras del envío para que se registre de nuevo con albarán propio.");
                }
                return Ok(AResultado(envio, envio.CodigoBarras.Trim(), envio.Bultos, reimpresion, reimpresion: true));
            }

            ResultadoTramitacionRemota resultado;
            try
            {
                resultado = await agencia.InsertarYEtiquetarAsync(MapearEnvioRemoto(envio));
            }
            catch (DataTransException ex)
            {
                await AuditarTramitacion(envio, agencia, false, ex.Message);
                return Content(HttpStatusCode.BadGateway, ex.Message);
            }

            // Si la agencia asignó albarán, el envío YA existe en su sistema: hay que persistirlo SIEMPRE
            // (aunque la etiqueta haya fallado) para no reinsertarlo en un reintento (envío fantasma +
            // cobro doble). Si no hubo albarán (inserción rechazada), el envío queda intacto/pendiente.
            if (!string.IsNullOrWhiteSpace(resultado.Albaran))
            {
                envio.CodigoBarras = resultado.Albaran;
                envio.Bultos = (short)resultado.Bultos;
                envio.Estado = (short)Constantes.Agencias.ESTADO_EN_CURSO;
                // Issue #135: con el envío ya tramitado, el sentinel de reembolso (<0) pasa a 0.
                if (envio.Reembolso < 0)
                {
                    envio.Reembolso = 0;
                }
                envio.Usuario = User?.Identity?.Name ?? "NestoAPI";
                envio.FechaModificacion = DateTime.Now;
                // CRÍTICO: persistir el registro (albarán/estado) ANTES de auditar; si no, un fallo de la
                // auditoría dejaría un envío registrado en la agencia pero no en nuestra BD (envío fantasma).
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbEntityValidationException ex)
                {
                    // El envío YA está registrado en la agencia (albarán resultado.Albaran) pero no se
                    // pudo guardar en nuestra BD. Logueamos el detalle de los ValidationErrors (que el
                    // mensaje genérico de EF oculta) CON el albarán, para diagnosticar el campo y poder
                    // recuperar el envío sin perderlo.
                    string detalle = DescribirErroresValidacion(ex);
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"Innovatrans registró el envío {envio.Numero} (albarán {resultado.Albaran}) pero falló al guardar en BD: {detalle}", ex)));
                    return Content(HttpStatusCode.InternalServerError,
                        $"Se registró en la agencia (albarán {resultado.Albaran}) pero no se pudo guardar en nuestra BD: {detalle}");
                }
            }

            if (!resultado.Exito)
            {
                // Dos casos: (a) inserción rechazada (sin albarán, el envío queda intacto/pendiente) o
                // (b) registrado pero sin etiqueta ZPL válida (el albarán ya quedó guardado arriba, así
                // que un reintento reimprimirá en vez de reinsertar). En ambos: BadGateway con el motivo.
                await AuditarTramitacion(envio, agencia, false, resultado.Error);
                return Content(HttpStatusCode.BadGateway, resultado.Error);
            }

            await AuditarTramitacion(envio, agencia, true, null);

            return Ok(AResultado(envio, resultado.Albaran, (short)resultado.Bultos, resultado.Etiqueta, reimpresion: false));
        }

        // POST: api/EnviosAgencias/5/Anular
        // Anula en la agencia un envío YA registrado (con albarán, Estado En curso) y lo devuelve a
        // etiqueta PENDIENTE en nuestra BD (#316): Estado=-1 y sin albarán. Así el usuario puede
        // corregir la dirección con los flujos de pendientes, re-tramitar o borrarlo con el DELETE
        // normal (que exige Estado < 0). API primero, BD después: si la agencia rechaza (p. ej.
        // ventana de edición del día cerrada), la BD queda intacta y se devuelve SU motivo tal cual.
        [HttpPost]
        [Authorize]
        [Route("api/EnviosAgencias/{id:int}/Anular")]
        [ResponseType(typeof(EnvioAgenciaDTO))]
        public async Task<IHttpActionResult> AnularEnvio(int id)
        {
            EnviosAgencia envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }

            IAgenciaRemota agencia = fabricaAgenciasRemotas.Crear(envio.Agencia);
            if (agencia == null)
            {
                return BadRequest($"La agencia {envio.Agencia} no tiene gestión remota en el servidor.");
            }

            if (string.IsNullOrWhiteSpace(envio.CodigoBarras) || envio.Estado != Constantes.Agencias.ESTADO_EN_CURSO)
            {
                return BadRequest("Solo se puede anular un envío En curso ya registrado en la agencia. " +
                    "Las etiquetas pendientes se borran directamente, sin anular.");
            }

            // El reembolso ya cobrado/pagado implica que el envío llegó a destino: anularlo en la
            // agencia dejaría la contabilidad descuadrada. Regla heredada del procedimiento manual.
            if (envio.FechaPagoReembolso != null)
            {
                return BadRequest("El reembolso de este envío ya está pagado: no se puede anular. " +
                    "Cualquier problema se gestiona como incidencia con la agencia.");
            }

            ResultadoOperacionRemota resultado;
            try
            {
                resultado = await agencia.AnularAsync(envio.CodigoBarras.Trim());
            }
            catch (DataTransException ex)
            {
                await AuditarOperacion(envio, agencia, false, ex.Message, "Anular");
                return Content(HttpStatusCode.BadGateway, ex.Message);
            }

            if (!resultado.Exito)
            {
                await AuditarOperacion(envio, agencia, false, resultado.Error, "Anular");
                return Content(HttpStatusCode.BadGateway, resultado.Error);
            }

            // Anulado en la agencia: devolver el envío a etiqueta pendiente en nuestra BD.
            string albaranAnulado = envio.CodigoBarras.Trim();
            envio.CodigoBarras = string.Empty;
            envio.Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE;
            envio.Usuario = User?.Identity?.Name ?? "NestoAPI";
            try
            {
                await db.SaveChangesAsync();
            }
            catch (DbEntityValidationException ex)
            {
                string detalle = DescribirErroresValidacion(ex);
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                    $"Innovatrans anuló el envío {envio.Numero} (albarán {albaranAnulado}) pero falló al actualizar la BD: {detalle}", ex)));
                return Content(HttpStatusCode.InternalServerError,
                    $"Se anuló en la agencia (albarán {albaranAnulado}) pero no se pudo actualizar nuestra BD: {detalle}");
            }

            await AuditarOperacion(envio, agencia, true, null, "Anular");
            return Ok(new EnvioAgenciaDTO(envio));
        }

        // POST: api/EnviosAgencias/5/Modificar
        // Modifica en la agencia un envío YA registrado (con albarán, Estado En curso) con la
        // dirección corregida y devuelve la etiqueta REIMPRESA (#317): la etiqueta lleva CP/población
        // impresos, así que modificar obliga a re-etiquetar. Para etiquetas pendientes (sin albarán)
        // ya existe ActualizarDireccionEtiqueta. API primero, BD después: los datos nuevos solo se
        // persisten si la agencia acepta la modificación.
        [HttpPost]
        [Authorize]
        [Route("api/EnviosAgencias/{id:int}/Modificar")]
        [ResponseType(typeof(TramitarEnvioResultadoDTO))]
        public async Task<IHttpActionResult> ModificarEnvio(int id, ModificarEnvioAgenciaDTO datos)
        {
            if (datos == null)
            {
                return BadRequest("Faltan los datos a modificar.");
            }

            EnviosAgencia envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }

            IAgenciaRemota agencia = fabricaAgenciasRemotas.Crear(envio.Agencia);
            if (agencia == null)
            {
                return BadRequest($"La agencia {envio.Agencia} no tiene gestión remota en el servidor.");
            }

            if (string.IsNullOrWhiteSpace(envio.CodigoBarras) || envio.Estado != Constantes.Agencias.ESTADO_EN_CURSO)
            {
                return BadRequest("Solo se puede modificar en la agencia un envío En curso ya registrado. " +
                    "Las etiquetas pendientes se corrigen con ActualizarDireccionEtiqueta.");
            }

            DatosEnvioRemoto datosRemotos = MapearEnvioRemoto(envio);
            AplicarCorrecciones(datosRemotos, datos);

            ResultadoTramitacionRemota resultado;
            try
            {
                resultado = await agencia.ModificarYEtiquetarAsync(datosRemotos, envio.CodigoBarras.Trim());
            }
            catch (DataTransException ex)
            {
                await AuditarOperacion(envio, agencia, false, ex.Message, "Modificar");
                return Content(HttpStatusCode.BadGateway, ex.Message);
            }

            // Si hay albarán en el resultado, la agencia SÍ aplicó la modificación (aunque la etiqueta
            // fallara): hay que persistir los datos nuevos SIEMPRE, para que la BD no se quede con la
            // dirección vieja de un envío que ya viaja con la corregida.
            if (!string.IsNullOrWhiteSpace(resultado.Albaran))
            {
                envio.Nombre = datosRemotos.Nombre;
                envio.Direccion = datosRemotos.Direccion;
                envio.CodPostal = datosRemotos.CodigoPostal;
                envio.Poblacion = datosRemotos.Poblacion;
                envio.Telefono = datosRemotos.Telefono;
                envio.Movil = datosRemotos.Movil;
                envio.Observaciones = datosRemotos.Observaciones;
                if (!string.IsNullOrWhiteSpace(datos.Provincia))
                {
                    envio.Provincia = datos.Provincia.Trim();
                }
                envio.Usuario = User?.Identity?.Name ?? "NestoAPI";
                try
                {
                    await db.SaveChangesAsync();
                }
                catch (DbEntityValidationException ex)
                {
                    string detalle = DescribirErroresValidacion(ex);
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"Innovatrans modificó el envío {envio.Numero} (albarán {resultado.Albaran}) pero falló al guardar en BD: {detalle}", ex)));
                    return Content(HttpStatusCode.InternalServerError,
                        $"Se modificó en la agencia (albarán {resultado.Albaran}) pero no se pudo guardar en nuestra BD: {detalle}");
                }
            }

            if (!resultado.Exito)
            {
                // Dos casos: (a) modificación rechazada (sin albarán, BD intacta) o (b) modificada
                // pero sin etiqueta ZPL (los datos ya quedaron guardados arriba; el usuario puede
                // reimprimir con Tramitar, que es idempotente). En ambos: BadGateway con el motivo.
                await AuditarOperacion(envio, agencia, false, resultado.Error, "Modificar");
                return Content(HttpStatusCode.BadGateway, resultado.Error);
            }

            await AuditarOperacion(envio, agencia, true, null, "Modificar");
            return Ok(AResultado(envio, resultado.Albaran, (short)resultado.Bultos, resultado.Etiqueta, reimpresion: false));
        }

        // Pisa en los datos remotos SOLO los campos informados del DTO (null/vacío = conservar).
        private static void AplicarCorrecciones(DatosEnvioRemoto datosRemotos, ModificarEnvioAgenciaDTO datos)
        {
            if (!string.IsNullOrWhiteSpace(datos.Nombre)) datosRemotos.Nombre = datos.Nombre.Trim();
            if (!string.IsNullOrWhiteSpace(datos.Direccion)) datosRemotos.Direccion = datos.Direccion.Trim();
            if (!string.IsNullOrWhiteSpace(datos.CodigoPostal)) datosRemotos.CodigoPostal = datos.CodigoPostal.Trim();
            if (!string.IsNullOrWhiteSpace(datos.Poblacion)) datosRemotos.Poblacion = datos.Poblacion.Trim();
            if (!string.IsNullOrWhiteSpace(datos.Telefono)) datosRemotos.Telefono = datos.Telefono.Trim();
            if (!string.IsNullOrWhiteSpace(datos.Movil)) datosRemotos.Movil = datos.Movil.Trim();
            if (!string.IsNullOrWhiteSpace(datos.Observaciones)) datosRemotos.Observaciones = datos.Observaciones.Trim();
        }

        // POST: api/EnviosAgencias/5/ActualizarSeguimiento
        // Actualiza el estado de UN envío a demanda (sin esperar al job de Hangfire que corre cada 2h):
        // consulta el seguimiento de su agencia por el albarán y persiste Estado/FechaEntrega. Reutiliza
        // la misma lógica que el job. Devuelve el seguimiento (Estado/FechaEntrega/Detalle).
        [HttpPost]
        [Authorize]
        [Route("api/EnviosAgencias/{id:int}/ActualizarSeguimiento")]
        [ResponseType(typeof(SeguimientoEnvioRemoto))]
        public async Task<IHttpActionResult> ActualizarSeguimiento(int id)
        {
            EnviosAgencia envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }
            if (string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                return BadRequest("El envío no tiene albarán todavía: no se puede consultar el seguimiento.");
            }

            ISeguimientoAgenciaRemota agencia = fabricaAgenciasRemotas.CrearSeguimiento(envio.Agencia);
            if (agencia == null)
            {
                return BadRequest("La agencia de este envío no tiene seguimiento remoto.");
            }

            SeguimientoEnvioRemoto seguimiento;
            try
            {
                seguimiento = await agencia.ConsultarSeguimientoAsync(envio.CodigoBarras.Trim());
            }
            catch (System.Exception ex)
            {
                // AuditarSeguimiento registra el fallo en AgenciasLlamadasWeb (con el SOAP crudo) y en ELMAH.
                await AuditarSeguimiento(envio, agencia, false, ex.Message);
                return Content(HttpStatusCode.BadGateway, $"No se pudo consultar el seguimiento en la agencia: {ex.Message}");
            }

            SeguimientoEnviosJobsService.AplicarSeguimiento(envio, seguimiento);
            await db.SaveChangesAsync();                            // persiste el nuevo estado (lo importante) antes de auditar
            await AuditarSeguimiento(envio, agencia, true, null);   // audita la llamada (best-effort; captura denegaciones suaves)
            return Ok(seguimiento);
        }

        // Extrae el detalle de los ValidationErrors de EF (campo + mensaje); el Message genérico de
        // DbEntityValidationException no los incluye y deja el error sin diagnosticar.
        private static string DescribirErroresValidacion(Exception ex)
        {
            DbEntityValidationException validacion = ex as DbEntityValidationException;
            if (validacion == null)
            {
                return ex.Message;
            }
            string campos = string.Join(" | ", validacion.EntityValidationErrors
                .SelectMany(e => e.ValidationErrors)
                .Select(v => $"{v.PropertyName}: {v.ErrorMessage}"));
            return string.IsNullOrEmpty(campos) ? ex.Message : campos;
        }

        private static async Task<EtiquetaDataTrans> ReimprimirSeguro(IAgenciaRemota agencia, string albaran)
        {
            try
            {
                return await agencia.ReimprimirAsync(albaran);
            }
            catch (DataTransException)
            {
                return null;
            }
        }

        private static DatosEnvioRemoto MapearEnvioRemoto(EnviosAgencia envio) => new DatosEnvioRemoto
        {
            Referencia = envio.Pedido?.ToString(),
            Nombre = envio.Nombre?.Trim(),
            Telefono = envio.Telefono?.Trim(),
            Movil = envio.Movil?.Trim(),
            CodigoPostal = envio.CodPostal?.Trim(),
            Poblacion = envio.Poblacion?.Trim(),
            Direccion = envio.Direccion?.Trim(),
            Peso = envio.Peso,
            Bultos = envio.Bultos,
            Reembolso = envio.Reembolso,
            Observaciones = envio.Observaciones?.Trim()
        };

        private Task AuditarTramitacion(EnviosAgencia envio, IAgenciaRemota agencia, bool exito, string error)
            => AuditarOperacion(envio, agencia, exito, error, "Tramitar");

        // Auditoría común de las operaciones remotas del controller (Tramitar/Anular/Modificar, #316/#317).
        private async Task AuditarOperacion(EnviosAgencia envio, IAgenciaRemota agencia, bool exito, string error, string operacion)
        {
            // NestoAPI#259: una operación fallida queda en AgenciasLlamadasWeb (Exito=false), pero
            // además la logueamos en ELMAH con contexto para detectarla desde el primer momento (antes
            // solo se veía revisando la tabla de llamadas o cuando se quejaba el almacén). Cubre todas
            // las rutas de fallo que pasan por aquí: DataTransException, BadGateway, sin ZPL, reimpresión.
            // Gobernado por el flag de logging detallado de la agencia (hoy ON solo en Innovatrans).
            if (!exito && agencia?.LoggingDetallado == true)
            {
                try
                {
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"{operacion} en agencia fallido: envío {envio.Numero} (pedido {envio.Pedido}, " +
                        $"cliente {envio.Cliente?.Trim()}, albarán {envio.CodigoBarras?.Trim()}): {error}")));
                }
                catch
                {
                    // El logueo nunca debe enmascarar ni romper la operación.
                }
            }

            // La auditoría es best-effort: si falla (validación, etc.) NUNCA debe enmascarar el error
            // real de la operación ni revertir el registro ya guardado del envío.
            try
            {
                db.AgenciasLlamadasWeb.Add(ConstruirAuditoria(agencia.Intercambios,
                    $"{operacion} envío {envio.Numero} (pedido {envio.Pedido}, CP {envio.CodPostal?.Trim()})", exito, error));
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                    $"Fallo auditando {operacion} del envío {envio.Numero}: {DescribirErroresValidacion(ex)}", ex)));
            }
        }

        // NestoAPI#259: audita en AgenciasLlamadasWeb la consulta de seguimiento a demanda (éxito y fallo),
        // igual que la tramitación. Clave: guarda el CuerpoRespuesta crudo, así que captura también las
        // denegaciones "suaves" de la agencia (p.ej. respuesta=400 con HTTP 200, que NO lanzan excepción).
        // En fallo, además loguea en ELMAH (gobernado por LoggingDetallado de la agencia). Best-effort:
        // la auditoría nunca debe romper la operación.
        private async Task AuditarSeguimiento(EnviosAgencia envio, ISeguimientoAgenciaRemota agencia, bool exito, string error)
        {
            if (!exito && agencia?.LoggingDetallado == true)
            {
                try
                {
                    Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                        $"Seguimiento a demanda fallido: envío {envio.Numero} (cliente {envio.Cliente?.Trim()}, " +
                        $"albarán {envio.CodigoBarras?.Trim()}): {error}")));
                }
                catch
                {
                    // El logueo nunca debe romper la operación.
                }
            }

            try
            {
                // Las llamadas SOAP del seguimiento se registran en Intercambios si la estrategia las
                // expone (Innovatrans, que es IAgenciaRemota); GLS sigue por su web de tracking y no.
                IReadOnlyList<IntercambioRemoto> intercambios = (agencia as IAgenciaRemota)?.Intercambios
                    ?? new List<IntercambioRemoto>();
                db.AgenciasLlamadasWeb.Add(ConstruirAuditoria(intercambios,
                    $"Consultar seguimiento envío {envio.Numero} (pedido {envio.Pedido}, albarán {envio.CodigoBarras?.Trim()})", exito, error));
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                    $"Fallo auditando el seguimiento del envío {envio.Numero}: {DescribirErroresValidacion(ex)}", ex)));
            }
        }

        // Construye la fila de auditoría de AgenciasLlamadasWeb a partir de los intercambios SOAP crudos
        // (petición/respuesta) y una cabecera de contexto. La usan tanto la tramitación como el seguimiento.
        // Las columnas son NOT NULL con longitud máxima (UrlLlamada/TextoRespuestaError 255, Usuario 30,
        // Agencia 50); CuerpoLlamada/Respuesta son nvarchar(max). Hay que respetar ambas o EF lanza
        // DbEntityValidationException.
        private AgenciaLlamadaWeb ConstruirAuditoria(IReadOnlyList<IntercambioRemoto> intercambios, string cabecera, bool exito, string error)
        {
            return new AgenciaLlamadaWeb
            {
                Agencia = Limitar("Innovatrans", 50),
                Fecha = DateTime.Now,
                Exito = exito,
                UrlLlamada = Limitar(intercambios.LastOrDefault()?.Url ?? ConfigurationManager.AppSettings["Innovatrans:Url"], 255),
                CuerpoLlamada = cabecera + "\n\n" + (Serializar(intercambios, i => i.Peticion) ?? string.Empty),
                CuerpoRespuesta = Serializar(intercambios, i => i.Respuesta) ?? string.Empty,
                TextoRespuestaError = Limitar(error, 255),
                Usuario = Limitar(User?.Identity?.Name ?? "NestoAPI", 30)
            };
        }

        private static string Serializar(IReadOnlyList<IntercambioRemoto> intercambios, Func<IntercambioRemoto, string> parte)
            => intercambios.Count == 0
                ? null
                : string.Join("\n\n", intercambios.Select(i => $"=== {i.Operacion} ===\n{parte(i)}"));

        // Recorta a la longitud máxima de la columna y nunca devuelve null (las columnas son NOT NULL).
        private static string Limitar(string valor, int maximo)
        {
            if (string.IsNullOrEmpty(valor))
            {
                return string.Empty;
            }
            return valor.Length <= maximo ? valor : valor.Substring(0, maximo);
        }

        private static TramitarEnvioResultadoDTO AResultado(EnviosAgencia envio, string albaran, short bultos, EtiquetaDataTrans etiqueta, bool reimpresion) => new TramitarEnvioResultadoDTO
        {
            Numero = envio.Numero,
            Albaran = albaran,
            Bultos = bultos,
            Reimpresion = reimpresion,
            EtiquetaTipo = etiqueta?.Tipo,
            EtiquetaCodificacion = etiqueta?.Codificacion,
            EtiquetaContenido = etiqueta?.Contenido
        };

        // POST: api/EnviosAgencias
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> PostEnviosAgencia(EnviosAgencia enviosAgencia)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.EnviosAgencias.Add(enviosAgencia);
            await db.SaveChangesAsync();

            return CreatedAtRoute("DefaultApi", new { id = enviosAgencia.Numero }, enviosAgencia);
        }
        
        /*
        [HttpPost]
        public void PostEnviosAgencia(EnviosAgencia enviosAgencia) {
            // Command line argument must the the SMTP host.
            SmtpClient client = new SmtpClient();
            client.Port = 25;
            client.Host = "smtp.nuevavision.es";
            client.EnableSsl = false;
            //client.Timeout = 10000;
            client.DeliveryMethod = SmtpDeliveryMethod.Network;
            client.UseDefaultCredentials = false;
            //client.Credentials = new System.Net.NetworkCredential("Boletin", "Madrid2010");
            string asunto = "Observación C/" + enviosAgencia.Cliente.Trim() + ", pedido " + enviosAgencia.Pedido;
            string cuerpo = 
                "Nombre: " + enviosAgencia.Nombre + "\n" +
                "Dirección: " + enviosAgencia.Direccion + "\n" +
                "Reembolso: " + enviosAgencia.Reembolso.ToString("C2") + "\n\n" +
                "Observaciones:\n" +
                enviosAgencia.Observaciones + "\n";

            MailMessage mm = new MailMessage(enviosAgencia.Email, enviosAgencia.Empresa1.Email.Trim(), 
                asunto, 
                cuerpo);
            mm.BodyEncoding = UTF8Encoding.UTF8;
            mm.DeliveryNotificationOptions = DeliveryNotificationOptions.OnFailure;

            client.Send(mm);
        }
        */


        // DELETE: api/EnviosAgencias/5
        [Authorize]
        [ResponseType(typeof(EnviosAgencia))]
        public async Task<IHttpActionResult> DeleteEnviosAgencia(int id)
        {
            EnviosAgencia enviosAgencia = await db.EnviosAgencias.FindAsync(id);
            if (enviosAgencia == null)
            {
                return NotFound();
            }

            if (enviosAgencia.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
            {
                return BadRequest("Solo se pueden eliminar etiquetas pendientes (Estado < 0)");
            }

            db.EnviosAgencias.Remove(enviosAgencia);
            await db.SaveChangesAsync();

            return Ok(enviosAgencia);
        }

        [HttpPost]
        [Authorize]
        [Route("api/EnviosAgencias/CrearEtiquetaPendiente")]
        [ResponseType(typeof(EnvioAgenciaDTO))]
        public async Task<IHttpActionResult> CrearEtiquetaPendiente(CrearEtiquetaPendienteDTO request)
        {
            if (request == null)
            {
                return BadRequest("El request no puede ser nulo");
            }

            var pedido = await db.CabPedidoVtas
                .Include(p => p.LinPedidoVtas)
                .FirstOrDefaultAsync(p => p.Empresa == request.Empresa && p.Número == request.Pedido);

            if (pedido == null)
            {
                return NotFound();
            }

            bool yaExisteEtiquetaPendiente = await db.EnviosAgencias
                .AnyAsync(e => e.Empresa == request.Empresa && e.Pedido == request.Pedido && e.Estado < Constantes.Agencias.ESTADO_EN_CURSO);

            if (yaExisteEtiquetaPendiente)
            {
                return Conflict();
            }

            var direccion = await db.Clientes
                .Include(c => c.PersonasContactoClientes)
                .FirstOrDefaultAsync(c => c.Empresa == pedido.Empresa && c.Nº_Cliente == pedido.Nº_Cliente && c.Contacto == pedido.Contacto);

            if (direccion == null)
            {
                return BadRequest("No se encontró la dirección del contacto del pedido");
            }

            // NestoAPI#204: validar combinación agencia + destino antes de crear la etiqueta.
            var errorAgencia = ValidarAgenciaCompatibleConDestino(
                request.Agencia, direccion.CodPostal?.Trim() ?? "", pedido, request.CobrarReembolso);
            if (errorAgencia != null)
            {
                return BadRequest(errorAgencia);
            }

            // Cobertura de tarifa: una agencia del comparador (GLS/Innovatrans) no puede tramitar una
            // zona en la que NO tiene tarifa (p.ej. GLS a Portugal). Mismo criterio que el comparador
            // de Nesto, para que los 3 clientes (Nesto, NestoApp, TiendasNuevaVision) se comporten igual.
            var errorCobertura = ValidarCoberturaTarifa(request.Agencia, direccion.CodPostal?.Trim() ?? "", request.Empresa);
            if (errorCobertura != null)
            {
                return BadRequest(errorCobertura);
            }

            var telefono = new Telefono(direccion.Teléfono);
            var correo = new CorreoCliente(direccion.PersonasContactoClientes);

            decimal reembolso;
            if (!request.CobrarReembolso)
            {
                reembolso = Constantes.Agencias.REEMBOLSO_NO_COBRAR;
            }
            else if (request.ImporteReembolso.HasValue)
            {
                reembolso = request.ImporteReembolso.Value;
            }
            else
            {
                reembolso = 0;
            }

            var codPostal = direccion.CodPostal?.Trim() ?? "";
            var defaultsAgencia = ObtenerDefaultsAgencia(request.Agencia, codPostal);

            // NestoAPI#310: red de seguridad — la casilla "Recoger producto" (Nesto/NestoApp) crea
            // esta etiqueta con Retorno > 0, pero el repartidor se entera por los comentarios de
            // ruta. Si el usuario no lo escribió, se añade aquí SIN pisar lo que hubiera.
            if (request.Retorno > 0)
            {
                pedido.Comentarios = AnadirAvisoRecogerProducto(pedido.Comentarios);
            }

            var envio = new EnviosAgencia
            {
                Empresa = request.Empresa,
                Pedido = request.Pedido,
                Cliente = pedido.Nº_Cliente,
                Contacto = pedido.Contacto,
                Agencia = request.Agencia,
                Estado = (short)Constantes.Agencias.ESTADO_PENDIENTE,
                Retorno = request.Retorno,
                Reembolso = reembolso,
                Fecha = DateTime.Today,
                FechaModificacion = DateTime.Now,
                Bultos = 0,
                Servicio = defaultsAgencia.Servicio,
                Horario = defaultsAgencia.Horario,
                Nombre = direccion.Nombre?.Trim() ?? "",
                Direccion = direccion.Dirección?.Trim() ?? "",
                CodPostal = codPostal,
                Poblacion = direccion.Población?.Trim() ?? "",
                Provincia = direccion.Provincia?.Trim() ?? "",
                Telefono = telefono.FijoUnico(),
                Movil = telefono.MovilUnico(),
                Email = correo.CorreoAgencia(),
                Atencion = direccion.Nombre?.Trim() ?? "",
                // NestoAPI#309: Observaciones es varchar(80); copiar Comentarios sin truncar
                // reventaba SaveChanges con DbEntityValidationException cuando eran más largos.
                Observaciones = TruncarObservaciones(pedido.Comentarios),
                FechaEntrega = pedido.Fecha,
                Pais = defaultsAgencia.Pais,
                Usuario = User?.Identity?.Name ?? "NestoAPI"
            };

            db.EnviosAgencias.Add(envio);
            await db.SaveChangesAsync();

            var dto = new EnvioAgenciaDTO(envio);
            return Created(new Uri(Request.RequestUri, envio.Numero.ToString()), dto);
        }

        // NestoAPI#310: texto que se añade a los comentarios de ruta como red de seguridad
        internal const string AVISO_RECOGER_PRODUCTO = "Recoger producto";
        private const int LONGITUD_MAXIMA_OBSERVACIONES = 80; // EnviosAgencia.Observaciones es varchar(80)

        /// <summary>
        /// NestoAPI#310: añade "Recoger producto" a los comentarios de ruta si no lo contienen ya
        /// (case-insensitive). Solo añade al final, nunca pisa ni borra lo que hubiera.
        /// </summary>
        internal static string AnadirAvisoRecogerProducto(string comentarios)
        {
            if (comentarios != null && comentarios.IndexOf(AVISO_RECOGER_PRODUCTO, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return comentarios;
            }
            if (string.IsNullOrWhiteSpace(comentarios))
            {
                return AVISO_RECOGER_PRODUCTO;
            }
            string existente = comentarios.TrimEnd();
            string separador = existente.EndsWith(".") ? " " : ". ";
            return existente + separador + AVISO_RECOGER_PRODUCTO;
        }

        internal static string TruncarObservaciones(string comentarios)
        {
            if (comentarios == null || comentarios.Length <= LONGITUD_MAXIMA_OBSERVACIONES)
            {
                return comentarios;
            }
            return comentarios.Substring(0, LONGITUD_MAXIMA_OBSERVACIONES);
        }

        /// <summary>
        /// NestoAPI#204: rechazos por combinación agencia + destino. Devuelve mensaje de error o null
        /// si la combinación es válida. Cubre:
        /// - CEX no entrega en Canarias → forzar Canteras.
        /// - Canteras solo opera en Canarias.
        /// - Canteras es operativa manual y no admite contra reembolso.
        /// - Canteras exige importe del pedido ≥ <see cref="Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS"/>
        ///   o una línea de portes ≥ <see cref="Constantes.Portes.CANARIAS"/>.
        /// </summary>
        private static string ValidarAgenciaCompatibleConDestino(int agencia, string codPostal, CabPedidoVta pedido, bool cobrarReembolso)
        {
            bool esCanarias = Infraestructure.PedidosVenta.GestorPortes.EsCanarias(codPostal);

            if (agencia == Constantes.Agencias.AGENCIA_CORREOS_EXPRESS && esCanarias)
            {
                return "Correos Express no entrega en Canarias. Usa la agencia Canteras para envíos a Canarias.";
            }

            if (agencia == Constantes.Agencias.AGENCIA_CANTERAS)
            {
                if (!esCanarias)
                {
                    return "La agencia Canteras solo opera en Canarias (códigos postales 35xxx y 38xxx).";
                }
                if (cobrarReembolso)
                {
                    return "La agencia Canteras no admite contra reembolso. Cobra el pedido por otro medio antes de tramitar el envío.";
                }

                decimal baseImponiblePedido = pedido.LinPedidoVtas?
                    .Where(l => l.TipoLinea == Constantes.TiposLineaVenta.PRODUCTO)
                    .Sum(l => l.Base_Imponible) ?? 0M;
                bool llevaLineaPortesCanarias = pedido.LinPedidoVtas?
                    .Any(l => l.TipoLinea == Constantes.TiposLineaVenta.CUENTA_CONTABLE
                        && l.Producto != null
                        && l.Producto.Trim().StartsWith("624")
                        && l.Base_Imponible >= Constantes.Portes.CANARIAS) ?? false;

                if (baseImponiblePedido < Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS
                    && !llevaLineaPortesCanarias)
                {
                    return $"El pedido no llega al mínimo de Canarias ({Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS:N0} €) " +
                           $"y no lleva una línea de portes de {Constantes.Portes.CANARIAS:N0} €. Añade portes o aumenta el importe.";
                }
            }

            return null;
        }

        /// <summary>
        /// Requisito Innovatrans: una agencia gestionada por el comparador (GLS/Innovatrans) NO puede
        /// crear una etiqueta para una zona en la que no tiene tarifa portada (p.ej. GLS a Portugal,
        /// que solo cubre Provincial/Peninsular/Baleares). Devuelve el mensaje de error o null si la
        /// combinación es válida. Las agencias FUERA del comparador (Canteras manual, CEX/Sending en
        /// cuarentena, OnTime) tienen su propia lógica y NO se validan aquí.
        /// </summary>
        private string ValidarCoberturaTarifa(int agencia, string codPostal, string empresa)
        {
            bool esAgenciaComparador = new RegistroTarifas().Todas().Any(t => t.AgenciaId == agencia);
            if (!esAgenciaComparador)
            {
                return null;
            }

            var numerosExistentes = db.AgenciasTransportes.Select(a => a.Numero).Distinct().ToList();
            var idsSombra = db.AgenciasTransportes.Where(a => a.EsSombra).Select(a => a.Numero).ToList();
            var registro = new RegistroTarifasExistentes(new RegistroTarifas(), numerosExistentes);
            var comparador = new ComparadorAgencias(registro, new ProveedorRecargoCombustibleEF(db), idsSombra);

            // peso/reembolso 0: la cobertura depende solo de que la zona tenga tramos en la tarifa.
            if (comparador.CosteDeAgencia(empresa, codPostal, 0m, 0m, agencia) == null)
            {
                return $"La agencia seleccionada no tiene tarifa para la zona del destino (CP {codPostal}). No se puede crear la etiqueta.";
            }
            return null;
        }

        private static (short Servicio, short Horario, int Pais) ObtenerDefaultsAgencia(int agencia, string codPostal)
        {
            switch (agencia)
            {
                case Constantes.Agencias.AGENCIA_GLS:
                    return (Servicio: 96, Horario: 18, Pais: 34); // BusinessParcel, Economy, España
                case Constantes.Agencias.AGENCIA_CORREOS_EXPRESS:
                    if (EsCodigoPostalPortugues(codPostal))
                    {
                        return (Servicio: 63, Horario: 0, Pais: 724); // Paq24, Portugal
                    }
                    if (EsCodigoPostalEspanol(codPostal))
                    {
                        return (Servicio: 93, Horario: 0, Pais: 724); // ePaq24, España
                    }
                    return (Servicio: 90, Horario: 0, Pais: 724); // Internacional monobulto
                case Constantes.Agencias.AGENCIA_SENDING:
                    return (Servicio: 1, Horario: 1, Pais: 34); // Send Express, Normal, España
                default:
                    return (Servicio: 0, Horario: 0, Pais: 1);
            }
        }

        private static bool EsCodigoPostalEspanol(string codPostal)
        {
            return codPostal.Length == 5 && int.TryParse(codPostal, out int cp) && cp >= 1000 && cp <= 52999;
        }

        private static bool EsCodigoPostalPortugues(string codPostal)
        {
            // Formato portugués: 4 dígitos o 4 dígitos-3 dígitos (ej: "1000" o "1000-001")
            var sinGuion = codPostal.Replace("-", "");
            return (codPostal.Length == 4 || codPostal.Length == 8)
                && int.TryParse(sinGuion, out int cp) && cp >= 1000 && cp <= 9999999;
        }

        [HttpPut]
        [Authorize]
        [Route("api/EnviosAgencias/ActualizarDireccionEtiqueta/{id}")]
        public async Task<IHttpActionResult> ActualizarDireccionEtiqueta(int id, ActualizarDireccionEtiquetaDTO request)
        {
            if (request == null)
            {
                return BadRequest("El request no puede ser nulo");
            }

            var envio = await db.EnviosAgencias.FindAsync(id);
            if (envio == null)
            {
                return NotFound();
            }

            if (envio.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
            {
                return BadRequest("Solo se puede actualizar la dirección de etiquetas pendientes");
            }

            var direccion = await db.Clientes
                .Include(c => c.PersonasContactoClientes)
                .FirstOrDefaultAsync(c => c.Empresa == request.Empresa && c.Nº_Cliente == request.Cliente && c.Contacto == request.Contacto);

            if (direccion == null)
            {
                return BadRequest("No se encontró la dirección del contacto");
            }

            var telefono = new Telefono(direccion.Teléfono);
            var correo = new CorreoCliente(direccion.PersonasContactoClientes);

            envio.Contacto = request.Contacto;
            envio.Nombre = direccion.Nombre?.Trim() ?? "";
            envio.Direccion = direccion.Dirección?.Trim() ?? "";
            envio.CodPostal = direccion.CodPostal?.Trim() ?? "";
            envio.Poblacion = direccion.Población?.Trim() ?? "";
            envio.Provincia = direccion.Provincia?.Trim() ?? "";
            envio.Telefono = telefono.FijoUnico();
            envio.Movil = telefono.MovilUnico();
            envio.Email = correo.CorreoAgencia();
            envio.Atencion = direccion.Nombre?.Trim() ?? "";
            envio.Usuario = User?.Identity?.Name ?? "NestoAPI";
            envio.FechaModificacion = DateTime.Now;

            await db.SaveChangesAsync();

            return Ok(new EnvioAgenciaDTO(envio));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool EnviosAgenciaExists(int id)
        {
            return db.EnviosAgencias.Count(e => e.Numero == id) > 0;
        }

        // Anónimo a propósito (Issue #189): se llama desde navegador con CORS abierto (página
        // externa de entrega/seguimiento), sin sesión de NestoAPI.
        [AllowAnonymous]
        [HttpPost]
        [EnableCors(origins: "*", headers: "*", methods: "*", SupportsCredentials = true)]
        [Route("api/EnviosAgencias/EnviarCorreoEntregaAgencia")]
        public async Task EnviarCorreoEntregaAgencia(EnviosAgencia envio)
        {
            GestorEnviosAgencia gestor = new GestorEnviosAgencia();

            await gestor.EnviarCorreoEntregaAgencia(envio);
        }

        /// <summary>
        /// Obtiene el último envío de un cliente con información de seguimiento.
        /// Issue #70 - Para TiendasNuevaVision
        /// </summary>
        /// <param name="empresa">Código de empresa</param>
        /// <param name="cliente">Código de cliente</param>
        /// <returns>Información del último envío con URL de seguimiento</returns>
        [HttpGet]
        [Authorize]
        [Route("api/EnviosAgencias/UltimoEnvioCliente")]
        [ResponseType(typeof(UltimoEnvioClienteDTO))]
        public async Task<IHttpActionResult> GetUltimoEnvioCliente(string empresa, string cliente)
        {
            if (string.IsNullOrWhiteSpace(empresa) || string.IsNullOrWhiteSpace(cliente))
            {
                return BadRequest("Los parámetros empresa y cliente son obligatorios");
            }

            // Validación de seguridad según tipo de usuario
            var identity = User.Identity as ClaimsIdentity;
            var validacion = ValidadorAccesoCliente.ValidarAcceso(identity, cliente);
            if (!validacion.Autorizado)
            {
                return Unauthorized();
            }

            var ultimoEnvio = await db.EnviosAgencias
                .Where(e => e.Empresa == empresa &&
                            e.Cliente == cliente &&
                            e.CodigoBarras != null &&
                            e.Estado >= Constantes.Agencias.ESTADO_EN_CURSO)
                .OrderByDescending(e => e.Fecha)
                .ThenByDescending(e => e.Numero)
                .Include(e => e.AgenciasTransporte)
                .Select(e => new UltimoEnvioClienteDTO
                {
                    Pedido = e.Pedido ?? 0,
                    Fecha = e.Fecha,
                    FechaEntrega = e.FechaEntrega,
                    AgenciaId = e.Agencia,
                    AgenciaNombre = e.AgenciasTransporte.Nombre,
                    AgenciaIdentificador = e.AgenciasTransporte.Identificador,
                    NumeroSeguimiento = e.CodigoBarras,
                    CodigoPostal = e.CodPostal,
                    Cliente = e.Cliente,
                    Estado = e.Estado,
                    Bultos = e.Bultos,
                    Observaciones = e.Observaciones
                })
                .FirstOrDefaultAsync();

            if (ultimoEnvio == null)
            {
                return NotFound();
            }

            return Ok(ultimoEnvio);
        }

    }
}