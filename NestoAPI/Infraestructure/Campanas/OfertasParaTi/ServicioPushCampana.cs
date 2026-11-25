using NestoAPI.Infraestructure.CorreosPostCompra;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Hangfire;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    /// <summary>Lo que la push de campaña lee y escribe fuera del núcleo de ofertas.</summary>
    public interface IFuentePushCampana
    {
        /// <summary>Clientes con algún dispositivo activo de NestoTiendas con RecibirConsejos (el del recordatorio de reposición).</summary>
        Task<List<string>> ClientesConConsejos();
        /// <summary>Los que han apagado «Avisos de reposición» en la app (sin la tabla, nadie).</summary>
        Task<ISet<string>> ClientesDeBaja();
        /// <summary>De esos clientes, los que ya tienen en el buzón una push de esa campaña desde ese día.</summary>
        Task<ISet<string>> ClientesConPushDeCampana(string campana, IReadOnlyCollection<string> clientes, DateTime desde);
        /// <summary>Una fila en Modificaciones (Tabla CampanaPush).</summary>
        Task Anotar(string nuevo, string usuario);
    }

    public class FuentePushCampanaSql : IFuentePushCampana
    {
        internal const string TABLA = "CampanaPush";
        internal const string SQL_ANOTAR = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @Usuario)";

        private readonly NVEntities db;

        public FuentePushCampanaSql(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        /// <summary>
        /// Los dispositivos que cuentan (Carlos 25/11/26): NestoTiendas activo, con RecibirConsejos y Cliente, de la empresa O SIN ELLA (la app
        /// no la manda al registrarse: los 60 dispositivos tienen Empresa NULL, que es la de por defecto). Antes se leía con el criterio del
        /// recordatorio de reposición (<c>d.Empresa == EMPRESA</c>) y no salía NADIE. Como <c>ObtenerDispositivosCliente</c>, que es por donde se envía.
        /// </summary>
        internal static Expression<Func<DispositivoNotificacion, bool>> FiltroDestinatarios(string empresa)
            => d => d.Aplicacion == Constantes.Aplicaciones.NESTO_TIENDAS && d.Activo && d.RecibirConsejos
                && (d.Empresa == null || d.Empresa == empresa) && d.Cliente != null;

        public async Task<List<string>> ClientesConConsejos()
        {
            List<string> clientes = await db.DispositivosNotificaciones
                .Where(FiltroDestinatarios(Constantes.Empresas.EMPRESA_POR_DEFECTO))
                .Select(d => d.Cliente)
                .Distinct()
                .ToListAsync().ConfigureAwait(false);
            return clientes.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Las bajas («Avisos de reposición») NO dependen de la empresa del dispositivo: la app las guarda siempre con la empresa por
        // defecto (PreferenciasNotificacionesController), que es la que se lee aquí.

        public Task<ISet<string>> ClientesDeBaja()
            => ServicioPreferenciasNotificaciones.ClientesDeBaja(new RepositorioPreferenciasNotificacionesSql(db), Constantes.Empresas.EMPRESA_POR_DEFECTO,
                TiposNotificacionCliente.AVISOS_REPOSICION);

        /// <summary>
        /// Por el buzón (NotificacionesBuzon, una fila por cliente con Datos = el JSON de la push, con su «campana»): cuenta aunque la
        /// hayan borrado (les llegó). Sin tabla nueva.
        /// </summary>
        public async Task<ISet<string>> ClientesConPushDeCampana(string campana, IReadOnlyCollection<string> clientes, DateTime desde)
        {
            var resultado = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> lista = (clientes ?? new List<string>()).ToList();
            string marca = ReglaPushCampana.MarcaCampana(campana);
            for (int i = 0; i < lista.Count; i += LectorOfertasParaTiSql.LOTE_CLIENTES)
            {
                List<string> lote = lista.Skip(i).Take(LectorOfertasParaTiSql.LOTE_CLIENTES).ToList();
                List<string> con = await db.NotificacionesBuzon
                    .Where(n => n.Aplicacion == Constantes.Aplicaciones.NESTO_TIENDAS && n.FechaCreacion >= desde
                        && n.Cliente != null && lote.Contains(n.Cliente) && n.Datos != null && n.Datos.Contains(marca))
                    .Select(n => n.Cliente)
                    .Distinct()
                    .ToListAsync().ConfigureAwait(false);
                foreach (string c in con.Where(c => c != null))
                {
                    _ = resultado.Add(c.Trim());
                }
            }
            return resultado;
        }

        public async Task Anotar(string nuevo, string usuario)
        {
            _ = await db.Database.ExecuteSqlCommandAsync(SQL_ANOTAR,
                new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = TABLA },
                new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = DBNull.Value },
                new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                new SqlParameter("@Usuario", SqlDbType.NVarChar, 50) { Value = string.IsNullOrWhiteSpace(usuario) ? "NestoAPI" : (usuario.Length > 50 ? usuario.Substring(0, 50) : usuario) })
                .ConfigureAwait(false);
        }
    }

    public enum EstadoPushCampana
    {
        Ok,
        PeticionIncorrecta
    }

    public class ResultadoPushCampana
    {
        public EstadoPushCampana Estado { get; set; }
        public string Mensaje { get; set; }
        public RespuestaCampanaPushDTO Respuesta { get; set; }
    }

    /// <summary>
    /// «Ofertas para ti», la push de campaña personalizable (POST api/Campanas/Push). Con ensayo (por defecto) calcula a quién y con qué
    /// texto, sin enviar ni escribir nada (solo lee). De verdad: por cliente, su lista del núcleo y su push por
    /// <see cref="IServicioNotificacionesPush.EnviarConsejoACliente"/> (la del recordatorio de reposición: guarda en el buzón y manda
    /// a los dispositivos con consejos), y al final una fila en Modificaciones. Un cliente que falla no para a los demás.
    /// </summary>
    public class ServicioPushCampana
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;

        private readonly ServicioOfertasParaTi nucleo;
        private readonly IFuentePushCampana fuente;
        private readonly IServicioNotificacionesPush push;
        private readonly SelectorOfertasConPrecio selector;
        private readonly Func<string, Task<string>> leerContacto;
        private readonly Func<string, Task<string>> leerFoto;
        private readonly Action<Exception> registrar;
        private readonly Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto;

        /// <param name="contexto">La campaña preparada (producción: la guardada 10 minutos, con el ranking de cada cliente que ya haya
        /// calculado la app). Sin él, se prepara de nuevo.</param>
        /// <param name="precios">El motor de precios: la push elige las ofertas igual que la pantalla (solo las que le mejoran el precio).</param>
        /// <param name="leerContacto">Cliente → contacto de su ficha principal (null si no existe: sin ofertas).</param>
        public ServicioPushCampana(ServicioOfertasParaTi nucleo, IFuentePushCampana fuente, IServicioNotificacionesPush push,
            ICalculadorPreciosOferta precios, Func<string, Task<string>> leerContacto,
            Func<string, Task<string>> leerFoto = null, Action<Exception> registrar = null,
            Func<string, DateTime, Task<ContextoOfertasParaTi>> contexto = null)
        {
            this.nucleo = nucleo ?? throw new ArgumentNullException(nameof(nucleo));
            this.contexto = contexto ?? ((c, d) => nucleo.Preparar(c, d));
            this.fuente = fuente ?? throw new ArgumentNullException(nameof(fuente));
            this.push = push ?? throw new ArgumentNullException(nameof(push));
            this.leerContacto = leerContacto ?? throw new ArgumentNullException(nameof(leerContacto));
            this.leerFoto = leerFoto ?? RecordatorioReposicionJobsService.LeerFotoTienda;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
            selector = new SelectorOfertasConPrecio(nucleo, precios ?? throw new ArgumentNullException(nameof(precios)), this.registrar);
        }

        /// <summary>La de producción (la del controlador y la del envío en segundo plano).</summary>
        public static ServicioPushCampana Produccion(NVEntities db)
        {
            ServicioOfertasParaTi nucleo = ServicioOfertasParaTi.Produccion(db);
            return new ServicioPushCampana(nucleo, new FuentePushCampanaSql(db), new ServicioNotificacionesPush(),
                new CalculadorPreciosOfertaMotor(db), ServicioOfertasCliente.LectorContactoPrincipal(db),
                contexto: (c, d) => CacheContextoOfertasParaTi.Obtener(c, d, () => nucleo.Preparar(c, d)));
        }

        /// <summary>
        /// Lo que se comprueba ANTES de encolar el envío de verdad (para dar el 400 en el acto): la petición y el horario. Null si vale.
        /// </summary>
        public static string ValidarParaEnviar(PeticionCampanaPushDTO peticion, DateTime ahora)
        {
            string error = ReglaPushCampana.Validar(peticion, out _);
            if (error != null)
            {
                return error;
            }
            return ReglaPushCampana.EnHorario(ahora) ? null : ReglaPushCampana.MENSAJE_FUERA_DE_HORARIO;
        }

        public async Task<ResultadoPushCampana> Ejecutar(PeticionCampanaPushDTO peticion, bool ensayo, DateTime ahora, string usuario)
        {
            string error = ReglaPushCampana.Validar(peticion, out PeticionPushCampanaValida p);
            if (error != null)
            {
                return new ResultadoPushCampana { Estado = EstadoPushCampana.PeticionIncorrecta, Mensaje = error };
            }
            if (!ensayo && !ReglaPushCampana.EnHorario(ahora))
            {
                return new ResultadoPushCampana { Estado = EstadoPushCampana.PeticionIncorrecta, Mensaje = ReglaPushCampana.MENSAJE_FUERA_DE_HORARIO };
            }
            var reloj = System.Diagnostics.Stopwatch.StartNew();
            int consultasPrecio = 0;
            int puntuados = 0;
            DateTime hoy = ahora.Date;
            ContextoOfertasParaTi contexto = await this.contexto(p.Campana, hoy).ConfigureAwait(false);
            if (contexto.Candidatas.Count == 0)
            {
                return new ResultadoPushCampana
                {
                    Estado = EstadoPushCampana.PeticionIncorrecta,
                    Mensaje = $"La campaña «{p.Campana}» no tiene hoy ninguna oferta vigente para profesionales (por producto, con audiencia 1 o 2)."
                };
            }
            string titulo = ReglaOfertasParaTi.Titulo(p.Campana);
            var respuesta = new RespuestaCampanaPushDTO { Ensayo = ensayo, Campana = p.Campana };

            List<string> conApp = await fuente.ClientesConConsejos().ConfigureAwait(false) ?? new List<string>();
            ISet<string> deBaja = await fuente.ClientesDeBaja().ConfigureAwait(false) ?? new HashSet<string>();
            DateTime? desde = ReglaPushCampana.DesdeSinRepetir(hoy, p.DiasSinRepetir);
            ISet<string> recientes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (desde.HasValue)
            {
                List<string> candidatos = ReglaPushCampana.Conjunto(conApp).ToList();
                recientes = await fuente.ClientesConPushDeCampana(p.Campana, candidatos, desde.Value).ConfigureAwait(false) ?? recientes;
            }
            ReglaPushCampana.Reparto reparto = ReglaPushCampana.Repartir(conApp, deBaja, p, recientes);
            respuesta.Saltados.AddRange(reparto.Saltados);
            respuesta.Destinatarios = reparto.Destinatarios.Count;

            // Enteras y luego la MISMA selección que la pantalla: solo las que le mejoran el precio, con el respaldo general
            Dictionary<string, ResultadoOfertasCliente> listas = await nucleo.Calcular(contexto, reparto.Destinatarios, null).ConfigureAwait(false);
            var fotos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string cliente in reparto.Destinatarios)
            {
                try
                {
                    ResultadoOfertasCliente r = listas.TryGetValue(cliente, out ResultadoOfertasCliente suyo) ? suyo : new ResultadoOfertasCliente { Cliente = cliente };
                    string contacto = await leerContacto(cliente).ConfigureAwait(false);
                    RecorridoOfertas elegidas = await selector.Seleccionar(contexto, r, EMPRESA, cliente, contacto, p.Maximo).ConfigureAwait(false);
                    consultasPrecio += elegidas.ConsultasPrecio;
                    puntuados += r.Puntuados;
                    List<OfertaElegida> ofertas = elegidas.Ofertas.Select(e => e.Oferta).ToList();
                    // Solo se nombra un producto si es de lo suyo (habitual, le toca, de sus marcas)
                    List<OfertaElegida> nombrables = ofertas.Where(ReglaSeleccionOfertasParaTi.EsSuya).ToList();
                    string cuerpo;
                    if (ofertas.Count > 0)
                    {
                        respuesta.ConOfertas++;
                        if (nombrables.Count == 0 && (p.Cuerpo == null || TextosPushOfertas.NombraProducto(p.Cuerpo)))
                        {
                            cuerpo = TextosPushOfertas.CuerpoGenerico(titulo);
                        }
                        else
                        {
                            cuerpo = p.Cuerpo != null
                                ? TextosPushOfertas.AplicarPlantilla(p.Cuerpo, nombrables, titulo, ofertas.Count)
                                : TextosPushOfertas.CuerpoAutomatico(nombrables, ofertas.Count);
                        }
                    }
                    else
                    {
                        respuesta.SinOfertas++;
                        if (!p.IncluirSinOfertas)
                        {
                            respuesta.Saltados.Add(new SaltadoPushCampanaDTO { Cliente = cliente, Motivo = ReglaPushCampana.MOTIVO_SIN_OFERTAS });
                            continue;
                        }
                        cuerpo = TextosPushOfertas.AplicarPlantilla(p.CuerpoSinOfertas ?? TextosPushOfertas.CuerpoSinOfertasPorDefecto(titulo), ofertas, titulo);
                    }
                    string tituloPush = p.Titulo != null
                        ? (nombrables.Count == 0 && TextosPushOfertas.NombraProducto(p.Titulo) ? titulo : TextosPushOfertas.AplicarPlantilla(p.Titulo, nombrables, titulo, ofertas.Count))
                        : titulo;
                    string imagen = p.ImagenUrl ?? (ofertas.Count > 0 ? await Foto(fotos, ofertas[0].Candidata.Producto).ConfigureAwait(false) : null);
                    NotificacionPushDTO notificacion = TextosPushOfertas.Notificacion(p.Campana, tituloPush, cuerpo, imagen);
                    var mensaje = new MensajePushCampanaDTO
                    {
                        Cliente = cliente,
                        Titulo = notificacion.Titulo,
                        Cuerpo = notificacion.Cuerpo,
                        Productos = ofertas.Select(o => o.Candidata.Producto).ToList(),
                        ImagenUrl = notificacion.Datos.TryGetValue("imagenUrl", out string foto) ? foto : null
                    };
                    if (!ensayo)
                    {
                        mensaje.Enviados = await push.EnviarConsejoACliente(EMPRESA, cliente, notificacion).ConfigureAwait(false);
                    }
                    respuesta.Mensajes.Add(mensaje);
                }
                catch (Exception ex)
                {
                    // Un cliente que falla no deja sin push a los demás
                    registrar(new Exception($"[Push de campaña] Cliente {cliente}: {ex.Message}", ex));
                    respuesta.Saltados.Add(new SaltadoPushCampanaDTO { Cliente = cliente, Motivo = "Error: " + ex.Message });
                }
            }

            respuesta.ConsultasPrecio = consultasPrecio;
            respuesta.ProductosPuntuados = puntuados;
            respuesta.Segundos = Math.Round((decimal)reloj.Elapsed.TotalSeconds, 1);
            if (!ensayo)
            {
                try
                {
                    // El resultado del envío (que va en segundo plano) queda aquí
                    await fuente.Anotar(JsonConvert.SerializeObject(new
                    {
                        Peticion = peticion,
                        respuesta.Campana,
                        respuesta.Destinatarios,
                        respuesta.ConOfertas,
                        respuesta.SinOfertas,
                        Enviados = respuesta.Mensajes.Count(m => m.Enviados > 0),
                        Dispositivos = respuesta.Mensajes.Sum(m => m.Enviados),
                        Saltados = respuesta.Saltados.Count,
                        ClientesEnviados = respuesta.Mensajes.Where(m => m.Enviados > 0).Select(m => m.Cliente).ToList(),
                        SaltadosDetalle = respuesta.Saltados.Select(s => s.Cliente + ": " + s.Motivo).ToList(),
                        respuesta.ConsultasPrecio,
                        respuesta.ProductosPuntuados,
                        respuesta.Segundos
                    }), usuario).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    registrar(new Exception($"[Push de campaña] Enviada, pero no se ha podido anotar en Modificaciones: {ex.Message}", ex));
                }
            }
            return new ResultadoPushCampana { Estado = EstadoPushCampana.Ok, Respuesta = respuesta };
        }

        private async Task<string> Foto(Dictionary<string, string> fotos, string producto)
        {
            if (fotos.TryGetValue(producto, out string guardada))
            {
                return guardada;
            }
            string foto = null;
            try
            {
                foto = PlantillaRecordatorioReposicion.NormalizarUrlFoto(await leerFoto(producto).ConfigureAwait(false));
            }
            catch
            {
                foto = null; // sin foto la push sigue sirviendo
            }
            fotos[producto] = foto;
            return foto;
        }
    }

    /// <summary>
    /// El envío DE VERDAD de la push de campaña, en segundo plano: por cliente se calcula su ranking y los precios hasta tener sus N
    /// ofertas (unas decenas de consultas por cliente; con 60 clientes, más de un minuto), así que la petición devuelve 202 y el
    /// resultado queda en Modificaciones (Tabla CampanaPush). Sin reintentos (una push repetida molesta más que una que no sale) y de una
    /// en una.
    /// </summary>
    public static class CampanaPushJobs
    {
        private const string USUARIO_ELMAH = "Sistema (push de campaña)";

        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void Enviar(PeticionCampanaPushDTO peticion, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    db.Configuration.LazyLoadingEnabled = false;
                    db.Configuration.ProxyCreationEnabled = false;
                    ResultadoPushCampana resultado = ServicioPushCampana.Produccion(db).Ejecutar(peticion, false, DateTime.Now, usuario)
                        .ConfigureAwait(false).GetAwaiter().GetResult();
                    if (resultado.Estado != EstadoPushCampana.Ok)
                    {
                        ElmahHelper.Log(new Exception($"[Push de campaña] No se ha enviado «{peticion?.Campana}»: {resultado.Mensaje}"), USUARIO_ELMAH);
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Push de campaña] Error al enviar «{peticion?.Campana}»: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }
    }
}
