using Hangfire;
using NestoAPI.Infraestructure.IndicadoresStock;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Hosting;

namespace NestoAPI.Infraestructure.Campanas.OfertasParaTi
{
    // «Ofertas para ti»: el REGISTRO, en sombra (Carlos 25/11/26: «mostrar los productos es muy útil para retroalimentar el modelo: vemos
    // lo que le gusta y hace clic, lo que ignora»). Solo se recogen datos; no se entrena nada. Firebase no vale (no lleva el cliente).
    // Patrón de las tablas en sombra del repo: dbo.OfertasClienteMostradas fuera del EDMX (Scripts/OfertasClienteMostradas.sql), ADO.NET con
    // Enlist=false, en segundo plano y sin romper ni retrasar nada; sin la tabla no se registra nada (ELMAH una vez).

    public static class OrigenesOfertaMostrada
    {
        /// <summary>La franja / pantalla de las 5 (GET api/Pedidos/Cliente/Ofertas).</summary>
        public const string CINCO = "Cinco";
        /// <summary>«Ver todas» (GET …/Ofertas/Todas), cada página.</summary>
        public const string TODAS = "Todas";
        /// <summary>Lo que se nombra o enseña con la push de campaña (solo de verdad).</summary>
        public const string PUSH = "Push";

        public static string Normalizar(string origen)
        {
            string o = origen?.Trim();
            if (string.Equals(o, TODAS, StringComparison.OrdinalIgnoreCase))
            {
                return TODAS;
            }
            return string.Equals(o, PUSH, StringComparison.OrdinalIgnoreCase) ? PUSH : CINCO;
        }
    }

    public static class AccionesEventoOferta
    {
        /// <summary>La tarjeta llegó a verse en pantalla.</summary>
        public const string VISTA = "Vista";
        /// <summary>La abrió (la ficha del producto).</summary>
        public const string ABIERTA = "Abierta";
        /// <summary>La añadió al carrito.</summary>
        public const string ANADIDA = "Anadida";

