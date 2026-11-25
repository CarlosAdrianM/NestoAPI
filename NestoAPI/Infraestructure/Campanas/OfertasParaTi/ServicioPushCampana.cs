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
using System.Threading.Tasks;

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

        public Task<List<string>> ClientesConConsejos() => RecordatorioReposicionPushJobsService.ClientesConConsejos(db);

        public Task<ISet<string>> ClientesDeBaja()
            => ServicioPreferenciasNotificaciones.ClientesDeBaja(new RepositorioPreferenciasNotificacionesSql(db), Constantes.Empresas.EMPRESA_POR_DEFECTO,
                TiposNotificacionCliente.AVISOS_REPOSICION);

        /// <summary>
        /// Por el buzón (NotificacionesBuzon, una fila por cliente con Datos = el JSON de la push), como la regla de los 7 días de la
        /// push de reposición: cuenta aunque la hayan borrado (les llegó). Sin tabla nueva.
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
        private readonly Func<string, Task<string>> leerFoto;
        private readonly Action<Exception> registrar;

        public ServicioPushCampana(ServicioOfertasParaTi nucleo, IFuentePushCampana fuente, IServicioNotificacionesPush push,
            Func<string, Task<string>> leerFoto = null, Action<Exception> registrar = null)
        {
            this.nucleo = nucleo ?? throw new ArgumentNullException(nameof(nucleo));
            this.fuente = fuente ?? throw new ArgumentNullException(nameof(fuente));
            this.push = push ?? throw new ArgumentNullException(nameof(push));
            this.leerFoto = leerFoto ?? RecordatorioReposicionJobsService.LeerFotoTienda;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
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
            DateTime hoy = ahora.Date;
            ContextoOfertasParaTi contexto = await nucleo.Preparar(p.Campana, hoy).ConfigureAwait(false);
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

            // Enteras: en modo Carrito se quedan solo los habituales y luego se recorta al máximo
            Dictionary<string, ResultadoOfertasCliente> listas = await nucleo.Calcular(contexto, reparto.Destinatarios, null).ConfigureAwait(false);
            var fotos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string cliente in reparto.Destinatarios)
            {
                try
                {
                    List<OfertaElegida> todas = listas.TryGetValue(cliente, out ResultadoOfertasCliente r) ? r.Ofertas : new List<OfertaElegida>();
                    List<OfertaElegida> ofertas = ReglaPushCampana.ProductosDelModo(p.Modo, todas).Take(p.Maximo).ToList();
                    string cuerpo;
                    if (ofertas.Count > 0)
                    {
                        respuesta.ConOfertas++;
                        cuerpo = p.Cuerpo != null ? TextosPushOfertas.AplicarPlantilla(p.Cuerpo, ofertas, titulo) : TextosPushOfertas.CuerpoAutomatico(ofertas);
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
                    string tituloPush = p.Titulo != null ? TextosPushOfertas.AplicarPlantilla(p.Titulo, ofertas, titulo) : titulo;
                    string imagen = p.ImagenUrl ?? (ofertas.Count > 0 ? await Foto(fotos, ofertas[0].Candidata.Producto).ConfigureAwait(false) : null);
                    NotificacionPushDTO notificacion = TextosPushOfertas.Notificacion(p.Modo, p.Campana, tituloPush, cuerpo, ofertas, imagen);
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

            if (!ensayo)
            {
                try
                {
                    await fuente.Anotar(JsonConvert.SerializeObject(new
                    {
                        Peticion = peticion,
                        respuesta.Campana,
                        respuesta.Destinatarios,
                        respuesta.ConOfertas,
                        respuesta.SinOfertas,
                        Enviados = respuesta.Mensajes.Count(m => m.Enviados > 0),
                        Dispositivos = respuesta.Mensajes.Sum(m => m.Enviados),
                        Saltados = respuesta.Saltados.Count
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
}
