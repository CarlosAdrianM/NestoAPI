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
                bool reimpresionOk = reimpresion != null && reimpresion.Exito;
                await AuditarTramitacion(envio, agencia, reimpresionOk, reimpresionOk ? null : "No se pudo reimprimir la etiqueta.");
                if (!reimpresionOk)
                {
                    return Content(HttpStatusCode.BadGateway, "No se pudo reimprimir la etiqueta del envío ya tramitado.");
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

            if (!resultado.Exito)
            {
                await AuditarTramitacion(envio, agencia, false, resultado.Error);
                return Content(HttpStatusCode.BadGateway, resultado.Error);
            }

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
            await AuditarTramitacion(envio, agencia, true, null);

            return Ok(AResultado(envio, resultado.Albaran, (short)resultado.Bultos, resultado.Etiqueta, reimpresion: false));
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

        private async Task AuditarTramitacion(EnviosAgencia envio, IAgenciaRemota agencia, bool exito, string error)
        {
            // La auditoría es best-effort: si falla (validación, etc.) NUNCA debe enmascarar el error
            // real de la tramitación ni revertir el registro ya guardado del envío.
            try
            {
                db.AgenciasLlamadasWeb.Add(ConstruirAuditoria(envio, agencia, exito, error));
                await db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                Elmah.ErrorLog.GetDefault(null)?.Log(new Elmah.Error(new Exception(
                    $"Fallo auditando la tramitación del envío {envio.Numero}: {DescribirErroresValidacion(ex)}", ex)));
            }
        }

        private AgenciaLlamadaWeb ConstruirAuditoria(EnviosAgencia envio, IAgenciaRemota agencia, bool exito, string error)
        {
            // Las columnas de AgenciasLlamadasWeb son NOT NULL y varias tienen longitud máxima
            // (UrlLlamada/TextoRespuestaError 255, Usuario 30, Agencia 50); CuerpoLlamada/Respuesta son
            // nvarchar(max). Hay que respetar ambas o EF lanza DbEntityValidationException.
            var intercambios = agencia.Intercambios;
            return new AgenciaLlamadaWeb
            {
                Agencia = Limitar("Innovatrans", 50),
                Fecha = DateTime.Now,
                Exito = exito,
                UrlLlamada = Limitar(intercambios.LastOrDefault()?.Url ?? ConfigurationManager.AppSettings["Innovatrans:Url"], 255),
                // SOAP crudo de cada intercambio (insertar + etiquetar). Con la cabecera del envío
                // para no perder el contexto al revisar la auditoría.
                CuerpoLlamada = $"Tramitar envío {envio.Numero} (pedido {envio.Pedido}, CP {envio.CodPostal?.Trim()})\n\n"
                    + (Serializar(intercambios, i => i.Peticion) ?? string.Empty),
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
                Observaciones = pedido.Comentarios,
                FechaEntrega = pedido.Fecha,
                Pais = defaultsAgencia.Pais,
                Usuario = User?.Identity?.Name ?? "NestoAPI"
            };

            db.EnviosAgencias.Add(envio);
            await db.SaveChangesAsync();

            var dto = new EnvioAgenciaDTO(envio);
            return Created(new Uri(Request.RequestUri, envio.Numero.ToString()), dto);
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