        /// <summary>La acción con su nombre del contrato, o null si no es ninguna.</summary>
        public static string Normalizar(string accion)
        {
            string a = accion?.Trim();
            if (string.Equals(a, VISTA, StringComparison.OrdinalIgnoreCase)) return VISTA;
            if (string.Equals(a, ABIERTA, StringComparison.OrdinalIgnoreCase)) return ABIERTA;
            if (string.Equals(a, ANADIDA, StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Añadida", StringComparison.OrdinalIgnoreCase)) return ANADIDA;
            return null;
        }
    }

    /// <summary>Una fila de dbo.OfertasClienteMostradas: una oferta servida a un cliente un día por un origen, con lo que hizo con ella.</summary>
    public class FilaOfertaMostrada
    {
        public long Id { get; set; }
        public DateTime Fecha { get; set; }
        public string Empresa { get; set; }
        public string Cliente { get; set; }
        public string Contacto { get; set; }
        public string Campana { get; set; }
        public string Producto { get; set; }
        public string Origen { get; set; }
        /// <summary>Lugar en lo servido (1 = la primera). Null si la fila la creó un evento sin haberse registrado al servir.</summary>
        public int? Posicion { get; set; }
        /// <summary>Lugar en el ranking completo del cliente (1 = la primera).</summary>
        public int? PosicionRanking { get; set; }
        public float? Probabilidad { get; set; }
        public string Motivo { get; set; }
        public string VersionModelo { get; set; }
        /// <summary>1 − Precio/PrecioAntes (lo que de verdad le mejora).</summary>
        public decimal? Descuento { get; set; }
        /// <summary>Precio unitario con la campaña.</summary>
        public decimal? Precio { get; set; }
        public DateTime? VistaEl { get; set; }
        public DateTime? AbiertaEl { get; set; }
        public DateTime? AnadidaEl { get; set; }
        public int? CantidadAnadida { get; set; }
        public DateTime? CompradaEl { get; set; }
        public int? PedidoCompra { get; set; }

        /// <summary>La clave de «una vez por día y origen».</summary>
        public string Clave => ReglaRegistroOfertas.Clave(Fecha, Empresa, Cliente, Producto, Origen);
    }

    /// <summary>POST api/Pedidos/Cliente/Ofertas/Eventos.</summary>
    public class PeticionEventosOfertasDTO
    {
        public string Campana { get; set; }
        public List<EventoOfertaDTO> Eventos { get; set; } = new List<EventoOfertaDTO>();
    }

    public class EventoOfertaDTO
    {
        public string Producto { get; set; }
        /// <summary>«Vista» | «Abierta» | «Anadida».</summary>
        public string Accion { get; set; }
        /// <summary>Con «Anadida», las unidades (sin dato, 1).</summary>
        public int? Cantidad { get; set; }
        /// <summary>«Cinco» | «Todas» (sin dato, «Cinco»).</summary>
        public string Origen { get; set; }
    }

    /// <summary>LAS REGLAS del registro (puras).</summary>
    public static class ReglaRegistroOfertas
    {
        public const int MAXIMO_EVENTOS = 100;
        /// <summary>Días después de enseñarla en los que un pedido del producto cuenta como compra (el mismo día incluido: 0..7).</summary>
        public const int DIAS_COMPRA = 7;

        public static string Clave(DateTime fecha, string empresa, string cliente, string producto, string origen)
            => string.Join("|", fecha.Date.ToString("yyyyMMdd"), empresa?.Trim(), cliente?.Trim(), producto?.Trim(), origen?.Trim()).ToUpperInvariant();

        /// <summary>Las filas de lo servido: posición 1.., la del ranking (1..) y los datos de la oferta.</summary>
        public static List<FilaOfertaMostrada> Filas(DateTime hoy, string empresa, string cliente, string contacto, string origen,
            IReadOnlyList<OfertaConPrecio> servidas, IReadOnlyList<OfertaElegida> ranking, string versionModelo)
        {
            var posiciones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < (ranking?.Count ?? 0); i++)
            {
                string p = ranking[i]?.Candidata?.Producto?.Trim();
                if (p != null && !posiciones.ContainsKey(p))
                {
                    posiciones[p] = i + 1;
                }
            }
            var filas = new List<FilaOfertaMostrada>();
            int posicion = 0;
            foreach (OfertaConPrecio s in servidas ?? new List<OfertaConPrecio>())
            {
                posicion++;
                if (s?.Oferta?.Candidata?.Producto == null)
                {
                    continue;
                }
                string producto = s.Oferta.Candidata.Producto.Trim();
                filas.Add(new FilaOfertaMostrada
                {
                    Fecha = hoy.Date,
                    Empresa = empresa,
                    Cliente = cliente?.Trim(),
                    Contacto = contacto?.Trim(),
                    Campana = s.Oferta.Candidata.Campana,
                    Producto = producto,
                    Origen = OrigenesOfertaMostrada.Normalizar(origen),
                    Posicion = posicion,
                    PosicionRanking = posiciones.TryGetValue(producto, out int r) ? (int?)r : null,
                    Probabilidad = s.Oferta.Probabilidad,
                    Motivo = s.Oferta.Motivo,
                    VersionModelo = s.Oferta.Probabilidad.HasValue ? versionModelo : null,
                    Descuento = s.Precios == null ? (decimal?)null : ReglaPreciosOfertaParaTi.Descuento(s.Precios),
                    Precio = s.Precios == null ? (decimal?)null : Math.Round(s.Precios.Precio, 4, MidpointRounding.AwayFromZero)
                });
            }
            return Deduplicar(filas);
        }

        /// <summary>Una oferta, una vez por cliente, día y origen: se queda la primera.</summary>
        public static List<FilaOfertaMostrada> Deduplicar(IEnumerable<FilaOfertaMostrada> filas)
        {
            var vistas = new HashSet<string>();
            return (filas ?? Enumerable.Empty<FilaOfertaMostrada>()).Where(f => f != null && vistas.Add(f.Clave)).ToList();
        }

        /// <summary>Los eventos que valen (producto y acción conocidos), como mucho <see cref="MAXIMO_EVENTOS"/>.</summary>
        public static List<EventoOfertaDTO> Eventos(PeticionEventosOfertasDTO peticion)
            => (peticion?.Eventos ?? new List<EventoOfertaDTO>())
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Producto) && AccionesEventoOferta.Normalizar(e.Accion) != null)
                .Take(MAXIMO_EVENTOS)
                .Select(e => new EventoOfertaDTO
                {
                    Producto = e.Producto.Trim(),
                    Accion = AccionesEventoOferta.Normalizar(e.Accion),
                    Cantidad = Math.Max(1, Math.Min(9999, e.Cantidad ?? 1)),
                    Origen = OrigenesOfertaMostrada.Normalizar(e.Origen)
                })
                .ToList();

        /// <summary>
        /// Un evento sobre la fila de ese día (o una nueva, sin posición, si al servirla no se registró): cada momento se apunta la primera vez;
        /// abrir o añadir implica haberla visto; añadir suma las unidades.
        /// </summary>
        public static FilaOfertaMostrada Aplicar(FilaOfertaMostrada existente, EventoOfertaDTO evento, DateTime ahora, string empresa, string cliente, string campana)
        {
            FilaOfertaMostrada fila = existente ?? new FilaOfertaMostrada
            {
                Fecha = ahora.Date,
                Empresa = empresa,
                Cliente = cliente?.Trim(),
                Campana = string.IsNullOrWhiteSpace(campana) ? null : campana.Trim(),
                Producto = evento.Producto?.Trim(),
                Origen = OrigenesOfertaMostrada.Normalizar(evento.Origen)
            };
            fila.VistaEl = fila.VistaEl ?? ahora;
            if (evento.Accion == AccionesEventoOferta.ABIERTA)
            {
                fila.AbiertaEl = fila.AbiertaEl ?? ahora;
            }
            if (evento.Accion == AccionesEventoOferta.ANADIDA)
            {
                fila.AnadidaEl = fila.AnadidaEl ?? ahora;
                fila.CantidadAnadida = (fila.CantidadAnadida ?? 0) + Math.Max(1, evento.Cantidad ?? 1);
            }
            return fila;
        }

