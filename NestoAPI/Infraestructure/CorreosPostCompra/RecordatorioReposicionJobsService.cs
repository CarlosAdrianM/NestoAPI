using Hangfire;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infraestructure.SugerenciasProductos;
using NestoAPI.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>Nivel del interruptor del recordatorio de reposición (NestoAPI#532).</summary>
    public enum ModoRecordatorioReposicion
    {
        /// <summary>No hace nada. Es el valor por defecto (sin fila, "0" o cualquier cosa no reconocida).</summary>
        Apagado,
        /// <summary>Calcula la lista y la manda SOLO al equipo interno. No escribe a ningún cliente.</summary>
        Sombra,
        /// <summary>
        /// Escribe a los clientes que tocan cuyo vendedor está en RecordatorioReposicionVendedores, los registra, y manda
        /// también el resumen al equipo interno.
        /// </summary>
        Activo
    }

    /// <summary>NestoAPI#532: lo que necesita el job, inyectable para testearlo sin BD, SMTP, PrestaShop ni OpenAI.</summary>
    public class DependenciasRecordatorioReposicion
    {
        public ILectorParametrosUsuario Lector { get; set; }
        public IServicioCorreoElectronico Correo { get; set; }
        /// <summary>(consumibles, hoy) → el cálculo de <see cref="SelectorRecordatoriosReposicion.Calcular"/>.</summary>
        public Func<string, DateTime, Task<ResultadoRecordatorioReposicionDTO>> Calcular { get; set; }
        /// <summary>Rellena el enlace a la tienda y la foto de cada producto del correo.</summary>
        public Func<RecordatorioReposicionClienteDTO, Task> RellenarProductos { get; set; }
        /// <summary>Registro de envíos, textos y bajas. Null o sin tablas: no se escribe a ningún cliente (como mucho, sombra).</summary>
        public IRepositorioRecordatorioReposicion Repositorio { get; set; }
        /// <summary>El texto de la semana. Null: la plantilla fija.</summary>
        public IGeneradorTextoRecordatorioReposicion GeneradorTexto { get; set; }
        /// <summary>Del resumen (sombra).</summary>
        public string Destinatarios { get; set; }
        /// <summary>From y Reply-To del correo al cliente (el buzón de los correos posventa).</summary>
        public string Remitente { get; set; }
        /// <summary>Entre correo y correo a clientes (Office 365 admite unos 30 por minuto).</summary>
        public TimeSpan PausaEntreCorreos { get; set; }
        public DateTime Hoy { get; set; }
        public Func<DateTime> Ahora { get; set; } = () => DateTime.Now;
        /// <summary>NestoAPI#736: la P1 del modelo de propensión para la puerta. Null: no se puntúa (como antes de #736).</summary>
        public IPuntuadorReposicion Puntuador { get; set; }
        /// <summary>NestoAPI#736: a ELMAH (null: ElmahHelper).</summary>
        public Action<Exception> RegistrarError { get; set; }
    }

    /// <summary>NestoAPI#532: lo que ha hecho una pasada (para los tests, el resumen y el endpoint).</summary>
    public class EjecucionRecordatorioReposicionDTO
    {
        /// <summary>El que se ha aplicado (un Activo sin tablas de registro baja a Sombra).</summary>
        public ModoRecordatorioReposicion Modo { get; set; }
        public ModoRecordatorioReposicion ModoParametro { get; set; }
        public ResultadoRecordatorioReposicionDTO Resultado { get; set; }
        public PlanRecordatorioReposicionDTO Plan { get; set; } = new PlanRecordatorioReposicionDTO();
        public TextoRecordatorioReposicion Texto { get; set; }
        /// <summary>Algo que el equipo tiene que saber (p. ej. que falta el script y no se ha escrito a nadie).</summary>
        public string Aviso { get; set; }
        public bool ResumenEnviado { get; set; }
        /// <summary>NestoAPI#736: lo que ha hecho la puerta del modelo de propensión.</summary>
        public ResumenPuertaModeloDTO PuertaModelo { get; set; } = new ResumenPuertaModeloDTO();
        /// <summary>NestoAPI#736: conversión de los envíos de ventana cerrada (null si no hay registro o no se ha podido leer).</summary>
        public InformeConversionReposicionDTO Conversion { get; set; }
    }

    /// <summary>
    /// NestoAPI#532: job semanal del recordatorio de reposición. El criterio vive en <see cref="CalculadoraReposicion"/>, la
    /// lectura en <see cref="SelectorRecordatoriosReposicion"/>, qué se hace con el cálculo en
    /// <see cref="ReglasEnvioRecordatorioReposicion"/>, el texto que cambia cada semana en
    /// <see cref="GeneradorTextoRecordatorioReposicion"/> y el correo en <see cref="PlantillaRecordatorioReposicion"/>.
    ///
    /// Interruptor: parámetro <c>RecordatorioReposicion</c> bajo «(defecto)» en ParámetrosUsuario. Sin fila o valor no
    /// reconocido = APAGADO. "Sombra": cada jueves UN correo a Informática con lo que se haría. "Activo": además se escribe
    /// de verdad a los clientes cuyo vendedor esté en <c>RecordatorioReposicionVendedores</c> (el grupo de control nunca), se
    /// registra cada envío (RecordatoriosReposicionEnvios) y el resumen dice qué se ha enviado y qué se ha quedado fuera.
    /// </summary>
    public static class RecordatorioReposicionJobsService
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        internal const string VALOR_SOMBRA = "Sombra";
        internal const string VALOR_ACTIVO = "Activo";
        internal const int MAXIMO_FILAS_DESCARTES = 300;
        internal const string REMITENTE_POR_DEFECTO = "nuevavision@nuevavision.es";
        /// <summary>
        /// Copia oculta de cada correo que sale a un cliente (Carlos, 22/10/26: para ver lo que llega de verdad). Para quitarla,
        /// dejarla en null o vacía. No va en el resumen interno ni en la sombra.
        /// </summary>
        internal const string COPIA_OCULTA_CORREOS_CLIENTE = Constantes.Correos.INFORMATICA;
        internal static readonly TimeSpan PAUSA_ENTRE_CORREOS = TimeSpan.FromSeconds(2.5);
        private static readonly System.Globalization.CultureInfo ES = System.Globalization.CultureInfo.GetCultureInfo("es-ES");

        internal const string AVISO_SIN_TABLAS = "Está en Activo, pero faltan las tablas de registro (Scripts/Issue532_RecordatorioReposicionActivo.sql) " +
            "o no se han podido leer: sin registro se repetirían los correos, así que esta semana NO se ha escrito a ningún cliente.";

        // Sin reintentos: si falla, la semana que viene vuelve a pasar (lo ya enviado está registrado y no se repite).
        [AutomaticRetry(Attempts = 0)]
        [DisableConcurrentExecution(timeoutInSeconds: 3600)]
        public static async Task Procesar()
        {
            try
            {
                using (NVEntities db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    var lector = new LectorParametrosUsuario();
                    _ = await Procesar(new DependenciasRecordatorioReposicion
                    {
                        Lector = lector,
                        Correo = new ServicioCorreoElectronico(),
                        Calcular = (consumibles, hoy) => new SelectorRecordatoriosReposicion(db, topeDias: ParametrosRecordatorioReposicion.LeerTopeDias(lector))
                            .Calcular(EMPRESA, hoy, consumibles),
                        Puntuador = CrearPuntuador(db),
                        RellenarProductos = CrearRellenadorProductos(),
                        Repositorio = new RepositorioRecordatorioReposicionSql(db),
                        GeneradorTexto = new GeneradorTextoRecordatorioReposicion(new ServicioOpenAI()),
                        Destinatarios = DestinatariosSombra(),
                        Remitente = Remitente(),
                        PausaEntreCorreos = PAUSA_ENTRE_CORREOS,
                        Hoy = DateTime.Today
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] Error en el job: " + ex.Message, ex));
                throw;
            }
        }

        /// <summary>NestoAPI#736: la P1 del modelo 1 de las sugerencias (el mismo zip, interruptor y caché que la plantilla).</summary>
        internal static IPuntuadorReposicion CrearPuntuador(NVEntities db)
            => new PuntuadorReposicionModelo(new RepositorioSugerenciasProductosSql(db), PredictorSugerenciasProductos.Instancia);

        /// <summary>Núcleo del job. Apagado no consulta nada.</summary>
        internal static async Task<EjecucionRecordatorioReposicionDTO> Procesar(DependenciasRecordatorioReposicion d)
        {
            ModoRecordatorioReposicion modo = LeerModo(d.Lector);
            if (modo == ModoRecordatorioReposicion.Apagado)
            {
                return new EjecucionRecordatorioReposicionDTO { Modo = modo, ModoParametro = modo };
            }
            ResultadoRecordatorioReposicionDTO resultado = await d.Calcular(LeerConsumibles(d.Lector), d.Hoy).ConfigureAwait(false);
            return await Ejecutar(modo, resultado, d).ConfigureAwait(false);
        }

        /// <summary>
        /// Con el cálculo hecho: texto de la semana, reglas, envíos (solo en Activo) y resumen. El endpoint para lanzar la
        /// sombra a mano lo llama con <see cref="ModoRecordatorioReposicion.Sombra"/>.
        /// </summary>
        internal static async Task<EjecucionRecordatorioReposicionDTO> Ejecutar(ModoRecordatorioReposicion modo,
            ResultadoRecordatorioReposicionDTO resultado, DependenciasRecordatorioReposicion d)
        {
            resultado = resultado ?? new ResultadoRecordatorioReposicionDTO { Fecha = d.Hoy.Date };
            var ejecucion = new EjecucionRecordatorioReposicionDTO { Modo = modo, ModoParametro = modo, Resultado = resultado };
            ejecucion.Texto = await GenerarTexto(d).ConfigureAwait(false);

            BajasCorreosPostCompra bajas = null;
            List<EnvioPrevioReposicion> envios = null;
            bool hayRegistro = false;
            try
            {
                if (d.Repositorio != null && await d.Repositorio.ExistenTablas().ConfigureAwait(false))
                {
                    bajas = await d.Repositorio.LeerBajas(EMPRESA).ConfigureAwait(false);
                    envios = await d.Repositorio.LeerEnviosDesde(EMPRESA, CalculadoraReposicion.HistorialDesde(d.Hoy)).ConfigureAwait(false);
                    hayRegistro = true;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] No se ha podido leer el registro: " + ex.Message, ex));
            }
            if (modo == ModoRecordatorioReposicion.Activo && !hayRegistro)
            {
                ejecucion.Modo = ModoRecordatorioReposicion.Sombra;
                ejecucion.Aviso = AVISO_SIN_TABLAS;
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] " + AVISO_SIN_TABLAS));
            }

            ejecucion.Plan = ReglasEnvioRecordatorioReposicion.Planificar(resultado, LeerVendedores(d.Lector), bajas, envios, d.Hoy,
                LeerVendedoresPeluqueriaExcluidos(d.Lector));

            // NestoAPI#736: la puerta del modelo, después de las reglas (a lo que se enviaría y al grupo de control registrado)
            ejecucion.PuertaModelo = await PuertaModeloRecordatorioReposicion.Aplicar(ejecucion.Plan, resultado,
                ParametrosRecordatorioReposicion.LeerModoPuerta(d.Lector), ParametrosRecordatorioReposicion.LeerUmbralModelo(d.Lector),
                d.Puntuador, EMPRESA, d.Hoy, d.RegistrarError).ConfigureAwait(false);

            if (hayRegistro)
            {
                ejecucion.Conversion = await LeerConversion(d).ConfigureAwait(false);
            }

            if (ejecucion.Modo == ModoRecordatorioReposicion.Activo)
            {
                await EnviarAClientes(ejecucion, d).ConfigureAwait(false);
            }
            else
            {
                RecordatorioReposicionClienteDTO muestra = ejecucion.Plan.Enviar.FirstOrDefault()
                    ?? ejecucion.Plan.FueraPorVendedor.FirstOrDefault();
                await Rellenar(d, muestra).ConfigureAwait(false);
            }

            ejecucion.ResumenEnviado = EnviarResumen(ejecucion, d.Correo, d.Destinatarios);
            return ejecucion;
        }

        /// <summary>
        /// NestoAPI#736: la conversión (ventana de <see cref="InformeConversionRecordatorioReposicion.DIAS_VENTANA_POR_DEFECTO"/>
        /// días) de los envíos de los últimos <see cref="InformeConversionRecordatorioReposicion.MESES_POR_DEFECTO"/> meses cuya
        /// ventana ya se ha cerrado. Null si no hay ninguno o falla (a ELMAH): el resumen sale igual.
        /// </summary>
        internal static async Task<InformeConversionReposicionDTO> LeerConversion(DependenciasRecordatorioReposicion d)
        {
            int dias = InformeConversionRecordatorioReposicion.DIAS_VENTANA_POR_DEFECTO;
            DateTime desde = d.Hoy.Date.AddMonths(-InformeConversionRecordatorioReposicion.MESES_POR_DEFECTO);
            DateTime hasta = d.Hoy.Date.AddDays(1 - dias); // exclusivo: las de ventana cerrada
            if (hasta <= desde)
            {
                return null;
            }
            try
            {
                List<FilaConversionReposicion> filas = await d.Repositorio.LeerConversion(EMPRESA, desde, hasta, dias).ConfigureAwait(false);
                InformeConversionReposicionDTO informe = InformeConversionRecordatorioReposicion.Calcular(filas, desde, dias, d.Hoy);
                return informe.EnviosCerrados > 0 ? informe : null;
            }
            catch (Exception ex)
            {
                (d.RegistrarError ?? ElmahHelper.Log)(new Exception("[Recordatorio reposición #736] No se ha podido leer la conversión: " + ex.Message, ex));
                return null;
            }
        }

        private static async Task<TextoRecordatorioReposicion> GenerarTexto(DependenciasRecordatorioReposicion d)
        {
            if (d.GeneradorTexto == null)
            {
                return TextoRecordatorioReposicion.Fija();
            }
            try
            {
                return await d.GeneradorTexto.Generar(d.Hoy.Date).ConfigureAwait(false) ?? TextoRecordatorioReposicion.Fija();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] Se usa el texto fijo: " + ex.Message, ex));
                return TextoRecordatorioReposicion.Fija(ex.Message);
            }
        }

        private static async Task Rellenar(DependenciasRecordatorioReposicion d, RecordatorioReposicionClienteDTO correo)
        {
            if (correo == null || d.RellenarProductos == null)
            {
                return;
            }
            try
            {
                await d.RellenarProductos(correo).ConfigureAwait(false);
            }
            catch
            {
                // Sin enlace ni foto el correo sigue sirviendo: el nombre del producto va igual
            }
        }

        /// <summary>
        /// Modo Activo: un correo por cliente de <see cref="PlanRecordatorioReposicionDTO.Enviar"/>, registrado en cuanto sale
        /// (si el job se cae a medias, lo ya enviado no se repite). Después se registra el grupo de control.
        /// </summary>
        private static async Task EnviarAClientes(EjecucionRecordatorioReposicionDTO ejecucion, DependenciasRecordatorioReposicion d)
        {
            PlanRecordatorioReposicionDTO plan = ejecucion.Plan;
            int? textoId = null;
            try
            {
                textoId = await d.Repositorio.GuardarTexto(EMPRESA, d.Hoy.Date, ejecucion.Texto).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] No se ha podido guardar el texto de la semana: " + ex.Message, ex));
            }

            bool primero = true;
            foreach (RecordatorioReposicionClienteDTO cliente in plan.Enviar)
            {
                if (!primero && d.PausaEntreCorreos > TimeSpan.Zero)
                {
                    await Task.Delay(d.PausaEntreCorreos).ConfigureAwait(false);
                }
                primero = false;

                await Rellenar(d, cliente).ConfigureAwait(false);
                bool enviado;
                try
                {
                    using (MailMessage correo = ConstruirCorreoCliente(cliente, ejecucion.Texto, d.Remitente, d.Hoy))
                    {
                        enviado = d.Correo.EnviarCorreoSMTP(correo);
                    }
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Recordatorio reposición #532] Cliente {cliente.Cliente}: {ex.Message}", ex));
                    enviado = false;
                }
                if (!enviado)
                {
                    plan.Fallidos.Add(cliente);
                    continue;
                }
                plan.Enviados.Add(cliente);
                await Registrar(d, cliente, RepositorioRecordatorioReposicionSql.GRUPO_ENVIADO, textoId, ejecucion.Texto).ConfigureAwait(false);
            }
            if (plan.Fallidos.Any())
            {
                ElmahHelper.Log(new Exception($"[Recordatorio reposición #532] No han salido {plan.Fallidos.Count} correos: " +
                    string.Join(", ", plan.Fallidos.Select(f => f.Cliente))));
            }

            foreach (RecordatorioReposicionClienteDTO control in plan.Control)
            {
                await Registrar(d, control, RepositorioRecordatorioReposicionSql.GRUPO_CONTROL, textoId, ejecucion.Texto).ConfigureAwait(false);
            }
        }

        private static async Task Registrar(DependenciasRecordatorioReposicion d, RecordatorioReposicionClienteDTO cliente, string grupo,
            int? textoId, TextoRecordatorioReposicion texto)
        {
            try
            {
                await d.Repositorio.RegistrarEnvio(EMPRESA, cliente, grupo, d.Ahora(), textoId, texto.Version).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Grave: sin la fila, la semana que viene se le volvería a escribir
                ElmahHelper.Log(new Exception($"[Recordatorio reposición #532] No se ha podido registrar ({grupo}) al cliente " +
                    $"{cliente.Cliente}: {ex.Message}", ex));
            }
        }

        /// <summary>El correo al cliente: del buzón de los correos posventa, que es también a donde contesta.</summary>
        internal static MailMessage ConstruirCorreoCliente(RecordatorioReposicionClienteDTO cliente, TextoRecordatorioReposicion texto,
            string remitente, DateTime hoy, string copiaOculta = COPIA_OCULTA_CORREOS_CLIENTE)
        {
            string buzon = string.IsNullOrWhiteSpace(remitente) ? REMITENTE_POR_DEFECTO : remitente.Trim();
            string asunto = PlantillaRecordatorioReposicion.Asunto(cliente, texto);
            var mail = new MailMessage
            {
                From = new MailAddress(buzon, "El equipo de Nueva Visión"),
                Subject = asunto,
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8,
                Body = "<!DOCTYPE html>\n<html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width, initial-scale=1.0'>" +
                    $"<title>{WebUtility.HtmlEncode(asunto)}</title></head>\n<body style='margin: 0; padding: 12px;'>\n" +
                    PlantillaRecordatorioReposicion.CuerpoHtml(cliente, texto, hoy) + "\n</body></html>"
            };
            mail.To.Add(cliente.Email.Trim());
            mail.ReplyToList.Add(new MailAddress(buzon, "El equipo de Nueva Visión"));
            if (!string.IsNullOrWhiteSpace(copiaOculta))
            {
                mail.Bcc.Add(copiaOculta.Trim());
            }
            return mail;
        }

        /// <summary>Manda el resumen (solo al equipo interno). En Activo también: dice qué ha salido de verdad.</summary>
        internal static bool EnviarResumen(EjecucionRecordatorioReposicionDTO ejecucion, IServicioCorreoElectronico servicioCorreo,
            string destinatarios)
        {
            try
            {
                using (MailMessage correo = ConstruirCorreoSombra(ejecucion, destinatarios))
                {
                    bool enviado = servicioCorreo.EnviarCorreoSMTP(correo);
                    if (!enviado)
                    {
                        ElmahHelper.Log(new Exception(
                            $"[Recordatorio reposición #532] No se ha podido mandar el resumen ({ejecucion.Plan.Enviar.Count} correos)."));
                    }
                    return enviado;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception("[Recordatorio reposición #532] Error al mandar el resumen: " + ex.Message, ex));
                return false;
            }
        }

        /// <summary>Cualquier fallo al leer el parámetro cuenta como apagado.</summary>
        internal static ModoRecordatorioReposicion LeerModo(ILectorParametrosUsuario lector)
        {
            try
            {
                return InterpretarModo(lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION));
            }
            catch
            {
                return ModoRecordatorioReposicion.Apagado;
            }
        }

        internal static ModoRecordatorioReposicion InterpretarModo(string valor)
        {
            string limpio = valor?.Trim();
            if (string.Equals(limpio, VALOR_ACTIVO, StringComparison.OrdinalIgnoreCase))
            {
                return ModoRecordatorioReposicion.Activo;
            }
            return string.Equals(limpio, VALOR_SOMBRA, StringComparison.OrdinalIgnoreCase)
                ? ModoRecordatorioReposicion.Sombra
                : ModoRecordatorioReposicion.Apagado;
        }

        /// <summary>Sin fila o con error: null (la calculadora usa la lista por defecto).</summary>
        internal static string LeerConsumibles(ILectorParametrosUsuario lector)
        {
            try
            {
                return lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_CONSUMIBLES);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>CSV de vendedores. Sin fila o con error: null (ninguno: no se escribe a nadie).</summary>
        internal static string LeerVendedores(ILectorParametrosUsuario lector)
        {
            try
            {
                return lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_VENDEDORES);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// NestoAPI#532: CSV de vendedores de peluquería cuyos clientes no reciben el correo del recordatorio (la push no). Sin fila
        /// o con error: null, que <see cref="ReglasEnvioRecordatorioReposicion.LeerVendedoresPeluqueriaExcluidos"/> lee como «IF».
        /// </summary>
        internal static string LeerVendedoresPeluqueriaExcluidos(ILectorParametrosUsuario lector)
        {
            try
            {
                return lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO,
                    Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_EXCLUIR_VENDEDORES_PELUQUERIA);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Carlos (25/09/26): de momento la sombra solo le llega a él. Para ampliarlo, volver a los de
        /// CorreosPostCompra:EmailsTest (Laura, Manuel y Carlos), que son los que revisan los correos posventa.
        /// </summary>
        internal static string DestinatariosSombra() => Constantes.Correos.INFORMATICA;

        /// <summary>El mismo buzón que los correos posventa de los sábados (CorreosPostCompra:Remitente en Web.config).</summary>
        internal static string Remitente()
        {
            try
            {
                string valor = ConfigurationManager.AppSettings["CorreosPostCompra:Remitente"];
                return string.IsNullOrWhiteSpace(valor) ? REMITENTE_POR_DEFECTO : valor.Trim();
            }
            catch
            {
                return REMITENTE_POR_DEFECTO;
            }
        }

        /// <summary>Foto del producto en la tienda (la misma fuente que facturas, fichas y plantilla: ProductoDTO.RutaImagen).</summary>
        internal static async Task<string> LeerFotoTienda(string producto)
            => string.IsNullOrWhiteSpace(producto)
                ? null
                : PlantillaRecordatorioReposicion.NormalizarUrlFoto(await ProductoDTO.RutaImagen(producto.Trim()).ConfigureAwait(false));

        /// <summary>
        /// Enlace a la ficha de la tienda y foto de cada producto, con caché para toda la pasada: muchos clientes comparten
        /// productos y cada uno son llamadas a PrestaShop.
        /// </summary>
        internal static Func<RecordatorioReposicionClienteDTO, Task> CrearRellenadorProductos(
            Func<string, Task<string>> leerEnlace = null, Func<string, Task<string>> leerFoto = null)
        {
            leerEnlace = leerEnlace ?? ProductoDTO.LeerUrlTiendaOnline;
            leerFoto = leerFoto ?? LeerFotoTienda;
            var enlaces = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var fotos = new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return async correo =>
            {
                foreach (CandidatoReposicionDTO producto in correo.Productos)
                {
                    string id = producto.Producto?.Trim() ?? string.Empty;
                    if (!enlaces.TryGetValue(id, out string url))
                    {
                        url = await Intentar(leerEnlace, id).ConfigureAwait(false);
                        enlaces[id] = url;
                    }
                    producto.EnlaceTienda = !string.IsNullOrWhiteSpace(url)
                        ? url
                        : ServicioRecomendacionesPostCompra.GenerarUrlBusquedaTienda(producto.NombreProducto);
                    if (!fotos.TryGetValue(id, out string foto))
                    {
                        foto = PlantillaRecordatorioReposicion.NormalizarUrlFoto(await Intentar(leerFoto, id).ConfigureAwait(false));
                        fotos[id] = foto;
                    }
                    producto.UrlFoto = foto;
                }
            };
        }

        private static async Task<string> Intentar(Func<string, Task<string>> leer, string producto)
        {
            try
            {
                return await leer(producto).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }
        }

        internal static MailMessage ConstruirCorreoSombra(EjecucionRecordatorioReposicionDTO ejecucion, string destinatarios)
        {
            ResultadoRecordatorioReposicionDTO resultado = ejecucion.Resultado ?? new ResultadoRecordatorioReposicionDTO { Fecha = DateTime.Today };
            PlanRecordatorioReposicionDTO plan = ejecucion.Plan ?? new PlanRecordatorioReposicionDTO();
            bool activo = ejecucion.Modo == ModoRecordatorioReposicion.Activo;
            string cabecera = activo
                ? $"[Activo] Recordatorio de reposición {resultado.Fecha:dd/MM/yyyy}: {plan.Enviados.Count} enviados"
                : $"[Sombra] Recordatorio de reposición {resultado.Fecha:dd/MM/yyyy}: {plan.Enviar.Count} se enviarían";
            var mail = new MailMessage
            {
                From = new MailAddress("nesto@nuevavision.es", "Nesto - Nueva Visión"),
                Subject = cabecera + $", {plan.FueraPorVendedor.Count} fuera por vendedor, " +
                    $"{plan.FueraPorVendedorPeluqueria.Count} fuera por vendedor de peluquería, {resultado.GrupoControl.Count} en el grupo de control",
                IsBodyHtml = true,
                BodyEncoding = Encoding.UTF8,
                SubjectEncoding = Encoding.UTF8
            };
            foreach (string destinatario in (destinatarios ?? Constantes.Correos.INFORMATICA)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(d => d.Trim())
                .Where(d => d.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                mail.To.Add(destinatario);
            }
            if (mail.To.Count == 0)
            {
                mail.To.Add(Constantes.Correos.INFORMATICA);
            }
            mail.Body = GenerarHtmlSombra(ejecucion);
            return mail;
        }

        internal static string GenerarHtmlSombra(EjecucionRecordatorioReposicionDTO ejecucion)
        {
            ResultadoRecordatorioReposicionDTO resultado = ejecucion.Resultado ?? new ResultadoRecordatorioReposicionDTO { Fecha = DateTime.Today };
            PlanRecordatorioReposicionDTO plan = ejecucion.Plan ?? new PlanRecordatorioReposicionDTO();
            TextoRecordatorioReposicion texto = ejecucion.Texto ?? TextoRecordatorioReposicion.Fija();
            bool activo = ejecucion.Modo == ModoRecordatorioReposicion.Activo;
            string vendedores = plan.Vendedores.Any() ? string.Join(", ", plan.Vendedores) : "(ninguno)";

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'></head><body style='font-family: Arial, sans-serif; font-size: 13px;'>");
            sb.AppendLine($"<h2>Recordatorio de reposición (modo {(activo ? "ACTIVO" : "sombra")})</h2>");
            if (!string.IsNullOrWhiteSpace(ejecucion.Aviso))
            {
                sb.AppendLine($"<p style='color: #b00020;'><b>{WebUtility.HtmlEncode(ejecucion.Aviso)}</b></p>");
            }
            if (activo)
            {
                sb.AppendLine($"<p><b>Se ha escrito de verdad a {plan.Enviados.Count} clientes</b> (los de los vendedores " +
                    $"<code>{WebUtility.HtmlEncode(vendedores)}</code>; «(NULL)» = sin vendedor). Cada envío queda registrado en " +
                    "RecordatoriosReposicionEnvios y no se repite.</p>");
            }
            else
            {
                sb.AppendLine("<p><b>Esto es una prueba: no se ha escrito a ningún cliente.</b> Es la lista de clientes a los que esta " +
                    "semana se les recordaría reponer un producto (NestoAPI#532), calculada con TODAS las ventas de Nesto (vendedor, " +
                    $"teléfono, tienda online...). En Activo se escribiría solo a los de los vendedores <code>{WebUtility.HtmlEncode(vendedores)}</code>.</p>");
            }
            string tope = resultado.TopeDias.HasValue
                ? $", y nunca si la última compra es de hace más de {resultado.TopeDias.Value} días (RecordatorioReposicionTopeDias)"
                : ", sin tope de días desde la última compra (RecordatorioReposicionTopeDias)";
            sb.AppendLine($"<p>Criterio: productos consumibles comprados al menos {CalculadoraReposicion.MINIMO_COMPRAS} días distintos " +
                $"desde el {resultado.HistorialDesde:dd/MM/yyyy}. Su ritmo es la mediana de días entre compras (mínimo " +
                $"{CalculadoraReposicion.INTERVALO_MINIMO_DIAS}); toca reponer cuando ha pasado 1,25 veces ese ritmo y menos de " +
                $"{CalculadoraReposicion.FACTOR_ABANDONO} veces (más allá ya no es reposición, es un cliente que se ha ido){tope}. " +
                $"Hasta {CalculadoraReposicion.MAXIMO_PRODUCTOS_POR_CORREO} " +
                $"productos por correo, los de más importe habitual. Un recordatorio por producto y compra, un correo cada " +
                $"{ReglasEnvioRecordatorioReposicion.DIAS_ENTRE_CORREOS} días como mucho y nunca a quien ha pedido la baja " +
                $"(CorreosPostCompraBajas). Consumibles: <code>{WebUtility.HtmlEncode(resultado.Consumibles)}</code>.</p>");
            sb.AppendLine($"<p>Texto de esta semana: <b>{WebUtility.HtmlEncode(texto.Origen)}</b> ({WebUtility.HtmlEncode(texto.Version)})" +
                (string.IsNullOrWhiteSpace(texto.MotivoFija) ? "" : $". Fija porque: {WebUtility.HtmlEncode(texto.MotivoFija)}") + ".</p>");

            AppendPuertaModelo(sb, ejecucion.PuertaModelo);

            if (activo)
            {
                sb.AppendLine($"<h3>Enviados de verdad ({plan.Enviados.Count} correos, {plan.Enviados.Sum(c => c.Productos.Count)} productos)</h3>");
                AppendTablaOVacio(sb, plan.Enviados, "Esta semana no ha salido ninguno.");
                if (plan.Fallidos.Any())
                {
                    sb.AppendLine($"<h3 style='color: #b00020;'>Ha fallado el envío ({plan.Fallidos.Count}): la semana que viene se reintentan</h3>");
                    AppendTablaCorreos(sb, plan.Fallidos);
                }
            }
            else
            {
                sb.AppendLine($"<h3>Se les escribiría ({plan.Enviar.Count} correos, {plan.Enviar.Sum(c => c.Productos.Count)} productos)</h3>");
                AppendTablaOVacio(sb, plan.Enviar, "Esta semana no se escribiría a nadie.");
            }

            if (plan.FueraPorVendedor.Any())
            {
                sb.AppendLine($"<h3>Fuera por vendedor ({plan.FueraPorVendedor.Count}): les tocaría, pero su vendedor no está en la lista</h3>");
                AppendTablaCorreos(sb, plan.FueraPorVendedor);
            }

            if (plan.FueraPorVendedorPeluqueria.Any())
            {
                string excluidos = plan.VendedoresPeluqueriaExcluidos.Any() ? string.Join(", ", plan.VendedoresPeluqueriaExcluidos) : "(ninguno)";
                sb.AppendLine($"<h3>Fuera por vendedor de peluquería ({plan.FueraPorVendedorPeluqueria.Count}): les tocaría, pero su " +
                    $"vendedor de peluquería es <code>{WebUtility.HtmlEncode(excluidos)}</code> " +
                    "(RecordatorioReposicionExcluirVendedoresPeluqueria)</h3>");
                AppendTablaCorreos(sb, plan.FueraPorVendedorPeluqueria);
            }

            if (resultado.GrupoControl.Any())
            {
                sb.AppendLine($"<h3>Grupo de control ({resultado.GrupoControl.Count}): cumplen, pero no se les escribe</h3>");
                sb.AppendLine($"<p>Un {CalculadoraReposicion.PORCENTAJE_GRUPO_CONTROL} % de los clientes (siempre los mismos) no recibe el " +
                    "correo, para comparar si los que lo reciben reponen más que los que no. " +
                    (activo ? $"Registrados para medir (vendedor en la lista): {plan.Control.Count}." : "") + "</p>");
                AppendTablaCorreos(sb, resultado.GrupoControl);
            }

            if (resultado.Descartes.Any())
            {
                sb.AppendLine($"<h3>Tocaría por ritmo, pero se quedan fuera ({resultado.Descartes.Count})</h3>");
                sb.AppendLine("<ul>");
                foreach (var motivo in resultado.Descartes
                    .GroupBy(d => MotivoResumido(d.Motivo))
                    .OrderByDescending(g => g.Count()))
                {
                    sb.AppendLine($"<li>{WebUtility.HtmlEncode(motivo.Key)}: {motivo.Count()}</li>");
                }
                sb.AppendLine("</ul>");
                AppendTablaDescartes(sb, resultado.Descartes.Take(MAXIMO_FILAS_DESCARTES).ToList());
                if (resultado.Descartes.Count > MAXIMO_FILAS_DESCARTES)
                {
                    sb.AppendLine($"<p>(Solo los {MAXIMO_FILAS_DESCARTES} primeros. La lista completa, en GET api/CorreosPostCompra/Reposicion.)</p>");
                }
            }

            AppendConversion(sb, ejecucion.Conversion);

            RecordatorioReposicionClienteDTO muestra = plan.Enviados.FirstOrDefault() ?? plan.Enviar.FirstOrDefault()
                ?? plan.FueraPorVendedor.FirstOrDefault();
            if (muestra != null)
            {
                sb.AppendLine($"<h3>Muestra: el correo {(activo && plan.Enviados.Contains(muestra) ? "que ha recibido" : "que recibiría")} el primero</h3>");
                sb.AppendLine("<div style='border: 1px solid #ccc; padding: 12px; background-color: #fafafa;'>");
                sb.AppendLine($"<p><b>Para:</b> {WebUtility.HtmlEncode(muestra.Email)} ({WebUtility.HtmlEncode(muestra.Cliente)} " +
                    $"{WebUtility.HtmlEncode(muestra.Nombre)})</p>");
                sb.AppendLine($"<p><b>Asunto:</b> {WebUtility.HtmlEncode(PlantillaRecordatorioReposicion.Asunto(muestra, texto))}</p>");
                sb.AppendLine(PlantillaRecordatorioReposicion.CuerpoHtml(muestra, texto, resultado.Fecha));
                sb.AppendLine("</div>");
            }

            sb.AppendLine($"<p style='color: #888; font-size: 11px; margin-top: 20px;'>Generado el {DateTime.Now:dd/MM/yyyy HH:mm}. " +
                "Parámetros de (defecto): RecordatorioReposicion (Apagado / Sombra / Activo) y RecordatorioReposicionVendedores " +
                "(Scripts/Issue532_RecordatorioReposicionActivo.sql); RecordatorioReposicionTopeDias, RecordatorioReposicionPuertaModelo y " +
                "RecordatorioReposicionUmbralModelo (Scripts/Issue736_RecordatorioReposicionModelo.sql).</p>");
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        /// <summary>NestoAPI#736: qué ha hecho la puerta del modelo de propensión.</summary>
        internal static void AppendPuertaModelo(StringBuilder sb, ResumenPuertaModeloDTO puerta)
        {
            if (puerta == null || puerta.Modo == ModoPuertaModeloReposicion.Apagado)
            {
                return;
            }
            string umbral = puerta.Umbral.ToString("0.00", ES);
            if (puerta.VersionModelo == null)
            {
                if (!string.IsNullOrWhiteSpace(puerta.Aviso))
                {
                    sb.AppendLine($"<p><b>Puerta del modelo de propensión ({puerta.Modo}):</b> {WebUtility.HtmlEncode(puerta.Aviso)}</p>");
                }
                return;
            }
            string efecto = puerta.Modo == ModoPuertaModeloReposicion.Activo
                ? $"<b>quitados</b> {puerta.PorDebajo} (van a «se quedan fuera»; {puerta.CorreosQuitados} correos o apuntes de control se quedan sin nada)"
                : $"en sombra no se quita ninguno: con la puerta en Activo se habrían quitado {puerta.PorDebajo}";
            sb.AppendLine($"<p><b>Puerta del modelo de propensión ({puerta.Modo}, modelo {WebUtility.HtmlEncode(puerta.VersionModelo)}, " +
                $"umbral {umbral}):</b> {puerta.Puntuados} productos puntuados (de los correos y del grupo de control), " +
                $"{puerta.SinPuntuar} sin puntuar (el modelo no los puede sugerir; pasan siempre). Con P1 menor que {umbral}: {efecto}. " +
                "La P1 de cada uno va en la columna P1 y queda registrada (RecordatoriosReposicionEnvios.ProbabilidadModelo).</p>");
        }

        /// <summary>NestoAPI#736: la conversión Enviado frente a Control de los envíos de ventana cerrada.</summary>
        internal static void AppendConversion(StringBuilder sb, InformeConversionReposicionDTO conversion)
        {
            if (conversion == null || conversion.EnviosCerrados == 0)
            {
                return;
            }
            sb.AppendLine($"<h3>¿Convierte? Recompra del producto recordado en {conversion.DiasVentana} días (envíos desde el " +
                $"{conversion.Desde:dd/MM/yyyy} con la ventana cerrada: {conversion.EnviosCerrados})</h3>");
            sb.AppendLine("<p>% de productos que el cliente volvió a pedir en la ventana, a los que se les recordó (Enviado) frente al " +
                "grupo de control. Si el correo sirve, Enviado tiene que salir claramente por encima. Detalle en " +
                "GET api/CorreosPostCompra/Reposicion/Conversion.</p>");
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'><th>Desglose</th><th>Tramo</th><th>Enviado</th>" +
                "<th>% recompra</th><th>Control</th><th>% recompra</th><th>Diferencia (puntos)</th></tr>");
            var filas = new List<ConversionReposicionTramoDTO> { conversion.Total };
            filas.AddRange(conversion.PorAntiguedad);
            filas.AddRange(conversion.PorIntervalo);
            filas.AddRange(conversion.PorProbabilidad);
            foreach (ConversionReposicionTramoDTO t in filas.Where(t => t != null))
            {
                sb.AppendLine($"<tr><td>{WebUtility.HtmlEncode(t.Dimension)}</td><td>{WebUtility.HtmlEncode(t.Tramo)}</td>" +
                    $"<td style='text-align: right;'>{t.EnviadoRecomprados}/{t.EnviadoProductos}</td>" +
                    $"<td style='text-align: right;'>{Porcentaje(t.EnviadoPorcentaje)}</td>" +
                    $"<td style='text-align: right;'>{t.ControlRecomprados}/{t.ControlProductos}</td>" +
                    $"<td style='text-align: right;'>{Porcentaje(t.ControlPorcentaje)}</td>" +
                    $"<td style='text-align: right;'>{(t.DiferenciaPuntos.HasValue ? t.DiferenciaPuntos.Value.ToString("+0.0;-0.0;0.0", ES) : "—")}</td></tr>");
            }
            sb.AppendLine("</table>");
        }

        private static string Porcentaje(decimal? valor) => valor.HasValue ? valor.Value.ToString("0.0", ES) + " %" : "—";

        /// <summary>Los motivos de sustituto y correo duplicado llevan el producto/cliente al final: se agrupan sin él.</summary>
        internal static string MotivoResumido(string motivo)
        {
            if (string.IsNullOrWhiteSpace(motivo))
            {
                return "(sin motivo)";
            }
            // NestoAPI#736: «Última compra hace 300 días: más de 240.» → «Última compra hace más de 240 días»
            if (motivo.StartsWith(CalculadoraReposicion.MOTIVO_TOPE_PREFIJO, StringComparison.Ordinal))
            {
                int mas = motivo.LastIndexOf("más de ", StringComparison.Ordinal);
                return mas > 0 ? CalculadoraReposicion.MOTIVO_TOPE_PREFIJO + motivo.Substring(mas).TrimEnd('.') + " días" : motivo.TrimEnd('.');
            }
            if (motivo.StartsWith(PuertaModeloRecordatorioReposicion.MOTIVO_MODELO_PREFIJO, StringComparison.Ordinal))
            {
                return "El modelo de propensión le da una P1 por debajo del umbral";
            }
            int dosPuntos = motivo.IndexOf(": ", StringComparison.Ordinal);
            return dosPuntos > 0 ? motivo.Substring(0, dosPuntos) : motivo.TrimEnd('.');
        }

        private static void AppendTablaOVacio(StringBuilder sb, List<RecordatorioReposicionClienteDTO> correos, string vacio)
        {
            if (correos.Any())
            {
                AppendTablaCorreos(sb, correos);
            }
            else
            {
                sb.AppendLine($"<p>{WebUtility.HtmlEncode(vacio)}</p>");
            }
        }

        private static void AppendTablaCorreos(StringBuilder sb, List<RecordatorioReposicionClienteDTO> correos)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.AppendLine("<tr style='background-color: #4A90D9; color: white;'><th>Cliente</th><th>Nombre</th><th>Correo</th>" +
                "<th>Vendedor</th><th>Producto</th><th>Compras</th><th>Ritmo (días)</th><th>Última compra</th>" +
                "<th>Días desde</th><th>Importe habitual</th><th>P1</th></tr>");
            bool alternar = false;
            foreach (RecordatorioReposicionClienteDTO correo in correos)
            {
                string fondo = alternar ? "#f2f2f2" : "#ffffff";
                int filas = Math.Max(1, correo.Productos.Count);
                bool primera = true;
                foreach (CandidatoReposicionDTO p in correo.Productos)
                {
                    sb.Append($"<tr style='background-color: {fondo};'>");
                    if (primera)
                    {
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Cliente)}</td>");
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Nombre)}</td>");
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Email)}</td>");
                        sb.Append($"<td rowspan='{filas}'>{WebUtility.HtmlEncode(correo.Vendedor ?? ReglasEnvioRecordatorioReposicion.SIN_VENDEDOR)}" +
                            (string.IsNullOrWhiteSpace(correo.VendedorNombre) ? "" : " " + WebUtility.HtmlEncode(correo.VendedorNombre)) + "</td>");
                        primera = false;
                    }
                    AppendCeldasProducto(sb, p);
                    sb.AppendLine("</tr>");
                }
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        private static void AppendTablaDescartes(StringBuilder sb, List<CandidatoReposicionDTO> descartes)
        {
            sb.AppendLine("<table border='1' cellpadding='5' cellspacing='0' style='border-collapse: collapse; font-size: 12px;'>");
            sb.AppendLine("<tr style='background-color: #888; color: white;'><th>Cliente</th><th>Producto</th><th>Compras</th>" +
                "<th>Ritmo (días)</th><th>Última compra</th><th>Días desde</th><th>Importe habitual</th><th>P1</th><th>Motivo</th></tr>");
            bool alternar = false;
            foreach (CandidatoReposicionDTO d in descartes)
            {
                sb.Append($"<tr style='background-color: {(alternar ? "#f2f2f2" : "#ffffff")};'>");
                sb.Append($"<td>{WebUtility.HtmlEncode(d.Cliente)}</td>");
                AppendCeldasProducto(sb, d);
                sb.Append($"<td>{WebUtility.HtmlEncode(d.Motivo)}</td>");
                sb.AppendLine("</tr>");
                alternar = !alternar;
            }
            sb.AppendLine("</table>");
        }

        private static void AppendCeldasProducto(StringBuilder sb, CandidatoReposicionDTO p)
        {
            sb.Append($"<td>{WebUtility.HtmlEncode(p.Producto)} {WebUtility.HtmlEncode(p.NombreProducto)}</td>");
            sb.Append($"<td style='text-align: center;'>{p.NumeroCompras}</td>");
            sb.Append($"<td style='text-align: center;'>{p.IntervaloDias}</td>");
            sb.Append($"<td>{p.UltimaCompra:dd/MM/yyyy}</td>");
            sb.Append($"<td style='text-align: center;'>{p.DiasDesdeUltimaCompra}</td>");
            sb.Append($"<td style='text-align: right;'>{p.ImporteHabitual:N2} €</td>");
            // NestoAPI#736: P1 del modelo (en rojo si queda por debajo del umbral)
            string p1 = p.ProbabilidadModelo.HasValue ? p.ProbabilidadModelo.Value.ToString("0.000", ES) : "—";
            sb.Append(p.FueraPorModelo == true
                ? $"<td style='text-align: right; color: #b00020;'>{p1}</td>"
                : $"<td style='text-align: right;'>{p1}</td>");
        }
    }
}