        /// <summary>
        /// La compra de una oferta enseñada: el primer pedido del cliente con ese producto del día en que se le enseñó a los
        /// <see cref="DIAS_COMPRA"/> siguientes (cualquier canal). Null si no hay.
        /// </summary>
        public static CompraTrasOferta PrimeraCompra(FilaOfertaMostrada fila, IEnumerable<CompraTrasOferta> compras)
        {
            if (fila == null)
            {
                return null;
            }
            DateTime desde = fila.Fecha.Date;
            DateTime hasta = desde.AddDays(DIAS_COMPRA + 1);
            return (compras ?? Enumerable.Empty<CompraTrasOferta>())
                .Where(c => c != null && c.Id == fila.Id && c.Fecha >= desde && c.Fecha < hasta)
                .OrderBy(c => c.Fecha)
                .ThenBy(c => c.Pedido)
                .FirstOrDefault();
        }
    }

    /// <summary>Un pedido del producto de una fila (Id) después de enseñárselo.</summary>
    public class CompraTrasOferta
    {
        public long Id { get; set; }
        public int Pedido { get; set; }
        public DateTime Fecha { get; set; }
    }

    // ------------------------------------------------------------------------------------------------ resultado (GET api/Campanas/Ofertas/Resultado)

    public class FilaResultadoOferta
    {
        public string Origen { get; set; }
        public string Motivo { get; set; }
        public float? Probabilidad { get; set; }
        public bool Vista { get; set; }
        public bool Abierta { get; set; }
        public bool Anadida { get; set; }
        public bool Comprada { get; set; }
        /// <summary>Al menos una fila servida (Posicion no nula); las que solo crea un evento no cuentan como servidas.</summary>
        public bool Servida { get; set; }
    }

    public class GrupoResultadoOfertasDTO
    {
        public string Grupo { get; set; }
        /// <summary>Solo en los deciles: el tramo de probabilidad.</summary>
        public float? ProbabilidadDesde { get; set; }
        public float? ProbabilidadHasta { get; set; }
        public int Servidas { get; set; }
        public int Vistas { get; set; }
        public int Abiertas { get; set; }
        public int Anadidas { get; set; }
        public int Compradas { get; set; }
        /// <summary>Sobre las servidas, en tanto por uno a 4 decimales (null sin servidas).</summary>
        public decimal? PorcentajeVistas { get; set; }
        public decimal? PorcentajeAbiertas { get; set; }
        public decimal? PorcentajeAnadidas { get; set; }
        public decimal? PorcentajeCompradas { get; set; }
    }

    public class ResultadoOfertasMostradasDTO
    {
        public string Campana { get; set; }
        public int Dias { get; set; }
        public string Desde { get; set; }
        public GrupoResultadoOfertasDTO Total { get; set; }
        public List<GrupoResultadoOfertasDTO> PorOrigen { get; set; } = new List<GrupoResultadoOfertasDTO>();
        public List<GrupoResultadoOfertasDTO> PorMotivo { get; set; } = new List<GrupoResultadoOfertasDTO>();
        /// <summary>Las servidas con probabilidad del modelo, en 10 tramos iguales de más a menos (decil 1 = las de más probabilidad).</summary>
        public List<GrupoResultadoOfertasDTO> PorDecil { get; set; } = new List<GrupoResultadoOfertasDTO>();
    }

    public static class ReglaResultadoOfertas
    {
        public const int DIAS_POR_DEFECTO = 30;
        public const int DIAS_TOPE = 366;

        public static ResultadoOfertasMostradasDTO Resumir(IEnumerable<FilaResultadoOferta> filas, string campana, int dias, DateTime desde)
        {
            List<FilaResultadoOferta> servidas = (filas ?? Enumerable.Empty<FilaResultadoOferta>()).Where(f => f != null && f.Servida).ToList();
            var r = new ResultadoOfertasMostradasDTO
            {
                Campana = campana,
                Dias = dias,
                Desde = desde.ToString("yyyy-MM-dd"),
                Total = Grupo("Total", servidas)
            };
            r.PorOrigen = servidas.GroupBy(f => f.Origen ?? "(sin origen)").OrderBy(g => g.Key).Select(g => Grupo(g.Key, g.ToList())).ToList();
            r.PorMotivo = servidas.GroupBy(f => f.Motivo ?? "(sin motivo)").OrderByDescending(g => g.Count()).ThenBy(g => g.Key)
                .Select(g => Grupo(g.Key, g.ToList())).ToList();
            List<FilaResultadoOferta> conProbabilidad = servidas.Where(f => f.Probabilidad.HasValue).OrderByDescending(f => f.Probabilidad.Value).ToList();
            int n = conProbabilidad.Count;
            for (int d = 0; d < 10 && n > 0; d++)
            {
                int desdeIndice = d * n / 10;
                int hastaIndice = (d + 1) * n / 10;
                List<FilaResultadoOferta> tramo = conProbabilidad.Skip(desdeIndice).Take(hastaIndice - desdeIndice).ToList();
                if (tramo.Count == 0)
                {
                    continue;
                }
                GrupoResultadoOfertasDTO g = Grupo("Decil " + (d + 1), tramo);
                g.ProbabilidadHasta = tramo.Max(f => f.Probabilidad.Value);
                g.ProbabilidadDesde = tramo.Min(f => f.Probabilidad.Value);
                r.PorDecil.Add(g);
            }
            return r;
        }

        private static GrupoResultadoOfertasDTO Grupo(string nombre, List<FilaResultadoOferta> filas)
        {
            int servidas = filas.Count;
            decimal? Porcentaje(int x) => servidas == 0 ? (decimal?)null : Math.Round((decimal)x / servidas, 4, MidpointRounding.AwayFromZero);
            int vistas = filas.Count(f => f.Vista), abiertas = filas.Count(f => f.Abierta), anadidas = filas.Count(f => f.Anadida), compradas = filas.Count(f => f.Comprada);
            return new GrupoResultadoOfertasDTO
            {
                Grupo = nombre,
                Servidas = servidas,
                Vistas = vistas,
                Abiertas = abiertas,
                Anadidas = anadidas,
                Compradas = compradas,
                PorcentajeVistas = Porcentaje(vistas),
                PorcentajeAbiertas = Porcentaje(abiertas),
                PorcentajeAnadidas = Porcentaje(anadidas),
                PorcentajeCompradas = Porcentaje(compradas)
            };
        }
    }

    // ------------------------------------------------------------------------------------------------ dónde se guarda

    public interface IRepositorioOfertasMostradas
    {
        bool TablaExiste();
        /// <summary>Inserta las que no estén ya (misma clave del día).</summary>
        void Insertar(IReadOnlyList<FilaOfertaMostrada> filas);
        FilaOfertaMostrada Leer(DateTime fecha, string empresa, string cliente, string producto, string origen);
        /// <summary>Guarda los momentos y la cantidad (UPDATE si tiene Id; si no, INSERT).</summary>
        void GuardarEvento(FilaOfertaMostrada fila);
        /// <summary>Hasta <paramref name="maximo"/> filas sin compra enseñadas desde <paramref name="desde"/>, a partir del Id <paramref name="despuesDe"/>.</summary>
        List<FilaOfertaMostrada> LeerSinCompra(DateTime desde, long despuesDe, int maximo);
        /// <summary>Los pedidos de esas filas (una consulta por tanda).</summary>
        List<CompraTrasOferta> LeerCompras(IReadOnlyList<long> ids);
        void MarcarCompra(long id, DateTime compradaEl, int pedido);
        List<FilaResultadoOferta> LeerResultado(string campana, DateTime desde);
    }

    /// <summary>Lo que usan los servicios: nunca lanza ni espera (en segundo plano).</summary>
    public interface IRegistroOfertasMostradas
    {
        void Servidas(IReadOnlyList<FilaOfertaMostrada> filas);
        void Eventos(string empresa, string cliente, PeticionEventosOfertasDTO peticion, DateTime ahora);
    }

    public class RegistroOfertasMostradas : IRegistroOfertasMostradas
    {
        private static int avisadoSinTabla;
        private readonly Func<IRepositorioOfertasMostradas> repositorio;
        private readonly Action<Action> lanzar;
        private readonly Action<Exception> registrar;

        /// <param name="lanzar">Cómo se lanza el trabajo (por defecto, en segundo plano; en las pruebas, en el acto).</param>
        public RegistroOfertasMostradas(Func<IRepositorioOfertasMostradas> repositorio, Action<Action> lanzar = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.lanzar = lanzar ?? EnSegundoPlano;
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public static RegistroOfertasMostradas Produccion() => new RegistroOfertasMostradas(RepositorioOfertasMostradasSql.DesdeConfiguracion);

        public void Servidas(IReadOnlyList<FilaOfertaMostrada> filas)
        {
            List<FilaOfertaMostrada> lista = ReglaRegistroOfertas.Deduplicar(filas);
            if (lista.Count == 0)
            {
                return;
            }
            Lanzar(repo => repo.Insertar(lista));
        }

        public void Eventos(string empresa, string cliente, PeticionEventosOfertasDTO peticion, DateTime ahora)
        {
            List<EventoOfertaDTO> eventos = ReglaRegistroOfertas.Eventos(peticion);
            if (eventos.Count == 0 || string.IsNullOrWhiteSpace(cliente))
            {
                return;
            }
            string campana = peticion?.Campana;
            Lanzar(repo =>
            {
                foreach (EventoOfertaDTO e in eventos)
                {
                    FilaOfertaMostrada existente = repo.Leer(ahora.Date, empresa, cliente, e.Producto, e.Origen);
                    repo.GuardarEvento(ReglaRegistroOfertas.Aplicar(existente, e, ahora, empresa, cliente, campana));
                }
            });
        }

        private void Lanzar(Action<IRepositorioOfertasMostradas> trabajo)
        {
            try
            {
                lanzar(() =>
                {
                    try
                    {
                        IRepositorioOfertasMostradas repo = repositorio();
                        if (!repo.TablaExiste())
                        {
                            if (System.Threading.Interlocked.Exchange(ref avisadoSinTabla, 1) == 0)
                            {
                                registrar(new Exception("[Ofertas para ti] No existe dbo.OfertasClienteMostradas (Scripts/OfertasClienteMostradas.sql): no se registra lo que se enseña."));
                            }
                            return;
                        }
                        trabajo(repo);
                    }
                    catch (Exception ex)
                    {
                        registrar(new Exception($"[Ofertas para ti] No se ha podido registrar lo enseñado: {ex.Message}", ex));
                    }
                });
            }
            catch (Exception ex)
            {
                registrar(new Exception($"[Ofertas para ti] No se ha podido lanzar el registro: {ex.Message}", ex));
            }
        }

        private static void EnSegundoPlano(Action trabajo)
        {
            if (HostingEnvironment.IsHosted)
            {
                HostingEnvironment.QueueBackgroundWorkItem(_ => trabajo());
            }
            else
            {
                _ = Task.Run(trabajo);
            }
        }
    }

    /// <summary>ADO.NET sobre NestoConnection con <c>Enlist=false</c> (nunca en la transacción ambiente), como los registros en sombra (#704).</summary>
    public class RepositorioOfertasMostradasSql : IRepositorioOfertasMostradas
    {
        internal const string NOMBRE_APLICACION = "NestoAPI OfertasMostradas";
        internal const int TIMEOUT_SEGUNDOS = 30;
        internal static readonly TimeSpan REINTENTO_SIN_TABLA = TimeSpan.FromMinutes(10);
        internal const string SQL_EXISTE = "SELECT CASE WHEN OBJECT_ID('dbo.OfertasClienteMostradas', 'U') IS NULL THEN 0 ELSE 1 END";

        internal const string COLUMNAS = "Id, Fecha, Empresa, Cliente, Contacto, Campana, Producto, Origen, Posicion, PosicionRanking, Probabilidad, Motivo, " +
            "VersionModelo, Descuento, Precio, VistaEl, AbiertaEl, AnadidaEl, CantidadAnadida, CompradaEl, PedidoCompra";

        internal const string SQL_INSERTAR = @"
IF NOT EXISTS (SELECT 1 FROM dbo.OfertasClienteMostradas WHERE Fecha = @fecha AND Empresa = @empresa AND Cliente = @cliente AND Producto = @producto AND Origen = @origen)
    INSERT INTO dbo.OfertasClienteMostradas (Fecha, Empresa, Cliente, Contacto, Campana, Producto, Origen, Posicion, PosicionRanking, Probabilidad, Motivo,
        VersionModelo, Descuento, Precio, VistaEl, AbiertaEl, AnadidaEl, CantidadAnadida)
    VALUES (@fecha, @empresa, @cliente, @contacto, @campana, @producto, @origen, @posicion, @posicionRanking, @probabilidad, @motivo,
        @versionModelo, @descuento, @precio, @vistaEl, @abiertaEl, @anadidaEl, @cantidadAnadida)";

        internal const string SQL_LEER = "SELECT " + COLUMNAS + @" FROM dbo.OfertasClienteMostradas
WHERE Fecha = @fecha AND Empresa = @empresa AND Cliente = @cliente AND Producto = @producto AND Origen = @origen";

        internal const string SQL_EVENTO = @"
UPDATE dbo.OfertasClienteMostradas SET VistaEl = @vistaEl, AbiertaEl = @abiertaEl, AnadidaEl = @anadidaEl, CantidadAnadida = @cantidadAnadida
WHERE Id = @id";

        internal const string SQL_SIN_COMPRA = "SELECT TOP (@maximo) " + COLUMNAS + @" FROM dbo.OfertasClienteMostradas
WHERE CompradaEl IS NULL AND Fecha >= @desde AND Id > @despuesDe ORDER BY Id";

        /// <summary>Una consulta por tanda: los pedidos (no presupuestos) del cliente con el producto desde el día en que se le enseñó.</summary>
        internal const string SQL_COMPRAS = @"
SELECT m.Id, l.Número AS Pedido, CAST(c.Fecha AS date) AS Fecha
FROM dbo.OfertasClienteMostradas m
INNER JOIN LinPedidoVta l WITH (NOLOCK) ON l.Empresa = m.Empresa AND l.[Nº Cliente] = m.Cliente AND l.Producto = m.Producto
INNER JOIN CabPedidoVta c WITH (NOLOCK) ON c.Empresa = l.Empresa AND c.Número = l.Número
WHERE m.Id IN ({0}) AND l.Estado >= -1 AND l.Cantidad > 0
  AND c.Fecha >= m.Fecha AND c.Fecha < DATEADD(day, @dias + 1, m.Fecha)";

        internal const string SQL_MARCAR = "UPDATE dbo.OfertasClienteMostradas SET CompradaEl = @compradaEl, PedidoCompra = @pedido WHERE Id = @id AND CompradaEl IS NULL";

        internal const string SQL_RESULTADO = @"
SELECT Origen, Motivo, Probabilidad, CAST(CASE WHEN VistaEl IS NULL THEN 0 ELSE 1 END AS bit) AS Vista,
    CAST(CASE WHEN AbiertaEl IS NULL THEN 0 ELSE 1 END AS bit) AS Abierta, CAST(CASE WHEN AnadidaEl IS NULL THEN 0 ELSE 1 END AS bit) AS Anadida,
    CAST(CASE WHEN CompradaEl IS NULL THEN 0 ELSE 1 END AS bit) AS Comprada, CAST(CASE WHEN Posicion IS NULL THEN 0 ELSE 1 END AS bit) AS Servida
FROM dbo.OfertasClienteMostradas WITH (NOLOCK)
WHERE Fecha >= @desde AND (@campana IS NULL OR Campana = @campana)";

        private static readonly object cerrojo = new object();
        private static bool existe;
        private static DateTime ultimaComprobacion = DateTime.MinValue;

        private readonly string cadenaConexion;

        public RepositorioOfertasMostradasSql(string cadenaConexion)
        {
            if (string.IsNullOrWhiteSpace(cadenaConexion))
            {
                throw new ArgumentNullException(nameof(cadenaConexion));
            }
            this.cadenaConexion = new SqlConnectionStringBuilder(cadenaConexion) { Enlist = false }.ConnectionString;
        }

        public static RepositorioOfertasMostradasSql DesdeConfiguracion()
        {
            ConnectionStringSettings origen = ConfigurationManager.ConnectionStrings["NestoConnection"]
                ?? throw new ConfigurationErrorsException("Falta la cadena de conexión NestoConnection");
            return new RepositorioOfertasMostradasSql(new SqlConnectionStringBuilder(origen.ConnectionString) { ApplicationName = NOMBRE_APLICACION }.ConnectionString);
        }

        public bool TablaExiste()
        {
            lock (cerrojo)
            {
                if (existe || DateTime.Now - ultimaComprobacion < REINTENTO_SIN_TABLA)
                {
                    return existe;
                }
            }
            bool hay;
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_EXISTE))
            {
                hay = Convert.ToInt32(comando.ExecuteScalar()) == 1;
            }
            lock (cerrojo)
            {
                existe = hay;
                ultimaComprobacion = DateTime.Now;
            }
            return hay;
        }

        public void Insertar(IReadOnlyList<FilaOfertaMostrada> filas)
        {
            using (SqlConnection conexion = Abrir())
            {
                foreach (FilaOfertaMostrada f in filas ?? new List<FilaOfertaMostrada>())
                {
                    using (SqlCommand comando = Comando(conexion, SQL_INSERTAR))
                    {
                        ParametrosFila(comando, f);
                        try
                        {
                            _ = comando.ExecuteNonQuery();
                        }
                        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                        {
                            // otra petición la ha insertado a la vez: ya está (índice único)
                        }
                    }
                }
            }
        }

        public FilaOfertaMostrada Leer(DateTime fecha, string empresa, string cliente, string producto, string origen)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_LEER))
            {
                comando.Parameters.Add("@fecha", SqlDbType.Date).Value = fecha.Date;
                Texto(comando, "@empresa", empresa, 3);
                Texto(comando, "@cliente", cliente, 10);
                Texto(comando, "@producto", producto, 15);
                Texto(comando, "@origen", origen, 10);
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    return r.Read() ? LeerFila(r) : null;
                }
            }
        }

        public void GuardarEvento(FilaOfertaMostrada fila)
        {
            if (fila.Id == 0)
            {
                Insertar(new[] { fila });
                FilaOfertaMostrada ya = Leer(fila.Fecha, fila.Empresa, fila.Cliente, fila.Producto, fila.Origen);
                if (ya == null || (ya.VistaEl == fila.VistaEl && ya.AbiertaEl == fila.AbiertaEl && ya.AnadidaEl == fila.AnadidaEl))
                {
                    return;
                }
                fila.Id = ya.Id; // la insertó otra petición a la vez: se actualiza la suya
            }
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_EVENTO))
            {
                comando.Parameters.Add("@id", SqlDbType.BigInt).Value = fila.Id;
                Fecha(comando, "@vistaEl", fila.VistaEl);
                Fecha(comando, "@abiertaEl", fila.AbiertaEl);
                Fecha(comando, "@anadidaEl", fila.AnadidaEl);
                comando.Parameters.Add("@cantidadAnadida", SqlDbType.Int).Value = (object)fila.CantidadAnadida ?? DBNull.Value;
                _ = comando.ExecuteNonQuery();
            }
        }

        public List<FilaOfertaMostrada> LeerSinCompra(DateTime desde, long despuesDe, int maximo)
        {
            var filas = new List<FilaOfertaMostrada>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_SIN_COMPRA))
            {
                comando.Parameters.Add("@maximo", SqlDbType.Int).Value = maximo;
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                comando.Parameters.Add("@despuesDe", SqlDbType.BigInt).Value = despuesDe;
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        filas.Add(LeerFila(r));
                    }
                }
            }
            return filas;
        }

        public List<CompraTrasOferta> LeerCompras(IReadOnlyList<long> ids)
        {
            var compras = new List<CompraTrasOferta>();
            if (ids == null || ids.Count == 0)
            {
                return compras;
            }
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, string.Format(SQL_COMPRAS, string.Join(", ", ids.Select((_, i) => "@i" + i)))))
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    comando.Parameters.Add("@i" + i, SqlDbType.BigInt).Value = ids[i];
                }
                comando.Parameters.Add("@dias", SqlDbType.Int).Value = ReglaRegistroOfertas.DIAS_COMPRA;
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        compras.Add(new CompraTrasOferta { Id = r.GetInt64(0), Pedido = r.GetInt32(1), Fecha = r.GetDateTime(2) });
                    }
                }
            }
            return compras;
        }

        public void MarcarCompra(long id, DateTime compradaEl, int pedido)
        {
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_MARCAR))
            {
                comando.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
                comando.Parameters.Add("@compradaEl", SqlDbType.DateTime2).Value = compradaEl;
                comando.Parameters.Add("@pedido", SqlDbType.Int).Value = pedido;
                _ = comando.ExecuteNonQuery();
            }
        }

        public List<FilaResultadoOferta> LeerResultado(string campana, DateTime desde)
        {
            var filas = new List<FilaResultadoOferta>();
            using (SqlConnection conexion = Abrir())
            using (SqlCommand comando = Comando(conexion, SQL_RESULTADO))
            {
                comando.Parameters.Add("@desde", SqlDbType.Date).Value = desde.Date;
                comando.Parameters.Add("@campana", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(campana) ? (object)DBNull.Value : campana.Trim();
                using (SqlDataReader r = comando.ExecuteReader())
                {
                    while (r.Read())
                    {
                        filas.Add(new FilaResultadoOferta
                        {
                            Origen = r.IsDBNull(0) ? null : r.GetString(0),
                            Motivo = r.IsDBNull(1) ? null : r.GetString(1),
                            Probabilidad = r.IsDBNull(2) ? (float?)null : r.GetFloat(2),
                            Vista = r.GetBoolean(3),
                            Abierta = r.GetBoolean(4),
                            Anadida = r.GetBoolean(5),
                            Comprada = r.GetBoolean(6),
                            Servida = r.GetBoolean(7)
                        });
                    }
                }
            }
            return filas;
        }

        private static FilaOfertaMostrada LeerFila(SqlDataReader r)
        {
            string S(int i) => r.IsDBNull(i) ? null : r.GetString(i).Trim();
            DateTime? F(int i) => r.IsDBNull(i) ? (DateTime?)null : r.GetDateTime(i);
            int? N(int i) => r.IsDBNull(i) ? (int?)null : r.GetInt32(i);
            decimal? D(int i) => r.IsDBNull(i) ? (decimal?)null : r.GetDecimal(i);
            return new FilaOfertaMostrada
            {
                Id = r.GetInt64(0),
                Fecha = r.GetDateTime(1),
                Empresa = S(2),
                Cliente = S(3),
                Contacto = S(4),
                Campana = S(5),
                Producto = S(6),
                Origen = S(7),
                Posicion = N(8),
                PosicionRanking = N(9),
                Probabilidad = r.IsDBNull(10) ? (float?)null : r.GetFloat(10),
                Motivo = S(11),
                VersionModelo = S(12),
                Descuento = D(13),
                Precio = D(14),
                VistaEl = F(15),
                AbiertaEl = F(16),
                AnadidaEl = F(17),
                CantidadAnadida = N(18),
                CompradaEl = F(19),
                PedidoCompra = N(20)
            };
        }

        private static void ParametrosFila(SqlCommand c, FilaOfertaMostrada f)
        {
            c.Parameters.Add("@fecha", SqlDbType.Date).Value = f.Fecha.Date;
            Texto(c, "@empresa", f.Empresa, 3);
            Texto(c, "@cliente", f.Cliente, 10);
            Texto(c, "@contacto", f.Contacto, 3);
            NTexto(c, "@campana", f.Campana, 50);
            Texto(c, "@producto", f.Producto, 15);
            Texto(c, "@origen", f.Origen, 10);
            c.Parameters.Add("@posicion", SqlDbType.Int).Value = (object)f.Posicion ?? DBNull.Value;
            c.Parameters.Add("@posicionRanking", SqlDbType.Int).Value = (object)f.PosicionRanking ?? DBNull.Value;
            c.Parameters.Add("@probabilidad", SqlDbType.Real).Value = (object)f.Probabilidad ?? DBNull.Value;
            Texto(c, "@motivo", f.Motivo, 30);
            Texto(c, "@versionModelo", f.VersionModelo, 50);
            SqlParameter descuento = c.Parameters.Add("@descuento", SqlDbType.Decimal);
            descuento.Precision = 9;
            descuento.Scale = 4;
            descuento.Value = (object)f.Descuento ?? DBNull.Value;
            SqlParameter precio = c.Parameters.Add("@precio", SqlDbType.Decimal);
            precio.Precision = 12;
            precio.Scale = 4;
            precio.Value = (object)f.Precio ?? DBNull.Value;
            Fecha(c, "@vistaEl", f.VistaEl);
            Fecha(c, "@abiertaEl", f.AbiertaEl);
            Fecha(c, "@anadidaEl", f.AnadidaEl);
            c.Parameters.Add("@cantidadAnadida", SqlDbType.Int).Value = (object)f.CantidadAnadida ?? DBNull.Value;
        }

        private static void Fecha(SqlCommand c, string nombre, DateTime? valor)
            => c.Parameters.Add(nombre, SqlDbType.DateTime2).Value = (object)valor ?? DBNull.Value;

        private static void Texto(SqlCommand c, string nombre, string valor, int largo)
        {
            string limpio = valor?.Trim();
            if (!string.IsNullOrEmpty(limpio) && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            c.Parameters.Add(nombre, SqlDbType.VarChar, largo).Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio;
        }

        private static void NTexto(SqlCommand c, string nombre, string valor, int largo)
        {
            string limpio = valor?.Trim();
            if (!string.IsNullOrEmpty(limpio) && limpio.Length > largo)
            {
                limpio = limpio.Substring(0, largo);
            }
            c.Parameters.Add(nombre, SqlDbType.NVarChar, largo).Value = string.IsNullOrEmpty(limpio) ? (object)DBNull.Value : limpio;
        }

        private SqlConnection Abrir()
        {
            var conexion = new SqlConnection(cadenaConexion);
            conexion.Open();
            return conexion;
        }

        private static SqlCommand Comando(SqlConnection conexion, string sql)
            => new SqlCommand(sql, conexion) { CommandTimeout = TIMEOUT_SEGUNDOS };
    }

    /// <summary>
    /// Job «ofertas-cliente-compras» (3:35 Madrid, cola «indicadores»): marca CompradaEl/PedidoCompra en lo enseñado de los últimos
    /// <see cref="ReglaRegistroOfertas.DIAS_COMPRA"/> + 1 días cuando el cliente pidió el producto (cualquier canal). Por tandas de
    /// <see cref="TANDA"/> filas, una consulta de pedidos por tanda; idempotente (solo las que aún no tienen compra). Sin la tabla, nada.
    /// </summary>
    public static class CompraOfertasMostradasJobsService
    {
        public const string JOB_ID = "ofertas-cliente-compras";
        public const string CRON = "35 3 * * *";
        public const int TANDA = 500;
        private const string USUARIO_ELMAH = "Sistema (ofertas para ti)";

        [Queue(IndicadoresStockJobsService.COLA)]
        [AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]
        [DisableConcurrentExecution(timeoutInSeconds: 1800)]
        public static void Marcar()
        {
            try
            {
                int marcadas = Marcar(RepositorioOfertasMostradasSql.DesdeConfiguracion(), DateTime.Today);
                Console.WriteLine($"[Ofertas para ti] Compras de lo enseñado marcadas: {marcadas}");
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Ofertas para ti] Error al marcar las compras de lo enseñado: {ex.Message}", ex), USUARIO_ELMAH);
            }
        }

        /// <summary>El núcleo (con el repositorio inyectado): cuántas ha marcado.</summary>
        internal static int Marcar(IRepositorioOfertasMostradas repositorio, DateTime hoy)
        {
            if (!repositorio.TablaExiste())
            {
                return 0;
            }
            DateTime desde = hoy.Date.AddDays(-(ReglaRegistroOfertas.DIAS_COMPRA + 1));
            long ultimo = 0;
            int marcadas = 0;
            while (true)
            {
                List<FilaOfertaMostrada> tanda = repositorio.LeerSinCompra(desde, ultimo, TANDA);
                if (tanda.Count == 0)
                {
                    break;
                }
                List<CompraTrasOferta> compras = repositorio.LeerCompras(tanda.Select(f => f.Id).ToList());
                foreach (FilaOfertaMostrada fila in tanda)
                {
                    CompraTrasOferta compra = ReglaRegistroOfertas.PrimeraCompra(fila, compras);
                    if (compra != null)
                    {
                        repositorio.MarcarCompra(fila.Id, compra.Fecha, compra.Pedido);
                        marcadas++;
                    }
                }
                ultimo = tanda.Max(f => f.Id);
                if (tanda.Count < TANDA)
                {
                    break;
                }
            }
            return marcadas;
        }
    }
}
